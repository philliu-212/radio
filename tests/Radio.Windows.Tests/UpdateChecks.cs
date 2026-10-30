using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Radio.Update;

namespace Radio.Windows.Tests;

internal static class UpdateChecks
{
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "RadioMini-ota-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var measurements = new List<object>();
        try
        {
            foreach (var mode in new[] { "Lite", "Full" })
            {
                var store = new Store(Path.Combine(root, mode)); Directory.CreateDirectory(store.Root);
                var first = MakePackage(store.Root, "1.0.0", new string('a', 40), mode);
                store.Save(new UpdateState { Current = first });
                var settings = "{\"SchemaVersion\":2,\"Theme\":\"light\",\"Volume\":37,\"MyStationIds\":[\"pop-917\"],\"PlayCounts\":{\"pop-917\":9},\"LastStationId\":\"pop-917\",\"StationSetupCompleted\":true}";
                File.WriteAllText(Path.Combine(store.Root, "radio.settings.json"), settings);
                store.MigrateSettings();
                Check(File.ReadAllText(store.Settings) == settings && File.Exists(Path.Combine(store.Root, "radio.settings.json")), mode + ": legacy settings copied without deleting original");
                File.WriteAllText(Path.Combine(store.Data, "personal.txt"), "keep");
                var second = MakeZip(Path.Combine(root, mode + "-1.1"), "1.1.0", new string('b', 40), mode);
                var third = MakeZip(Path.Combine(root, mode + "-1.2"), "1.2.0", new string('c', 40), mode);
                var fourth = MakeZip(Path.Combine(root, mode + "-1.3"), "1.3.0", new string('d', 40), mode);
                using var fixture = new ReleaseFixture(); fixture.Latest = second;
                var clock = DateTimeOffset.UtcNow;
                using var updater = new PortableUpdate(fixture, store, new Version(1, 0, 0), () => clock);
                var before = DiskBytes(store.Root); var watch = Stopwatch.StartNew();
                await updater.CheckNowAsync(); await updater.Preparation;
                var preparedMs = watch.Elapsed.TotalMilliseconds;
                Check(updater.View.Phase == UpdatePhase.Ready && store.Load().Current.Version == "1.0.0" && store.Load().Pending.Version == "1.1.0", mode + ": 1.0 -> 1.1 prepared without switching running version");
                Check(File.ReadAllText(store.Settings) == settings && Store.Hash(Path.Combine(store.PackagePath(first), "RadioMini.dll")) == first.Files.Single(f => f.Path == "RadioMini.dll").Sha256, mode + ": settings/running files unchanged while preparing");
                store.VerifyInstalledEntries(first);
                Check(true, mode + ": background preparation never replaces root apphost or installed updater");
                var downloads = fixture.Downloads;
                await updater.CheckNowAsync(); await updater.CheckNowAsync();
                Check(fixture.Downloads == downloads, mode + ": ready build never downloaded repeatedly");
                fixture.Latest = third; await updater.CheckNowAsync(); await updater.Preparation;
                Check(store.Load().Pending.Version == "1.2.0" && !Directory.Exists(Path.Combine(store.Root, new string('b', 12))), mode + ": latest wins and unused old pending cleaned");
                fixture.Latest = fourth; fixture.Corrupt = true;
                await updater.CheckNowAsync(); await updater.Preparation;
                Check(updater.View.Phase == UpdatePhase.Failed && store.Load().Pending.Version == "1.2.0", mode + ": failed newer checksum preserves usable previous pending");
                var checks = fixture.Checks; await updater.TickAsync();
                Check(checks == fixture.Checks && updater.NextCheck > clock, mode + ": failed downloads use backoff");
                fixture.Corrupt = false; fixture.Interrupt = true; clock = clock.AddMinutes(1);
                await updater.TickAsync(); await updater.Preparation;
                Check(store.Load().Pending.Version == "1.2.0", mode + ": interrupted stream preserves pending and old version");
                fixture.Interrupt = false; clock = clock.AddMinutes(2);
                await updater.TickAsync(); await updater.Preparation;
                Check(store.Load().Pending.Version == "1.3.0", mode + ": interrupted download retries successfully without a queue");
                var saved = store.Load(); store.Verify(saved.Pending);
                watch.Restart(); store.Promote(saved); var switchMs = watch.Elapsed.TotalMilliseconds;
                Check(saved.Current.Version == "1.3.0" && saved.Previous.Version == "1.0.0" && saved.Pending is null, mode + ": restart switches state directly across versions");
                File.WriteAllText(store.Settings, "{\"SchemaVersion\":2,\"Volume\":99}");
                store.Rollback(saved);
                Check(store.Load().Current.Version == "1.0.0" && File.ReadAllText(store.Settings) == settings, mode + ": unconfirmed startup/power loss rolls back package and exact data snapshot");
                Check(File.ReadAllText(Path.Combine(store.Data, "personal.txt")) == "keep", mode + ": custom data remains intact");
                store.VerifyInstalledEntries(first);
                Check(true, mode + ": rollback restores both root apphost and updater bytes");
                // Cancel an in-flight 1.1 when 1.2 appears, before there is a Ready.
                fixture.Latest = second; fixture.Hold = true;
                await updater.CheckNowAsync(); await fixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                fixture.Latest = third; fixture.Hold = false;
                await updater.CheckNowAsync(); await updater.Preparation;
                Check(fixture.Cancelled > 0 && store.Load().Pending.Version == "1.2.0", mode + ": newer release cancels in-flight older preparation");
                using (var fresh = new PortableUpdate(fixture, store, new Version(1, 0, 0)))
                {
                    downloads = fixture.Downloads; await fresh.CheckNowAsync();
                    Check(fresh.View.Phase == UpdatePhase.Ready && fixture.Downloads == downloads, mode + ": persisted pending recognized after process restart");
                }
                // A broken newer archive and a protocol/schema mismatch must not
                // modify data/current/pending, even when its ZIP checksum is valid.
                foreach (var bad in new[] { "../escape", "settings", "duplicate", "symlink", "schema", "inventory", "entry", "missing-updater" })
                {
                    fixture.Latest = BadZip(fourth, bad);
                    await updater.CheckNowAsync(); await updater.Preparation;
                    Check(store.Load().Pending.Version == "1.2.0" && store.Load().Current.Version == "1.0.0", mode + ": unsafe/incomplete ZIP rejected: " + bad);
                }
                fixture.Latest = fourth;
                using (store.Lock("update"))
                {
                    await updater.CheckNowAsync(); await updater.Preparation;
                    Check(store.Load().Pending.Version == "1.2.0", mode + ": cross-process preparation lock prevents concurrent writes");
                }
                using (var closingFixture = new ReleaseFixture { Latest = fourth, Hold = true })
                {
                    using var closing = new PortableUpdate(closingFixture, store, new Version(1, 0, 0));
                    await closing.CheckNowAsync(); await closingFixture.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    closing.Dispose(); await closing.Preparation.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(store.Load().Current.Version == "1.0.0" && store.Load().Pending.Version == "1.2.0" && closingFixture.Cancelled > 0,
                        mode + ": closing the app cancels download while preserving current and ready pending");
                }
                updater.Dispose(); await updater.Preparation;
                var state = store.Load(); store.Save(state);
                File.WriteAllText(store.StatePath, "interrupted state write");
                Check(store.Load().Current.Version == "1.0.0", mode + ": atomic state backup recovers corrupt primary record");
                using (store.Lock("run"))
                {
                    try { using var duplicate = store.Lock("run"); throw new Exception("Duplicate process lock acquired"); }
                    catch (IOException) { Check(true, mode + ": single active data owner enforced"); }
                }
                state = store.Load();
                var oldRoot = File.ReadAllBytes(store.RootExe);
                // Permit backup reads but deny replacement of the second entry.
                // The first entry will have changed when promotion fails.
                using (var locked = new FileStream(store.UpdaterExe, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { store.Promote(state); throw new Exception("Locked updater replaced"); }
                    catch (IOException) { store.Rollback(state); }
                }
                Check(File.ReadAllBytes(store.RootExe).SequenceEqual(oldRoot) && store.Load().Current.Version == "1.0.0",
                    mode + ": second-entry replacement failure restores first entry and original state");
                state.Pending = Store.Read<Package>(Path.Combine(store.Root, new string('c', 12), "package.json"));
                store.Save(state); store.Promote(state); store.Confirm(state);
                Check(state.Previous is null && state.Trial == "" && !Directory.Exists(store.PackagePath(first)) && !Directory.Exists(Path.Combine(store.Ota, "recovery")),
                    mode + ": health confirmation removes old resources and temporary recovery entries");
                File.WriteAllText(store.StatePath, "corrupt primary after confirmation");
                Check(store.Load().Current.Version == "1.2.0" && store.Load().Previous is null && store.Load().Trial == "",
                    mode + ": backup state cannot resurrect a deleted rollback package");
                var agedWorker = Path.Combine(store.Ota, "workers", new string('a', 32));
                var freshWorker = Path.Combine(store.Ota, "workers", new string('b', 32));
                Directory.CreateDirectory(agedWorker); Directory.CreateDirectory(freshWorker);
                File.WriteAllText(Path.Combine(agedWorker, "updater.exe"), "retired worker");
                Directory.SetCreationTimeUtc(agedWorker, DateTime.UtcNow.AddDays(-2));
                store.CleanupWorkers();
                Check(!Directory.Exists(agedWorker) && Directory.Exists(freshWorker), mode + ": aged worker copies reclaimed without deleting fresh handoffs");
                measurements.Add(new { Mode = mode, FixtureZipBytes = second.Bytes.Length, MemoryTransportDownloadAndPreparationMs = preparedMs,
                    StateSwitchOnlyMs = switchMs, DiskBytesBefore = before, DiskBytesAfter = DiskBytes(store.Root), Scope = "tiny offline fixture, not real portable/network/restart measurement" });
            }
            CheckReleaseSelection();
            var drive = Path.GetPathRoot(root)!;
            Check(new Store(drive).Root == drive && new Store(drive).Prefix == drive, "Drive-root portable paths stay absolute (USB root does not become drive-relative)");
            await CheckMigrationAsync(Path.Combine(root, "migration"));
            Directory.CreateDirectory("artifacts");
            File.WriteAllText("artifacts/ota-offline-metrics.json", JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("All offline background OTA checks passed.");
        }
        finally { Directory.Delete(root, true); }
    }
    internal static Package MakePackage(string root, string version, string commit, string mode)
    {
        var package = new Package { Version = version, Commit = commit, Mode = mode };
        var folder = Path.Combine(root, package.DirectoryName); Directory.CreateDirectory(folder);
        foreach (var name in new[] { "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll" }.Concat(mode == "Full" ? ["coreclr.dll"] : Array.Empty<string>()))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name + version);
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
            package.Files.Add(new PackageFile { Path = name, Size = bytes.Length, Sha256 = Hash(bytes) });
        }
        foreach (var name in new[] { "RadioMini.exe", "bin/updater.exe" })
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(name + version);
            var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
            package.EntryFiles.Add(new PackageFile { Path = name, Size = bytes.Length, Sha256 = Hash(bytes) });
        }
        Store.Write(Path.Combine(folder, "package.json"), package); return package;
    }
    internal sealed record ZipFixture(Version Version, string Mode, byte[] Bytes)
    {
        internal string Name => $"Radio-Portable-v{Version}-{Mode}-win-x64-20261029-123456.zip";
        internal string Json => JsonSerializer.Serialize(new { tag_name = "v" + Version, draft = false, prerelease = false,
            assets = new[] { new { name = Name, size = Bytes.Length, digest = "sha256:" + Hash(Bytes), browser_download_url = $"{PortableUpdate.Repository}/releases/download/v{Version}/{Name}" } } });
    }
    internal static ZipFixture MakeZip(string directory, string version, string commit, string mode)
    {
        Directory.CreateDirectory(directory); var root = Path.Combine(directory, "Portable"); Directory.CreateDirectory(root);
        var package = MakePackage(root, version, commit, mode);
        new Store(root).Save(new UpdateState { Current = package });
        var zip = Path.Combine(directory, "package.zip"); ZipFile.CreateFromDirectory(directory + "/Portable", zip, CompressionLevel.Optimal, true);
        return new(Version.Parse(version), mode, File.ReadAllBytes(zip));
    }
    private static ZipFixture BadZip(ZipFixture source, string kind)
    {
        using var stream = new MemoryStream(); stream.Write(source.Bytes);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            if (kind == "entry" || kind == "missing-updater")
            {
                var entry = zip.Entries.Single(e => e.FullName == (kind == "entry" ? "Portable/RadioMini.exe" : "Portable/bin/updater.exe"));
                entry.Delete();
                if (kind == "entry") { using var output = zip.CreateEntry("Portable/RadioMini.exe").Open(); output.WriteByte(0); }
            }
            else if (kind == "schema" || kind == "inventory")
            {
                var entry = zip.Entries.Single(e => e.Name == "package.json");
                Package package; using (var input = entry.Open()) package = (Package)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Package)).ReadObject(input)!;
                entry.Delete(); if (kind == "schema") package.DataSchema = 99; else package.Files[0].Sha256 = new string('0', 64);
                var replacement = zip.CreateEntry("Portable/" + package.DirectoryName + "/package.json");
                using var output = replacement.Open(); new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Package)).WriteObject(output, package);
            }
            else
            {
                var name = kind switch { "settings" => "Portable/data/radio.settings.json", "duplicate" => "Portable/RadioMini.exe", "symlink" => "Portable/link", _ => "Portable/../escape" };
                var entry = zip.CreateEntry(name); if (kind == "symlink") entry.ExternalAttributes = unchecked((int)0xa1ff0000);
            }
        }
        return source with { Bytes = stream.ToArray() };
    }
    private static void CheckReleaseSelection()
    {
        var zip = new ZipFixture(new Version(1, 10, 0), "Lite", [1, 2, 3]);
        Check(PortableUpdate.ParseRelease(zip.Json, new Version(1, 2, 0), "Lite")?.Version == zip.Version, "Numeric stable ZIP selection");
        Check(PortableUpdate.ParseRelease(zip.Json, zip.Version, "Lite") is null && PortableUpdate.ParseRelease(zip.Json, new Version(1, 2, 0), "Full") is null, "No equal/downgrade or cross-mode update");
        Check(PortableUpdate.ParseRelease(zip.Json.Replace("\"prerelease\":false", "\"prerelease\":true"), new Version(1, 0, 0), "Lite") is null, "Prerelease excluded");
        Check(PortableUpdate.ParseRelease(zip.Json.Replace("\"draft\":false", "\"draft\":true"), new Version(1, 0, 0), "Lite") is null, "Draft excluded");
        try { PortableUpdate.ParseRelease(zip.Json.Replace("sha256:", "sha512:"), new Version(1, 0, 0), "Lite"); throw new Exception("Missing digest accepted"); }
        catch (InvalidDataException) { Check(true, "Missing trusted SHA-256 rejected"); }
    }
    private static async Task CheckMigrationAsync(string root)
    {
        var old = Path.Combine(root, "old"); var target = Path.Combine(root, "new");
        Directory.CreateDirectory(old); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "RadioMini.exe"), "fixture apphost");
        var store = new Store(target); var package = MakePackage(target, "1.0.0", new string('a', 40), "Lite"); store.Save(new UpdateState { Current = package });
        var content = "{\"SchemaVersion\":1,\"Theme\":\"light\",\"Volume\":37,\"MyStationIds\":[\"pop-917\"],\"PlayCounts\":{\"pop-917\":9}}";
        File.WriteAllText(Path.Combine(old, "radio.settings.json"), content);
        async Task<int> Run()
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.GetFullPath("scripts/Migrate-Portable.ps1"), "-OldDirectory", old, "-NewDirectory", target }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!; var error = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync(); await error; return process.ExitCode;
        }
        Check(await Run() == 0 && File.ReadAllText(store.Settings) == content && File.ReadAllText(Path.Combine(old, "radio.settings.json")) == content, "Migration script copies exact legacy preferences/stations without modifying original");
        Check(await Run() != 0 && File.ReadAllText(store.Settings) == content, "Migration never overwrites existing destination preferences");
        File.WriteAllText(store.Settings, "{\"SchemaVersion\":99}");
        try { Store.ValidateSettings(store.Settings); throw new Exception("Future data schema accepted"); }
        catch (InvalidDataException) { Check(true, "Unsupported future data schema fails closed rather than resetting preferences"); }
    }
    internal static long DiskBytes(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    internal sealed class ReleaseFixture : HttpMessageHandler
    {
        internal ZipFixture Latest = null!;
        internal bool Corrupt, Interrupt, Hold;
        internal int Checks, Downloads, Cancelled;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/latest"))
            { Checks++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Latest.Json) }); }
            Downloads++;
            var bytes = Latest.Bytes.ToArray(); if (Corrupt) bytes[0] ^= 0xff;
            var hold = Hold; var interrupt = Interrupt;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ControlledStream(bytes, async token =>
            {
                if (interrupt) throw new IOException("Injected network interruption");
                if (!hold) return;
                Started.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); } catch (OperationCanceledException) { Cancelled++; throw; }
            })) });
        }
    }
    private sealed class ControlledStream(byte[] bytes, Func<CancellationToken, Task> beforeRead) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { await beforeRead(token); return await base.ReadAsync(buffer, token); }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { await beforeRead(token); return await base.ReadAsync(buffer, offset, count, token); }
    }
}
