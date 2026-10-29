using System.Diagnostics;
using Radio.Update;

namespace Radio.Windows;

internal static class OtaRuntime
{
    internal static Store? Store { get; private set; }
    internal static string SettingsPath => Store?.Settings ?? Path.Combine(AppContext.BaseDirectory, "radio.settings.json");
    // Called before WinForms/settings/audio initialization. Only packaged children
    // participate; normal dotnet run and probe behavior is unchanged.
    internal static bool Initialize(string[] args, out FileStream? lease)
    {
        lease = null;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "package.json"))) return true;
        var root = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!;
        var store = new Store(root);
        var package = Radio.Update.Store.Read<Package>(Path.Combine(AppContext.BaseDirectory, "package.json")); package.Validate();
        if (typeof(Program).Assembly.GetName().Version?.ToString(3) != package.Version) throw new InvalidDataException("App assembly and package version differ.");
        if (!string.Equals(store.PackagePath(package).TrimEnd(Path.DirectorySeparatorChar), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid portable layout.");
        if (!string.Equals(Environment.GetEnvironmentVariable("RADIOMINI_ROOT"), root, StringComparison.OrdinalIgnoreCase))
        {
            var start = new ProcessStartInfo(Path.Combine(root, "RadioMini.exe")) { UseShellExecute = false, WorkingDirectory = root };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            Process.Start(start)?.Dispose(); return false;
        }
        try { lease = store.Lock("run"); }
        catch (IOException) { return false; }
        Store = store; return true;
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
        var start = new ProcessStartInfo(Path.Combine(Store.Root, "RadioMini.exe")) { UseShellExecute = false, WorkingDirectory = Store.Root };
        start.ArgumentList.Add("--restart-update");
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
        Process.Start(start)?.Dispose();
    }
}
