using System.Diagnostics;
using Radio.Update;

namespace Radio.Windows;

internal static class OtaRuntime
{
    internal static Store? Store { get; private set; }
    internal static string SettingsPath => Store?.Settings ?? Path.Combine(AppContext.BaseDirectory, "radio.settings.json");
    // The root native apphost directly loads the managed assembly in the hash
    // directory. Development builds have no package and remain unchanged.
    internal static bool Initialize(string[] args, out FileStream? lease)
    {
        lease = null;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "package.json"))) return true;
        var root = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
        var store = new Store(root);
        var package = Radio.Update.Store.Read<Package>(Path.Combine(AppContext.BaseDirectory, "package.json")); package.Validate();
        if (typeof(Program).Assembly.GetName().Version?.ToString(3) != package.Version) throw new InvalidDataException("App assembly and package version differ.");
        if (!string.Equals(store.PackagePath(package).TrimEnd(Path.DirectorySeparatorChar), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid portable layout.");
        if (!string.Equals(Environment.ProcessPath, store.RootExe, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Portable must run from its root apphost.");
        var switching = Environment.GetEnvironmentVariable("RADIOMINI_SWITCH") == "1";
        Environment.SetEnvironmentVariable("RADIOMINI_SWITCH", null);
        FileStream? launch = null;
        try
        {
            try
            {
                if (!switching) launch = store.Lock("launch");
                lease = store.Lock("run");
            }
            catch (IOException) { return false; } // Another owner; never interrupt it.
            Store = store;
            if (switching && store.Load().Current.Commit != package.Commit)
                throw new InvalidDataException("Switched apphost loaded the wrong resource package.");
            if (!switching)
            {
                using var update = store.Lock("update");
                store.MigrateSettings();
                var state = store.Load();
                if (!string.IsNullOrEmpty(state.Trial) || state.Pending is not null)
                { RequestSwitch(args); lease.Dispose(); lease = null; return false; }
                if (state.Current.Commit != package.Commit) throw new InvalidDataException("Active package and apphost differ.");
                store.CleanupWorkers();
            }
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            return true;
        }
        catch { lease?.Dispose(); lease = null; throw; }
        finally { launch?.Dispose(); }
    }
    internal static void Healthy()
    {
        if (Store is null) return;
        var ready = Environment.GetEnvironmentVariable("RADIOMINI_READY");
        if (ready is null || !string.Equals(Path.GetDirectoryName(ready), Store.Ota, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(ready).StartsWith("ready-", StringComparison.Ordinal)) return;
        Radio.Update.Store.RejectLinks(ready); File.WriteAllText(ready, "ready");
        Environment.SetEnvironmentVariable("RADIOMINI_READY", null);
    }
    internal static void Restart()
    {
        if (Store is null) throw new InvalidOperationException("Not a portable launch.");
        RequestSwitch(Environment.GetCommandLineArgs().Skip(1).ToArray());
    }
    private static void RequestSwitch(string[] args)
    {
        var store = Store!;
        var directory = Path.Combine(store.Ota, "workers", Guid.NewGuid().ToString("N"));
        Radio.Update.Store.RejectLinks(directory); Directory.CreateDirectory(directory);
        using var own = Process.GetCurrentProcess();
        var request = Path.Combine(directory, "request.json");
        Radio.Update.Store.Write(request, new SwitchRequest { Root = store.Root, ParentId = own.Id,
            ParentStart = own.StartTime.ToUniversalTime().Ticks, Arguments = args.Where(a => a != "--restart-update").ToArray() });
        var start = new ProcessStartInfo(store.UpdaterExe) { UseShellExecute = false, WorkingDirectory = store.Root };
        start.ArgumentList.Add("--switch"); start.ArgumentList.Add(request);
        using var helper = Process.Start(start) ?? throw new IOException("Could not start updater.");
        var accepted = Path.Combine(directory, "accepted");
        var watch = Stopwatch.StartNew();
        while (!File.Exists(accepted))
        {
            if (helper.HasExited && helper.ExitCode != 0) throw new IOException("Updater rejected the switch request; player remains open.");
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("Updater did not accept the switch request; player remains open.");
            Thread.Sleep(25);
        }
    }
}
