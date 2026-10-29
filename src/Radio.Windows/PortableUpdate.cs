using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Windows;

internal sealed record UpdateRelease(Version Version, Uri Download, long Size, string Sha256, string Mode);
internal sealed record UpdateFile(string Path, long Size, string Sha256, string Asset, long DownloadSize, string DownloadSha256);
internal sealed record UpdateOperation(string Path, long Size, string Sha256, bool Changed, bool Delete = false, bool ExpectMissing = false);

internal sealed class PortableUpdate : IDisposable
{
    private readonly HttpClient _http;
    internal const string Repository = "https://github.com/philliu-212/radio";
    internal const string InstalledManifest = "radio.update-manifest.json";
    internal static string PackageMode(string directory)
    {
        var manifest = Path.Combine(directory, InstalledManifest);
        RejectLinks(manifest);
        if (!File.Exists(manifest)) return File.Exists(Path.Combine(directory, "coreclr.dll")) ? "Full" : "Lite";
        if (new FileInfo(manifest).Length > 2_000_000) throw new InvalidDataException("本機更新清單過大。");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        return document.RootElement.GetProperty("mode").GetString() switch
        {
            "Lite" => "Lite",
            "Full" => "Full",
            _ => throw new InvalidDataException("本機更新清單類型不符。")
        };
    }
    internal static bool IsPortable => string.Equals(Environment.ProcessPath, Path.Combine(AppContext.BaseDirectory, "RadioMini.exe"), StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(AppContext.BaseDirectory, "RadioMini.pdb"));

    public PortableUpdate(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioMini/" + MainForm.AppVersion);
    }

    internal async Task<UpdateRelease?> CheckAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync("https://api.github.com/repos/philliu-212/radio/releases/latest", timeout.Token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        return ParseRelease(json, Version.Parse(MainForm.AppVersion), PackageMode(AppContext.BaseDirectory));
    }

    internal static UpdateRelease? ParseRelease(string json, Version current, string mode)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version) || version <= current) return null;
        var name = $"Radio-Update-{tag}-{mode}-win-x64.json";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a =>
            a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) return null;
        var asset = assets[0];
        var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$")) return null;
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
            || url.Scheme != "https" || !url.AbsoluteUri.StartsWith(Repository + "/releases/download/" + tag + "/", StringComparison.Ordinal)) return null;
        var size = asset.GetProperty("size").GetInt64();
        return size is > 0 and <= 2_000_000 ? new(version, url, size, digest[7..], mode) : null;
    }

    internal static IReadOnlyList<UpdateFile> ParseManifest(string json, UpdateRelease release)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("version").GetString() != release.Version.ToString(3)
            || root.GetProperty("mode").GetString() != release.Mode || root.GetProperty("architecture").GetString() != "win-x64")
            throw new InvalidDataException("更新清單版本、類型或格式不符。");
        var files = new List<UpdateFile>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in root.GetProperty("files").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString() ?? "";
            var hash = entry.GetProperty("sha256").GetString() ?? "";
            var downloadHash = entry.GetProperty("downloadSha256").GetString() ?? "";
            var asset = entry.GetProperty("asset").GetString() ?? "";
            var size = entry.GetProperty("size").GetInt64();
            var downloadSize = entry.GetProperty("downloadSize").GetInt64();
            if (!SafePath(path) || !paths.Add(path) || !Regex.IsMatch(hash, "^[a-f0-9]{64}$")
                || !Regex.IsMatch(downloadHash, "^[a-f0-9]{64}$") || asset != $"Radio-File-{downloadHash}.gz"
                || size is < 0 or > 300_000_000 || downloadSize is <= 0 or > 300_000_000)
                throw new InvalidDataException("更新清單含不安全或重複的檔案。");
            total += size;
            if (total > 1_000_000_000 || files.Count >= 5000) throw new InvalidDataException("更新清單過大。");
            files.Add(new(path, size, hash, asset, downloadSize, downloadHash));
        }
        foreach (var file in files)
        {
            var parent = file.Path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (paths.Contains(parent)) throw new InvalidDataException("更新清單的檔案與目錄衝突。");
            }
        }
        foreach (var required in new[] { "RadioMini.exe", "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll" })
            if (!paths.Contains(required)) throw new InvalidDataException("更新清單缺少播放器檔案。");
        if (paths.Contains("coreclr.dll") != (release.Mode == "Full")) throw new InvalidDataException("更新清單的 runtime 類型不符。");
        return files;
    }

    private static bool SafePath(string path) => path.Length is > 0 and <= 240 && path.Split('/').All(part =>
        Regex.IsMatch(part, @"^[A-Za-z0-9_.-]+$") && part is not "." and not ".." && !part.EndsWith('.')
        && !part.Equals("radio.settings.json", StringComparison.OrdinalIgnoreCase)
        && !part.Equals(InstalledManifest, StringComparison.OrdinalIgnoreCase)
        && !Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase));

    internal static void RejectLinks(string path)
    {
        for (var current = new FileInfo(path); current is not null; current = current.Directory is { } parent ? new FileInfo(parent.FullName) : null)
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新路徑含 junction 或符號連結。");
    }

    private static bool Matches(string path, UpdateFile file)
    {
        RejectLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<string> PrepareAsync(UpdateRelease release, IProgress<int> progress, CancellationToken token, string? installedDirectory = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        token = timeout.Token;
        var work = Path.Combine(Path.GetTempPath(), "RadioMini-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var manifest = Path.Combine(work, "manifest.json");
            await DownloadAsync(release.Download, manifest, release.Size, release.Sha256, null, token);
            var files = ParseManifest(await File.ReadAllTextAsync(manifest, token), release);
            var installed = installedDirectory ?? AppContext.BaseDirectory;
            var previousManifest = Path.Combine(installed, InstalledManifest);
            RejectLinks(previousManifest);
            IReadOnlyList<UpdateFile> previous = [];
            if (File.Exists(previousManifest))
            {
                if (new FileInfo(previousManifest).Length > 2_000_000) throw new InvalidDataException("本機更新清單過大。");
                var previousJson = await File.ReadAllTextAsync(previousManifest, token);
                using var previousDocument = JsonDocument.Parse(previousJson);
                if (!Version.TryParse(previousDocument.RootElement.GetProperty("version").GetString(), out var previousVersion) || previousVersion > release.Version)
                    throw new InvalidDataException("本機更新清單版本不符。");
                previous = ParseManifest(previousJson, release with { Version = previousVersion });
            }
            var newPaths = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = previous.Where(file => !newPaths.Contains(file.Path)).ToArray();
            if (removed.Any(old => newPaths.Any(path => path.StartsWith(old.Path + "/", StringComparison.OrdinalIgnoreCase)
                || old.Path.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("更新涉及檔案與目錄互換，請備份設定後手動更新。");
            var changed = await Task.Run(() => files.Where(file =>
            {
                token.ThrowIfCancellationRequested();
                return !Matches(Path.Combine(installed, file.Path), file);
            }).ToArray(), token);
            if (File.Exists(previousManifest))
            {
                var previousPaths = previous.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in changed.Where(file => !previousPaths.Contains(file.Path)))
                    if (File.Exists(Path.Combine(installed, file.Path)) || Directory.Exists(Path.Combine(installed, file.Path)))
                        throw new InvalidDataException($"新版檔案與未追蹤的本機檔案衝突：{file.Path}。請先備份／移走後重試。");
            }
            await Task.Run(() =>
            {
                foreach (var file in removed)
                {
                    token.ThrowIfCancellationRequested();
                    var path = Path.Combine(installed, file.Path);
                    RejectLinks(path);
                    if (File.Exists(path) && !Matches(path, file))
                        throw new InvalidDataException($"待移除的舊檔已被修改：{file.Path}。請先備份／移走該檔後重試。");
                }
            }, token);
            var totalSize = changed.Sum(file => file.DownloadSize);
            var missingPaths = changed.Where(file => !File.Exists(Path.Combine(installed, file.Path))).Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long downloaded = 0;
            foreach (var file in changed)
            {
                var compressed = Path.Combine(work, "file.gz");
                var url = new Uri($"{Repository}/releases/download/v{release.Version.ToString(3)}/{file.Asset}");
                await DownloadAsync(url, compressed, file.DownloadSize, file.DownloadSha256,
                    count => progress.Report((int)((downloaded + count) * 100 / totalSize)), token);
                var target = Path.Combine(work, "payload", file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await ExpandAsync(compressed, target, file, token);
                File.Delete(compressed);
                downloaded += file.DownloadSize;
            }
            // The installer revalidates both staged changes and unchanged local files
            // after exit, before touching any installed file. No ZIP fallback exists.
            var changedPaths = changed.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var operations = files.Select(file => new UpdateOperation(file.Path, file.Size, file.Sha256, changedPaths.Contains(file.Path), ExpectMissing: missingPaths.Contains(file.Path)))
                .Concat(removed.Select(file => new UpdateOperation(file.Path, file.Size, file.Sha256, true, true))).ToList();
            var stagedManifest = Path.Combine(work, "payload", InstalledManifest);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedManifest)!);
            File.Copy(manifest, stagedManifest);
            operations.Add(new(InstalledManifest, release.Size, release.Sha256, true));
            await File.WriteAllTextAsync(Path.Combine(work, "plan.json"), JsonSerializer.Serialize(new
            {
                files = operations
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), token);
            progress.Report(100);
            return work;
        }
        catch { Directory.Delete(work, true); throw; }
    }

    private async Task DownloadAsync(Uri url, string destination, long size, string hash, Action<long>? progress, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(destination);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            total += count;
            if (total > size) throw new InvalidDataException("下載大小與更新清單不符。");
            digest.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            progress?.Invoke(total);
        }
        if (total != size || !Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新檔不完整或 SHA-256 驗證失敗。");
    }

    private static async Task ExpandAsync(string compressed, string target, UpdateFile file, CancellationToken token)
    {
        await using var input = File.OpenRead(compressed);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await using var output = File.Create(target);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await gzip.ReadAsync(buffer, token)) > 0)
        {
            total += count;
            if (total > file.Size) throw new InvalidDataException("解壓檔案超過清單大小。");
            digest.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (total != file.Size || !Convert.ToHexString(digest.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("解壓檔案 SHA-256 驗證失敗。");
    }

    internal static async Task StartInstallerAsync(string work, CancellationToken token)
    {
        // Probe write access before stopping audio. The script checks again and rolls
        // back every replaced file if another process still has a DLL locked.
        var probe = Path.Combine(AppContext.BaseDirectory, ".update-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { }
        File.Delete(probe);
        var script = Path.Combine(work, "install.ps1");
        using (var resource = typeof(PortableUpdate).Assembly.GetManifestResourceStream("Radio.Windows.UpdateInstaller.ps1")!)
        using (var reader = new StreamReader(resource))
            File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Work", work, "-Destination", AppContext.BaseDirectory, "-ParentId", Environment.ProcessId.ToString() }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("無法啟動更新程序。");
        try
        {
            for (var i = 0; i < 600; i++)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(Path.Combine(work, "ready"))) return;
                if (process.HasExited)
                {
                    var log = Path.Combine(work, "result.txt");
                    throw new IOException("更新程序無法執行，播放器尚未退出。請手動下載新版。" +
                        (File.Exists(log) ? "\n" + File.ReadAllText(log) : ""));
                }
                await Task.Delay(100, token);
            }
            throw new IOException("更新程序啟動逾時，請手動下載新版。");
        }
        catch
        {
            // A cancelled/failed handshake must not later install after UI exit.
            if (!process.HasExited) process.Kill();
            throw;
        }
    }

    public void Dispose() => _http.Dispose();
}
