using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Radio.Windows;

namespace Radio.Windows.Tests;

internal static class UpdateChecks
{
    internal static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RadioMini-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string Release(string tag = "v0.5.0", string mode = "Lite", string? digest = null, bool prerelease = false, string? url = null) => JsonSerializer.Serialize(new
            {
                tag_name = tag, draft = false, prerelease,
                assets = new[] { new { name = $"Radio-Portable-{tag}-{mode}-win-x64-20261029-123456.zip", size = 100,
                    digest = digest ?? "sha256:" + new string('a', 64),
                    browser_download_url = url ?? $"{PortableUpdate.Repository}/releases/download/{tag}/package.zip" } }
            });
            var current = new Version(0, 4, 0);
            Check(PortableUpdate.ParseRelease(Release(), current, "Lite")?.Version == new Version(0, 5, 0), "New stable matching package is selected");
            Check(PortableUpdate.ParseRelease(Release(tag: "v0.10.0"), current, "Lite") is not null, "Versions compare numerically");
            Check(PortableUpdate.ParseRelease(Release(tag: "v0.4.0"), current, "Lite") is null, "Same version does not update");
            Check(PortableUpdate.ParseRelease(Release(tag: "v0.3.0"), current, "Lite") is null, "Older release does not downgrade");
            Check(PortableUpdate.ParseRelease(Release(prerelease: true), current, "Lite") is null, "Prereleases ignored");
            Check(PortableUpdate.ParseRelease(Release().Replace("\"draft\":false", "\"draft\":true"), current, "Lite") is null, "Drafts ignored");
            Check(PortableUpdate.ParseRelease(Release(tag: "v0.5.0-rc1"), current, "Lite") is null, "Non-stable tag rejected");
            Check(PortableUpdate.ParseRelease(Release(), current, "Full") is null && PortableUpdate.ParseRelease(Release(mode: "Full"), current, "Full") is not null, "Lite and Full remain separate");
            Check(PortableUpdate.ParseRelease(Release(digest: "sha256:bad"), current, "Lite") is null, "Missing/invalid digest cannot update");
            Check(PortableUpdate.ParseRelease(Release(url: "https://example.com/package.zip"), current, "Lite") is null, "Foreign release download rejected");
            Check(PortableUpdate.PackageMode(directory) == "Lite", "Lite runtime detection");
            File.WriteAllText(Path.Combine(directory, "coreclr.dll"), "fixture");
            Check(PortableUpdate.PackageMode(directory) == "Full", "Full runtime detection");

            var zip = Path.Combine(directory, "fixture.zip");
            void Zip(string? extra = null, bool required = true)
            {
                if (File.Exists(zip)) File.Delete(zip);
                using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
                if (required)
                    foreach (var file in new[] { "RadioMini.exe", "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll" })
                    {
                        using var writer = new StreamWriter(archive.CreateEntry("portable/" + file).Open());
                        writer.Write("fixture");
                    }
                if (extra is not null) archive.CreateEntry(extra);
            }
            string Digest() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            Zip();
            var output = Path.Combine(directory, "extracted");
            PortableUpdate.ValidateAndExtract(zip, output, Digest());
            Check(File.Exists(Path.Combine(output, "RadioMini.exe")), "Verified single-root package extracts");
            var bytes = File.ReadAllBytes(zip);
            using (var updater = new PortableUpdate(new DownloadFixture(bytes)))
            {
                var release = new UpdateRelease(new Version(0, 5, 0), new Uri("https://example.invalid/update.zip"), bytes.Length, Digest());
                var progress = new DownloadProgress();
                var prepared = await updater.PrepareAsync(release, progress, CancellationToken.None);
                try
                {
                    Check(File.Exists(Path.Combine(prepared, "payload", "RadioMini.exe")) && progress.Percent == 100, "Download stages verified files and reports 100 percent");
                }
                finally { Directory.Delete(prepared, true); }
                await RejectDownloadAsync(updater, release with { Size = bytes.Length + 1 }, "Truncated download rejected");
                await RejectDownloadAsync(updater, release with { Size = bytes.Length - 1 }, "Oversized download rejected");
                await RejectDownloadAsync(updater, release with { Sha256 = new string('0', 64) }, "Corrupted download rejected");
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                try { await updater.PrepareAsync(release, progress, cancelled.Token); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { Check(true, "Cancelled download cannot prepare installer"); }
            }
            Reject(() => PortableUpdate.ValidateAndExtract(zip, output + "-bad", new string('0', 64)), "Wrong hash rejected");
            foreach (var entry in new[] { "portable/../escape.exe", "portable/sub/../../escape.exe", "portable/C:/escape.exe", "/escape.exe", "portable/radio.settings.json", "portable/RADIOMINI.exe", "other/extra.dll", "portable/sub//extra.dll" })
            {
                Zip(entry);
                Reject(() => PortableUpdate.ValidateAndExtract(zip, output + "-bad", Digest()), "Unsafe entry rejected: " + entry);
            }
            Zip();
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
                archive.CreateEntry("portable/link").ExternalAttributes = unchecked((int)0xA1FF0000);
            Reject(() => PortableUpdate.ValidateAndExtract(zip, output + "-bad", Digest()), "ZIP symlinks rejected");
            Zip(required: false);
            Reject(() => PortableUpdate.ValidateAndExtract(zip, output + "-bad", Digest()), "Incomplete package rejected");

            await CheckInstallerAsync(Path.Combine(directory, "success"), false);
            await CheckInstallerAsync(Path.Combine(directory, "rollback"), true);
            Console.WriteLine("All offline portable update checks passed.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task CheckInstallerAsync(string root, bool fail)
    {
        var payload = Path.Combine(root, "payload");
        var destination = Path.Combine(root, "installed");
        Directory.CreateDirectory(payload); Directory.CreateDirectory(destination);
        foreach (var name in new[] { "00-first.txt", "RadioMini.exe", "zz-locked.dll" })
        {
            File.WriteAllText(Path.Combine(payload, name), "new");
            File.WriteAllText(Path.Combine(destination, name), "old");
        }
        File.WriteAllText(Path.Combine(payload, "01-added.txt"), "added");
        var settings = Path.Combine(destination, "radio.settings.json");
        File.WriteAllText(settings, "user settings");
        var script = Path.Combine(root, "install.ps1");
        using (var input = typeof(MainForm).Assembly.GetManifestResourceStream("Radio.Windows.UpdateInstaller.ps1")!)
        using (var output = File.Create(script)) input.CopyTo(output);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        using var parent = Process.Start(new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
        var info = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-Work", root, "-Destination", destination, "-ParentId", parent.Id.ToString(), "-NoRestart" }) info.ArgumentList.Add(arg);
        using var locked = fail ? File.Open(Path.Combine(destination, "zz-locked.dll"), FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        using var installer = Process.Start(info)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(Path.Combine(root, "ready")))
            {
                if (installer.HasExited) throw new Exception(await installer.StandardError.ReadToEndAsync());
                await Task.Delay(50, timeout.Token);
            }
            Check(File.ReadAllText(Path.Combine(destination, "00-first.txt")) == "old", "Installer waits for player exit");
            parent.Kill();
            await installer.WaitForExitAsync(timeout.Token);
            Check(installer.ExitCode == (fail ? 1 : 0), fail ? "Locked-file failure reported" : "Installer succeeds");
            Check(File.ReadAllText(settings) == "user settings", "Portable preferences preserved");
            Check(File.ReadAllText(Path.Combine(destination, "00-first.txt")) == (fail ? "old" : "new"), fail ? "Earlier replaced files roll back" : "Files replaced");
            Check(File.Exists(Path.Combine(destination, "01-added.txt")) == !fail, "Added files follow transaction outcome");
            Check(File.Exists(Path.Combine(root, "result.txt")), "Installer diagnostic log retained");
        }
        finally
        {
            if (!parent.HasExited) parent.Kill();
            if (!installer.HasExited) { installer.Kill(); await installer.WaitForExitAsync(); }
        }
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }

    private static async Task RejectDownloadAsync(PortableUpdate updater, UpdateRelease release, string message)
    {
        try { await updater.PrepareAsync(release, new DownloadProgress(), CancellationToken.None); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new Exception(message);
    }

    private sealed class DownloadProgress : IProgress<int>
    {
        internal int Percent { get; private set; }
        public void Report(int value) => Percent = value;
    }

    private sealed class DownloadFixture(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
