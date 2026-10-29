using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Radio.Update;

namespace Radio.Windows;

internal sealed record UpdateRelease(Version Version, Uri Download, long Size, string Sha256, string Mode);
internal enum UpdatePhase { Idle, Downloading, Preparing, Ready, Failed }
internal sealed record UpdateView(UpdatePhase Phase, Version? Pending, int Percent, string Message);

internal sealed class PortableUpdate : IDisposable
{
    internal const string Repository = "https://github.com/philliu-212/radio";
    private readonly HttpClient _http;
    private readonly Store? _store;
    private readonly Version _current;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _checking = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _preparation;
    private Task _task = Task.CompletedTask;
    private UpdateRelease? _candidate;
    private int _failures;
    private Version? _pending;
    private long _nextCheckTicks;
    internal Version? LatestVersion { get; private set; }
    internal UpdateView View { get; private set; } = new(UpdatePhase.Idle, null, 0, "");
    internal event Action<UpdateView>? Changed;
    internal static bool IsPortable => OtaRuntime.Store is not null;
    internal DateTimeOffset NextCheck => new(Interlocked.Read(ref _nextCheckTicks), TimeSpan.Zero);
    internal Task Preparation => _task;

    public PortableUpdate(HttpMessageHandler? handler = null, Store? store = null, Version? current = null, Func<DateTimeOffset>? clock = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioMini/" + MainForm.AppVersion);
        _store = store ?? OtaRuntime.Store;
        _current = current ?? Version.Parse(MainForm.AppVersion);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }
    private Version? Pending() => _pending;
    private void Publish(UpdatePhase phase, int percent, string message)
    {
        View = new(phase, Pending(), percent, message);
        Changed?.Invoke(View);
    }
    internal Task TickAsync() => _clock() >= NextCheck ? CheckNowAsync() : Task.CompletedTask;
    internal Task CheckNowAsync() => Task.Run(CheckCoreAsync);
    private async Task CheckCoreAsync()
    {
        if (_store is null || _lifetime.IsCancellationRequested || !await _checking.WaitAsync(0)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync("https://api.github.com/repos/philliu-212/radio/releases/latest", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var json = await ReadBoundedAsync(response.Content, 2_000_000, timeout.Token);
            using (var metadata = JsonDocument.Parse(json)) LatestVersion = StableVersion(metadata.RootElement);
            var state = _store.Load();
            _pending = state.Pending is null ? null : Version.Parse(state.Pending.Version);
            var release = ParseRelease(json, _current, state.Current.Mode);
            Interlocked.Exchange(ref _nextCheckTicks, _clock().AddHours(6).UtcTicks);
            if (release is null)
            {
                if (_task.IsCompleted) Publish(state.Pending is null ? UpdatePhase.Idle : UpdatePhase.Ready, 100, state.Pending is null ? "" : "更新已備妥，重新啟動並更新");
                return;
            }
            if (state.Pending is not null && Version.Parse(state.Pending.Version) >= release.Version)
            {
                // Durable pending survives normal closes and reboots. Verify once
                // per check in the background, never download the same ready build.
                await Task.Run(() => _store.Verify(state.Pending), timeout.Token);
                Publish(UpdatePhase.Ready, 100, "更新已備妥，重新啟動並更新"); return;
            }
            if (!_task.IsCompleted && _candidate is not null && _candidate.Version >= release.Version) return;
            if (!_task.IsCompleted)
            {
                _preparation?.Cancel();
                await _task;
            }
            _preparation?.Dispose();
            _preparation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _candidate = release;
            var preparationToken = _preparation.Token;
            _task = Task.Run(() => PrepareAndCommitAsync(release, preparationToken));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (Expected(error)) { Failure(error); }
        finally { _checking.Release(); }
    }
    private void Failure(Exception error)
    {
        Interlocked.Exchange(ref _nextCheckTicks, _clock().AddSeconds(Math.Min(900, 15 * Math.Pow(2, Math.Min(6, Interlocked.Increment(ref _failures) - 1)))).UtcTicks);
        try
        {
            Publish(UpdatePhase.Failed, 0, "更新失敗，稍後自動重試" + (Pending() is null ? "" : "；先前的更新仍可重新啟動套用"));
            if (_store is not null) File.WriteAllText(Path.Combine(_store.Ota, "last-error.txt"), error.GetType().Name + ": " + error.Message);
        }
        catch (Exception failure) when (Expected(failure)) { /* Read-only/invalid state must not crash playback. */ }
    }
    private async Task PrepareAndCommitAsync(UpdateRelease release, CancellationToken token)
    {
        try
        {
            using var lease = await AcquireUpdateLockAsync(token);
            var state = _store!.Load();
            if (state.Current.Version != _current.ToString(3)) throw new InvalidDataException("Running app and active package pointer differ; update deferred.");
            await Task.Run(_store.CleanupStaging, token);
            if (state.Pending is not null && Version.Parse(state.Pending.Version) >= release.Version) { Publish(UpdatePhase.Ready, 100, "更新已備妥"); return; }
            var work = Path.Combine(_store.Ota, "staging", Guid.NewGuid().ToString("N"));
            Store.RejectLinks(work); Directory.CreateDirectory(work);
            try
            {
                Publish(UpdatePhase.Downloading, 0, "背景下載更新中");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(30));
                var zip = Path.Combine(work, "download.part");
                await DownloadAsync(release, zip, timeout.Token);
                Publish(UpdatePhase.Preparing, 100, "背景驗證與準備中");
                var package = await Task.Run(() => ExtractAndVerify(zip, Path.Combine(work, "unpacked"), release, token), token);
                token.ThrowIfCancellationRequested();
                var target = _store.PackagePath(package);
                var staged = Path.Combine(work, "unpacked", "package", package.DirectoryName);
                if (Directory.Exists(target))
                {
                    var existing = Store.Read<Package>(Path.Combine(target, "package.json")); existing.Validate();
                    if (existing.Commit != package.Commit || existing.Version != package.Version || existing.Mode != package.Mode)
                        throw new InvalidDataException("Commit directory identity collision.");
                    await Task.Run(() => _store.Verify(package), token);
                }
                else { Store.RejectLinks(target); Directory.Move(staged, target); }
                token.ThrowIfCancellationRequested();
                state = _store.Load();
                if (state.Current.Version != _current.ToString(3)) throw new InvalidDataException("Active version changed during preparation.");
                // Only commit when everything is durable. A failed newer candidate
                // never replaces a known-good older Pending.
                if (state.Pending is null || Version.Parse(state.Pending.Version) < release.Version)
                { state.Pending = package; _store.Save(state); }
                _pending = Version.Parse(state.Pending.Version);
                await Task.Run(() => _store.Cleanup(state), token);
                Interlocked.Exchange(ref _failures, 0);
                Publish(UpdatePhase.Ready, 100, "更新已備妥，重新啟動並更新");
            }
            finally { Store.RejectTreeLinks(work); Directory.Delete(work, true); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (Expected(error)) { Failure(error); }
    }
    private async Task<FileStream> AcquireUpdateLockAsync(CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return _store!.Lock("update"); }
            catch (IOException) when (attempt < 20) { await Task.Delay(100, token); }
        }
    }
    internal static UpdateRelease? ParseRelease(string json, Version current, string mode)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = StableVersion(root);
        if (version is null || version <= current) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var pattern = "^" + Regex.Escape($"Radio-Portable-{tag}-{mode}-win-x64-") + @"\d{8}-\d{6}\.zip$";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => Regex.IsMatch(a.GetProperty("name").GetString() ?? "", pattern)).ToArray();
        if (assets.Length != 1) return null;
        var asset = assets[0];
        var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("Release ZIP has no trusted SHA-256 digest.");
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !url.AbsoluteUri.StartsWith(Repository + "/releases/download/" + tag + "/", StringComparison.Ordinal)) throw new InvalidDataException("Foreign release URL.");
        var size = asset.GetProperty("size").GetInt64();
        return size is > 0 and <= 400_000_000 ? new(version, url, size, digest[7..], mode) : null;
    }
    private static Version? StableVersion(JsonElement root)
    {
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        return Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") && Version.TryParse(tag[1..], out var version) ? version : null;
    }
    private async Task DownloadAsync(UpdateRelease release, string path, CancellationToken token)
    {
        using var response = await _http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920]; long total = 0; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            total += count; if (total > release.Size) throw new InvalidDataException("ZIP exceeds declared size.");
            hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token);
            Publish(UpdatePhase.Downloading, (int)(total * 100 / release.Size), "背景下載更新中");
        }
        if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP SHA-256/length mismatch.");
        output.Flush(true);
    }
    internal static Package ExtractAndVerify(string zip, string destination, UpdateRelease release, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 6000) throw new InvalidDataException("Too many ZIP entries.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string? root = null; long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var full = entry.FullName.Replace('\\', '/'); var parts = full.TrimEnd('/').Split('/');
            if (parts.Length < 1 || !Store.SafePath(string.Join('/', parts)) || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("Unsafe ZIP entry.");
            root ??= parts[0]; if (root != parts[0]) throw new InvalidDataException("ZIP has multiple roots.");
            if (entry.Name.Length == 0) continue;
            if (parts.Length < 2 || !paths.Add(string.Join('/', parts.Skip(1)))) throw new InvalidDataException("Duplicate ZIP entry.");
            total += entry.Length; if (entry.Length > 400_000_000 || total > 1_500_000_000) throw new InvalidDataException("ZIP expands too much.");
        }
        // Strip the portable ZIP's human-readable top folder into a fixed staging
        // name. Never trust a ZIP path as an absolute destination.
        foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0))
        {
            var relative = string.Join('/', entry.FullName.Replace('\\', '/').Split('/').Skip(1));
            var path = Path.Combine(destination, "package", relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920]; long length = 0; int count;
            while ((count = input.Read(buffer)) > 0)
            { token.ThrowIfCancellationRequested(); length += count; if (length > entry.Length) throw new InvalidDataException("ZIP entry length mismatch."); output.Write(buffer, 0, count); }
            if (length != entry.Length) throw new InvalidDataException("Truncated ZIP entry."); output.Flush(true);
        }
        var packageRoot = Path.Combine(destination, "package");
        var folders = Directory.GetDirectories(packageRoot).Where(p => Regex.IsMatch(Path.GetFileName(p), "^[a-f0-9]{12}$")).ToArray();
        if (folders.Length != 1 || !File.Exists(Path.Combine(packageRoot, "RadioMini.exe"))) throw new InvalidDataException("Not a launcher portable ZIP.");
        var package = Store.Read<Package>(Path.Combine(folders[0], "package.json")); package.Validate();
        if (package.Version != release.Version.ToString(3) || package.Mode != release.Mode || Path.GetFileName(folders[0]) != package.DirectoryName) throw new InvalidDataException("ZIP version/mode/commit mismatch.");
        var allowed = package.Files.Select(f => package.DirectoryName + "/" + f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        allowed.UnionWith(["RadioMini.exe", "READ-ME.txt", "THIRD-PARTY-NOTICES.txt", "data/ota/state.json", package.DirectoryName + "/package.json"]);
        if (paths.Any(p => !allowed.Contains(p))) throw new InvalidDataException("Unlisted ZIP resource/data.");
        var extracted = new Store(packageRoot);
        var bootstrap = extracted.Load();
        if (bootstrap.Current.Commit != package.Commit || bootstrap.Current.Version != package.Version || bootstrap.Current.Mode != package.Mode ||
            bootstrap.Pending is not null || bootstrap.Previous is not null || !string.IsNullOrEmpty(bootstrap.Trial) ||
            bootstrap.Current.Files.Count != package.Files.Count || package.Files.Any(file => !bootstrap.Current.Files.Any(other => other.Path == file.Path && other.Size == file.Size && other.Sha256 == file.Sha256)))
            throw new InvalidDataException("Portable bootstrap state and resources differ.");
        extracted.Verify(package);
        return package;
    }
    private static async Task<string> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token); using var output = new MemoryStream();
        var buffer = new byte[16384]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0) { if (output.Length + count > limit) throw new InvalidDataException("Release response too large."); output.Write(buffer, 0, count); }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or System.Runtime.Serialization.SerializationException;
    internal async Task StopPreparationAsync() { _preparation?.Cancel(); await _task; }
    public void Dispose() { _lifetime.Cancel(); _preparation?.Cancel(); _http.Dispose(); }
}
