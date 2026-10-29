using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Windows;

internal sealed record UpdateRelease(Version Version, Uri Download, long Size, string Sha256);

internal sealed class PortableUpdate : IDisposable
{
    private readonly HttpClient _http;
    internal const string Repository = "https://github.com/philliu-212/radio";
    internal static string PackageMode(string directory) => File.Exists(Path.Combine(directory, "coreclr.dll")) ? "Full" : "Lite";
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
        var prefix = $"Radio-Portable-{tag}-{mode}-win-x64-";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a =>
            Regex.IsMatch(a.GetProperty("name").GetString() ?? "", "^" + Regex.Escape(prefix) + @"\d{8}-\d{6}\.zip$")).ToArray();
        if (assets.Length != 1) return null;
        var asset = assets[0];
        var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$")) return null;
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
            || url.Scheme != "https" || !url.AbsoluteUri.StartsWith(Repository + "/releases/download/" + tag + "/", StringComparison.Ordinal)) return null;
        var size = asset.GetProperty("size").GetInt64();
        return size is > 0 and <= 300_000_000 ? new(version, url, size, digest[7..]) : null;
    }

    internal async Task<string> PrepareAsync(UpdateRelease release, IProgress<int> progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        token = timeout.Token;
        var work = Path.Combine(Path.GetTempPath(), "RadioMini-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var zip = Path.Combine(work, "update.zip");
            using (var response = await _http.GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(zip);
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("下載大小與 Release 不符。");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    progress.Report((int)(total * 100 / release.Size));
                }
                if (total != release.Size) throw new InvalidDataException("下載不完整。");
            }
            await Task.Run(() => ValidateAndExtract(zip, Path.Combine(work, "payload"), release.Sha256), token);
            return work;
        }
        catch { Directory.Delete(work, true); throw; }
    }

    internal static void ValidateAndExtract(string zip, string destination, string digest)
    {
        using (var stream = File.OpenRead(zip))
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新檔 SHA-256 驗證失敗。");
        using var archive = ZipFile.OpenRead(zip);
        string? root = null;
        long expanded = 0;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Publish-Release creates one top-level portable directory. Reject traversal,
        // links, duplicate paths and user settings before writing any payload files.
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Replace('\\', '/').Split('/');
            if (parts.Length < 2 || parts.Any(p => p is "." or ".." || p.Contains(':')) || string.IsNullOrEmpty(parts[0])
                || parts.Skip(1).SkipLast(1).Any(string.IsNullOrEmpty)
                || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("更新 ZIP 路徑不安全。");
            root ??= parts[0];
            if (parts[0] != root) throw new InvalidDataException("更新 ZIP 包含多個根目錄。");
            if (entry.Name.Length == 0) continue;
            var relative = string.Join('/', parts.Skip(1));
            if (parts.Any(p => p.Equals("radio.settings.json", StringComparison.OrdinalIgnoreCase))
                || !files.Add(relative) || parts.Any(p => p.EndsWith('.') || p.EndsWith(' ')))
                throw new InvalidDataException("更新 ZIP 包含設定或重複路徑。");
            expanded += entry.Length;
            if (expanded > 1_000_000_000 || files.Count > 5000) throw new InvalidDataException("更新 ZIP 過大。");
        }
        foreach (var required in new[] { "RadioMini.exe", "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll" })
            if (!files.Contains(required)) throw new InvalidDataException("更新 ZIP 缺少播放器檔案。");
        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0))
        {
            var relative = entry.FullName.Replace('\\', '/').Split('/').Skip(1).ToArray();
            var path = Path.Combine(destination, Path.Combine(relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path);
        }
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
        using (var output = File.Create(script)) resource.CopyTo(output);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Work", work, "-Destination", AppContext.BaseDirectory, "-ParentId", Environment.ProcessId.ToString() }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("無法啟動更新程序。");
        try
        {
            for (var i = 0; i < 150; i++)
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
