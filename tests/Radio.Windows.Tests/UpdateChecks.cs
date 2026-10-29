using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Radio.Windows.Tests;

internal static class UpdateChecks
{
    private static readonly string PowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
    private static readonly string[] Required = ["RadioMini.exe", "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll"];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "RadioMini-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckReleases();
            foreach (var mode in new[] { "Lite", "Full" })
            {
                var fixture = Path.Combine(root, mode);
                var installed = Path.Combine(fixture, "installed");
                var source = Path.Combine(fixture, "new");
                var assets = Path.Combine(fixture, "assets");
                Directory.CreateDirectory(installed); Directory.CreateDirectory(source);
                foreach (var name in Required)
                {
                    File.WriteAllText(Path.Combine(installed, name), name + " unchanged");
                    File.WriteAllText(Path.Combine(source, name), name + " unchanged");
                }
                if (mode == "Full")
                {
                    File.WriteAllText(Path.Combine(installed, "coreclr.dll"), new string('r', 100000));
                    File.Copy(Path.Combine(installed, "coreclr.dll"), Path.Combine(source, "coreclr.dll"));
                }
                File.WriteAllText(Path.Combine(installed, "removed.dll"), "old managed file");
                File.WriteAllText(Path.Combine(installed, "RadioMini.dll"), "old app");
                File.WriteAllText(Path.Combine(source, "RadioMini.dll"), "new app");
                Directory.CreateDirectory(Path.Combine(source, "sub"));
                File.WriteAllText(Path.Combine(source, "sub", "added.dll"), "new added file");
                await GenerateAsync(installed, Path.Combine(fixture, "old-assets"), "0.4.0", mode);
                await GenerateAsync(source, assets, "0.5.0", mode);
                // Untracked local files must never be uploaded, replaced or removed.
                File.WriteAllText(Path.Combine(installed, "radio.settings.json"), "user settings");
                File.WriteAllText(Path.Combine(installed, "personal.txt"), "user file");
                var baseline = File.ReadAllBytes(Path.Combine(installed, PortableUpdate.InstalledManifest));
                var manifestName = $"Radio-Update-v0.5.0-{mode}-win-x64.json";
                var manifest = File.ReadAllBytes(Path.Combine(assets, manifestName));
                var release = new UpdateRelease(new Version(0, 5, 0), new Uri($"{PortableUpdate.Repository}/releases/download/v0.5.0/{manifestName}"), manifest.Length, Hash(manifest), mode);
                Check(PortableUpdate.PackageMode(installed) == mode, mode + ": mode detection");
                var files = PortableUpdate.ParseManifest(Encoding.UTF8.GetString(manifest), release);
                Check(files.Count == Required.Length + 1 + (mode == "Full" ? 1 : 0), mode + ": publisher manifest matches exact package inventory");
                CheckManifestSafety(manifest, release);

                // Failure late in the transaction must restore modifications,
                // additions, deletions and the old inventory as a single unit.
                using (var handler = new AssetFixture(assets))
                using (var updater = new PortableUpdate(handler))
                {
                    var work = await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed);
                    try
                    {
                        AssertSelectiveDownload(handler, files, mode, 2);
                        using (File.Open(Path.Combine(installed, PortableUpdate.InstalledManifest), FileMode.Open, FileAccess.Read, FileShare.Read))
                            await InstallAsync(work, installed, false);
                        Check(File.ReadAllText(Path.Combine(installed, "RadioMini.dll")) == "old app", mode + ": modified file rolls back");
                        Check(!File.Exists(Path.Combine(installed, "sub", "added.dll")), mode + ": added file rolls back");
                        Check(File.ReadAllText(Path.Combine(installed, "removed.dll")) == "old managed file", mode + ": deleted file rolls back");
                        Check(File.Exists(Path.Combine(work, "backup", "removed.dll")), mode + ": failure occurs after managed deletion was reached");
                        Check(File.ReadAllBytes(Path.Combine(installed, PortableUpdate.InstalledManifest)).SequenceEqual(baseline), mode + ": inventory rolls back");
                    }
                    finally { Directory.Delete(work, true); }
                }

                File.WriteAllText(Path.Combine(installed, "removed.dll"), "user-modified old file");
                using (var updater = new PortableUpdate(new AssetFixture(assets)))
                    await RejectAsync(() => updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed), mode + ": refuse deleting a locally modified managed file");
                File.WriteAllText(Path.Combine(installed, "removed.dll"), "old managed file");

                Directory.CreateDirectory(Path.Combine(installed, "sub"));
                var collision = Path.Combine(installed, "sub", "added.dll");
                File.WriteAllText(collision, "personal collision");
                using (var updater = new PortableUpdate(new AssetFixture(assets)))
                    await RejectAsync(() => updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed), mode + ": new file cannot overwrite untracked user file");
                Check(File.ReadAllText(collision) == "personal collision", mode + ": conflicting user file left untouched");
                File.Delete(collision);

                // An unchanged local DLL can change while downloading: revalidate
                // after actual player exit before modifying/deleting anything.
                using (var updater = new PortableUpdate(new AssetFixture(assets)))
                {
                    var work = await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed);
                    try
                    {
                        await InstallAsync(work, installed, false, () => File.WriteAllText(Path.Combine(installed, "Radio.Core.dll"), "late modification"));
                        Check(File.ReadAllText(Path.Combine(installed, "RadioMini.dll")) == "old app" && File.Exists(Path.Combine(installed, "removed.dll")), mode + ": changed baseline aborts without partial install");
                    }
                    finally { Directory.Delete(work, true); }
                }
                File.WriteAllText(Path.Combine(installed, "Radio.Core.dll"), "Radio.Core.dll unchanged");

                // Test a missing managed file too. Its absence must download that
                // file, not an entire ZIP; existing exe and runtime stay untouched.
                File.Delete(Path.Combine(installed, "Radio.Core.dll"));
                var unchangedTime = File.GetLastWriteTimeUtc(Path.Combine(installed, "RadioMini.exe"));
                using (var handler = new AssetFixture(assets))
                using (var updater = new PortableUpdate(handler))
                {
                    var progress = new DownloadProgress();
                    var work = await updater.PrepareAsync(release, progress, CancellationToken.None, installed);
                    try
                    {
                        AssertSelectiveDownload(handler, files, mode, 3);
                        Check(progress.Percent == 100 && !File.Exists(Path.Combine(work, "payload", "RadioMini.exe")), mode + ": stage only new/changed/missing files");
                        await InstallAsync(work, installed, true);
                    }
                    finally { Directory.Delete(work, true); }
                }
                Check(File.ReadAllText(Path.Combine(installed, "RadioMini.dll")) == "new app", mode + ": modified file updated");
                Check(File.ReadAllText(Path.Combine(installed, "sub", "added.dll")) == "new added file", mode + ": new nested file added");
                Check(!File.Exists(Path.Combine(installed, "removed.dll")), mode + ": obsolete managed file deleted");
                Check(File.ReadAllText(Path.Combine(installed, "Radio.Core.dll")) == "Radio.Core.dll unchanged", mode + ": missing file repaired");
                Check(File.GetLastWriteTimeUtc(Path.Combine(installed, "RadioMini.exe")) == unchangedTime, mode + ": unchanged exe is not rewritten");
                Check(File.ReadAllText(Path.Combine(installed, "radio.settings.json")) == "user settings" && File.ReadAllText(Path.Combine(installed, "personal.txt")) == "user file", mode + ": settings and untracked files preserved");
                Check(File.ReadAllBytes(Path.Combine(installed, PortableUpdate.InstalledManifest)).SequenceEqual(manifest), mode + ": new inventory installed");
                using (var handler = new AssetFixture(assets))
                using (var updater = new PortableUpdate(handler))
                {
                    var work = await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed);
                    try { Check(handler.Requests.Count == 1, mode + ": fully matching installation downloads only manifest"); }
                    finally { Directory.Delete(work, true); }
                }
                if (mode == "Full") File.WriteAllText(Path.Combine(installed, "coreclr.dll"), "damaged runtime");
                else File.WriteAllText(Path.Combine(installed, "RadioMini.dll"), "damaged app");
                using (var handler = new AssetFixture(assets))
                using (var updater = new PortableUpdate(handler))
                {
                    var work = await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed);
                    try { Check(handler.Requests.Count == 2, mode + ": corrupt app/runtime downloads only that file"); }
                    finally { Directory.Delete(work, true); }
                }
                if (mode == "Full")
                {
                    File.Delete(Path.Combine(installed, "coreclr.dll"));
                    Check(PortableUpdate.PackageMode(installed) == "Full", "Full: installed inventory retains mode when runtime DLL is missing");
                    using var handler = new AssetFixture(assets);
                    using var updater = new PortableUpdate(handler);
                    var work = await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed);
                    try { Check(handler.Requests.Count == 2 && File.Exists(Path.Combine(work, "payload", "coreclr.dll")), "Full: missing runtime downloads only that file"); }
                    finally { Directory.Delete(work, true); }
                }
                await CheckDownloadFailuresAsync(assets, installed, release, files);
                // A pre-inventory installation can update without guessing what
                // old files belong to us. Untracked files are never deleted.
                File.Delete(Path.Combine(installed, PortableUpdate.InstalledManifest));
                File.WriteAllText(Path.Combine(installed, "legacy-personal.dll"), "keep");
                using (var updater = new PortableUpdate(new AssetFixture(assets)))
                {
                    var work = await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed);
                    try { await InstallAsync(work, installed, true); }
                    finally { Directory.Delete(work, true); }
                }
                Check(File.Exists(Path.Combine(installed, "legacy-personal.dll")), mode + ": missing old inventory never causes speculative deletion");
            }
            Console.WriteLine("All offline incremental update checks passed.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CheckReleases()
    {
        string Release(string tag = "v0.5.0", string mode = "Lite", string digest = "", bool prerelease = false, bool draft = false, string? url = null, bool legacy = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag, draft, prerelease,
            assets = new[] { new { name = legacy ? $"Radio-Portable-{tag}-{mode}-win-x64-20261029-123456.zip" : $"Radio-Update-{tag}-{mode}-win-x64.json", size = 100,
                digest = digest == "" ? "sha256:" + new string('a', 64) : digest,
                browser_download_url = url ?? $"{PortableUpdate.Repository}/releases/download/{tag}/manifest.json" } }
        });
        var current = new Version(0, 4, 0);
        Check(PortableUpdate.ParseRelease(Release(), current, "Lite")?.Version == new Version(0, 5, 0), "Stable incremental release selected");
        Check(PortableUpdate.ParseRelease(Release(tag: "v0.10.0"), current, "Lite") is not null, "Numeric version comparison");
        foreach (var tag in new[] { "v0.4.0", "v0.3.0", "v0.5.0-rc1" }) Check(PortableUpdate.ParseRelease(Release(tag), current, "Lite") is null, "Reject non-new/stable version: " + tag);
        Check(PortableUpdate.ParseRelease(Release(prerelease: true), current, "Lite") is null && PortableUpdate.ParseRelease(Release(draft: true), current, "Lite") is null, "Ignore draft and prerelease");
        Check(PortableUpdate.ParseRelease(Release(), current, "Full") is null && PortableUpdate.ParseRelease(Release(mode: "Full"), current, "Full")?.Mode == "Full", "Lite/Full use matching manifests");
        Check(PortableUpdate.ParseRelease(Release(digest: "sha256:bad"), current, "Lite") is null, "Reject missing/invalid manifest digest");
        Check(PortableUpdate.ParseRelease(Release(url: "https://example.com/manifest.json"), current, "Lite") is null, "Reject foreign release URL");
        Check(PortableUpdate.ParseRelease(Release(legacy: true), current, "Lite") is null, "No fallback to full ZIP download");
    }

    private static void CheckManifestSafety(byte[] manifest, UpdateRelease release)
    {
        foreach (var path in new[] { "../escape.dll", "sub/../../escape.dll", "C:/escape.dll", "/escape.dll", "radio.settings.json", "sub/radio.settings.json", "radio.update-manifest.json", "CON.dll", "sub//a.dll", "file.", "sub\\a.dll", "RadioMini.exe" })
        {
            var node = JsonNode.Parse(manifest)!;
            node["files"]![1]!["path"] = path;
            Reject(() => PortableUpdate.ParseManifest(node.ToJsonString(), release), "Reject unsafe/duplicate path: " + path);
        }
        foreach (var field in new[] { "schema", "version", "mode", "architecture" })
        {
            var node = JsonNode.Parse(manifest)!;
            node[field] = field == "schema" ? JsonValue.Create(99) : JsonValue.Create("wrong");
            Reject(() => PortableUpdate.ParseManifest(node.ToJsonString(), release), "Reject manifest mismatch: " + field);
        }
        var missing = JsonNode.Parse(manifest)!;
        missing["files"]!.AsArray().RemoveAt(0);
        Reject(() => PortableUpdate.ParseManifest(missing.ToJsonString(), release), "Reject incomplete inventory");
    }

    private static void AssertSelectiveDownload(AssetFixture handler, IReadOnlyList<UpdateFile> files, string mode, int count)
    {
        Check(handler.Requests.Count == count + 1 && handler.Requests.All(name => !name.EndsWith(".zip")), mode + ": requests only manifest plus changed files");
        foreach (var file in files.Where(file => file.Path is "RadioMini.exe" or "coreclr.dll"))
            Check(!handler.Requests.Contains(file.Asset), mode + ": no unchanged apphost/runtime download: " + file.Path);
    }

    private static async Task CheckDownloadFailuresAsync(string assets, string installed, UpdateRelease release, IReadOnlyList<UpdateFile> files)
    {
        using (var updater = new PortableUpdate(new AssetFixture(assets)))
        {
            await RejectAsync(() => updater.PrepareAsync(release with { Size = release.Size + 1 }, new DownloadProgress(), CancellationToken.None, installed), "Truncated manifest rejected");
            await RejectAsync(() => updater.PrepareAsync(release with { Size = release.Size - 1 }, new DownloadProgress(), CancellationToken.None, installed), "Oversized manifest rejected");
            await RejectAsync(() => updater.PrepareAsync(release with { Sha256 = new string('0', 64) }, new DownloadProgress(), CancellationToken.None, installed), "Manifest hash mismatch rejected");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await updater.PrepareAsync(release, new DownloadProgress(), cancelled.Token, installed); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "Cancellation prevents installer preparation"); }
        }
        foreach (var failure in new[] { "corrupt", "missing", "truncated", "oversized" })
        {
            using var updater = new PortableUpdate(new AssetFixture(assets, failure));
            try { await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None, installed); throw new Exception("Bad object accepted"); }
            catch (Exception error) when (error is InvalidDataException or HttpRequestException) { Check(true, "Changed-file download failure aborts: " + failure); }
        }
        foreach (var failure in new[] { "raw-hash", "expanded-size" })
        {
            var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(assets, Path.GetFileName(release.Download.AbsolutePath))))!;
            var damaged = files.Single(file => file.Path == (release.Mode == "Full" ? "coreclr.dll" : "RadioMini.dll"));
            var entry = node["files"]!.AsArray().Single(file => file!["path"]!.GetValue<string>() == damaged.Path)!;
            if (failure == "raw-hash") entry["sha256"] = new string('0', 64);
            else entry["size"] = 1;
            var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
            using var updater = new PortableUpdate(new AssetFixture(assets, manifestOverride: bytes));
            await RejectAsync(() => updater.PrepareAsync(release with { Size = bytes.Length, Sha256 = Hash(bytes) }, new DownloadProgress(), CancellationToken.None, installed), "Reject invalid decompressed object: " + failure);
        }
    }

    private static async Task GenerateAsync(string input, string output, string version, string mode)
    {
        var info = PowerShellInfo();
        foreach (var arg in new[] { "-File", Path.GetFullPath("scripts/New-UpdateAssets.ps1"), "-InputDirectory", input, "-OutputDirectory", output, "-Version", version, "-Mode", mode }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Exception(await process.StandardError.ReadToEndAsync());
    }

    private static async Task InstallAsync(string work, string destination, bool success, Action? afterReady = null)
    {
        var before = File.ReadAllBytes(Path.Combine(destination, "RadioMini.dll"));
        var script = Path.Combine(work, "install.ps1");
        using (var input = typeof(MainForm).Assembly.GetManifestResourceStream("Radio.Windows.UpdateInstaller.ps1")!)
        using (var reader = new StreamReader(input)) File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
        using var parent = Process.Start(new ProcessStartInfo(PowerShell, "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"") { UseShellExecute = false, CreateNoWindow = true })!;
        var info = PowerShellInfo();
        foreach (var arg in new[] { "-File", script, "-Work", work, "-Destination", destination, "-ParentId", parent.Id.ToString(), "-NoRestart" }) info.ArgumentList.Add(arg);
        using var installer = Process.Start(info)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (!File.Exists(Path.Combine(work, "ready")))
            {
                if (installer.HasExited) throw new Exception(File.ReadAllText(Path.Combine(work, "result.txt")) + await installer.StandardError.ReadToEndAsync());
                await Task.Delay(50, timeout.Token);
            }
            Check(File.ReadAllBytes(Path.Combine(destination, "RadioMini.dll")).SequenceEqual(before), "Installer waits for actual player exit");
            afterReady?.Invoke();
            parent.Kill();
            await installer.WaitForExitAsync(timeout.Token);
            if (installer.ExitCode != (success ? 0 : 1)) throw new Exception(File.ReadAllText(Path.Combine(work, "result.txt")));
            Check(true, success ? "External incremental installer succeeds" : "External incremental installer safely rejects/rolls back");
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
            if (!installer.HasExited) { installer.Kill(); await installer.WaitForExitAsync(); }
        }
    }

    private static ProcessStartInfo PowerShellInfo()
    {
        var info = new ProcessStartInfo(PowerShell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }) info.ArgumentList.Add(arg);
        return info;
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }
    private static async Task RejectAsync(Func<Task<string>> action, string message)
    {
        try { await action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }
    private sealed class DownloadProgress : IProgress<int>
    {
        internal int Percent { get; private set; }
        public void Report(int value) => Percent = value;
    }
    private sealed class AssetFixture(string directory, string? failure = null, byte[]? manifestOverride = null) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);
            Requests.Add(name);
            var path = Path.Combine(directory, name);
            if (!File.Exists(path) || (name.EndsWith(".gz") && failure == "missing")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var bytes = name.EndsWith(".json") && manifestOverride is not null ? manifestOverride.ToArray() : File.ReadAllBytes(path);
            if (name.EndsWith(".gz"))
            {
                if (failure == "corrupt") bytes[0] ^= 0xff;
                if (failure == "truncated") bytes = bytes[..^1];
                if (failure == "oversized") bytes = [.. bytes, 0];
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
