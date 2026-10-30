using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Radio.Update;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SetErrorMode(0x8003); // Do not let OS loader error dialogs block recovery.
        if (args.Length != 2 || (args[0] != "--switch" && args[0] != "--worker")) return 2;
        var requestPath = Path.GetFullPath(args[1]);
        Store store = null;
        try
        {
            Store.RejectLinks(requestPath);
            var request = Store.Read<SwitchRequest>(requestPath);
            store = new Store(request.Root);
            if (!requestPath.StartsWith(Path.Combine(store.Ota, "workers") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(requestPath) != "request.json" || request.ParentId <= 0 || request.Arguments == null)
                throw new InvalidDataException("Invalid switch request.");
            if (args[0] == "--switch")
            {
                if (!String.Equals(Process.GetCurrentProcess().MainModule.FileName, store.UpdaterExe, StringComparison.OrdinalIgnoreCase)) return 2;
                var worker = Path.Combine(Path.GetDirectoryName(requestPath), "updater.exe");
                Store.RejectLinks(worker); File.Copy(store.UpdaterExe, worker, false);
                Process.Start(new ProcessStartInfo(worker, "--worker " + Quote(requestPath)) { UseShellExecute = false, WorkingDirectory = store.Root }).Dispose();
                return 0;
            }
            if (!String.Equals(Process.GetCurrentProcess().MainModule.FileName, Path.Combine(Path.GetDirectoryName(requestPath), "updater.exe"), StringComparison.OrdinalIgnoreCase)) return 2;
            Trace(store, "switch worker started");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(requestPath), "accepted"), "ready");
            try
            {
                using (var parent = Process.GetProcessById(request.ParentId))
                    if (parent.StartTime.ToUniversalTime().Ticks == request.ParentStart && !parent.WaitForExit(30000))
                        throw new IOException("Player has not exited. No update applied.");
            }
            catch (ArgumentException) { }
            using (var launch = Acquire(store, "launch", 30000))
            {
                if (launch == null) throw new IOException("Another switch is active.");
                using (var check = Acquire(store, "run", 30000))
                    if (check == null) throw new IOException("Player is still running. No update applied.");
                // The forwarding helper must exit before replacing bin/updater.exe.
                WaitForOldChildren(store);
                using (var update = Acquire(store, "update", 10000))
                {
                    if (update == null) throw new IOException("Update preparation has not released its lock. Please try again.");
                    store.MigrateSettings();
                    var state = store.Load();
                    Trace(store, "loaded current=" + state.Current.Version + " trial=" + state.Trial);
                    if (!String.IsNullOrEmpty(state.Trial)) store.Rollback(state);
                    // A normal later launch also activates an already Ready package.
                    try { store.Promote(state); }
                    catch (Exception error)
                    {
                        Trace(store, "promotion failed: " + error);
                        if (!String.IsNullOrEmpty(state.Trial)) store.Rollback(state);
                        state.Pending = null; store.Save(state);
                        File.WriteAllText(Path.Combine(store.Ota, "last-error.txt"), "Could not promote pending package: " + error.Message);
                    }
                    if (!Start(store, state.Current, request.Arguments))
                    {
                        Trace(store, "startup failed; rolling back");
                        if (String.IsNullOrEmpty(state.Trial)) throw new IOException("Radio Mini failed to start. No trial version was installed.");
                        store.Rollback(state);
                        if (!Start(store, state.Current, request.Arguments)) throw new IOException("Both new and previous versions failed to start. Settings backup is in data/ota.");
                        File.WriteAllText(Path.Combine(store.Ota, "last-error.txt"), "New version failed its startup handshake; restored previous version and settings.");
                    }
                    store.Confirm(state);
                    Trace(store, "healthy current=" + state.Current.Version);
                    // The window is already healthy; reclaim staging afterwards.
                    try { store.Cleanup(state); store.CleanupStaging(); } catch (IOException) { }
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            try { Directory.CreateDirectory(store.Ota); File.WriteAllText(Path.Combine(store.Ota, "updater-error.txt"), error.ToString()); } catch { }
            MessageBox.Show(error.Message, "Radio Mini", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
    private static FileStream Acquire(Store store, string name, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { try { return store.Lock(name); } catch (IOException) { if (watch.ElapsedMilliseconds >= milliseconds) return null; Thread.Sleep(50); } }
        while (true);
    }
    private static bool Start(Store store, Package package, string[] args)
    {
        Trace(store, "starting " + package.Version);
        var ready = Path.Combine(store.Ota, "ready-" + Guid.NewGuid().ToString("N"));
        Store.RejectLinks(ready);
        var info = new ProcessStartInfo(store.RootExe);
        info.UseShellExecute = false;
        info.ErrorDialog = false;
        info.WorkingDirectory = store.Root;
        info.Arguments = String.Join(" ", args.Where(a => a != "--restart-update").Select(Quote));
        info.EnvironmentVariables["RADIOMINI_SWITCH"] = "1";
        info.EnvironmentVariables["RADIOMINI_READY"] = ready;
        info.EnvironmentVariables["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        Process process = null;
        try
        {
            Store.RejectLinks(info.FileName);
            ValidateExecutable(info.FileName);
            process = Process.Start(info);
            if (process == null) return false;
            Trace(store, "child pid=" + process.Id);
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 30000)
            {
                if (process.HasExited) { Trace(store, "child exited early"); return false; }
                if (File.Exists(ready)) { Trace(store, "child acknowledged ready"); return true; }
                Thread.Sleep(25);
            }
            process.Kill(); process.WaitForExit(); return false;
        }
        catch (Exception error) { Trace(store, "start error: " + error.Message); if (process != null && !process.HasExited) { process.Kill(); process.WaitForExit(); } return false; }
        finally { if (process != null) process.Dispose(); if (File.Exists(ready)) File.Delete(ready); }
    }
    private static void ValidateExecutable(string path)
    {
        // Reject truncated/non-PE files before CreateProcess. Some Windows
        // compatibility handlers otherwise display blocking loader UI.
        using (var input = File.OpenRead(path))
        using (var reader = new BinaryReader(input))
        {
            if (input.Length < 128 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Player executable is not a valid PE file.");
            input.Position = 0x3c; var offset = reader.ReadInt32();
            if (offset < 64 || offset > input.Length - 6) throw new InvalidDataException("Player PE header is truncated.");
            input.Position = offset;
            if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Player executable is not Windows x64.");
        }
    }
    private static void Trace(Store store, string message)
    {
        try
        {
            var path = Path.Combine(store.Ota, "updater.log"); Store.RejectLinks(path);
            Directory.CreateDirectory(store.Ota);
            // Bound local diagnostics; no settings, stream URLs or credentials.
            if (File.Exists(path) && new FileInfo(path).Length > 262144) File.Delete(path);
            File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + Process.GetCurrentProcess().Id + " " + message + Environment.NewLine);
        }
        catch { }
    }
    private static void WaitForOldChildren(Store store)
    {
        var own = Process.GetCurrentProcess().Id;
        foreach (var process in Process.GetProcessesByName("updater"))
        {
            try
            {
                if (process.Id != own && String.Equals(process.MainModule.FileName, store.UpdaterExe, StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(10000))
                    throw new IOException("Updater forwarding process did not exit.");
            }
            catch (System.ComponentModel.Win32Exception) { /* Unknown foreign/elevated processes are never killed. run.lock protects our managed owner. */ }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
    }
    private static string Quote(string argument)
    {
        var result = new System.Text.StringBuilder("\""); int slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
