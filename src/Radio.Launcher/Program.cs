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
        var store = new Store(AppDomain.CurrentDomain.BaseDirectory);
        try
        {
            Trace(store, "launcher started");
            // Cross-process file locks also work for copies on separate desktops.
            using (var launch = Acquire(store, "launch", 30000))
            {
                if (launch == null) return 0;
                using (var check = Acquire(store, "run", args.Contains("--restart-update") ? 30000 : 0))
                    if (check == null) { FocusExisting(store); return 0; }
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
                        if (!String.IsNullOrEmpty(state.Trial)) store.Rollback(state);
                        state.Pending = null; store.Save(state);
                        File.WriteAllText(Path.Combine(store.Ota, "last-error.txt"), "Could not promote pending package: " + error.Message);
                    }
                    if (!Start(store, state.Current, args))
                    {
                        Trace(store, "startup failed; rolling back");
                        if (String.IsNullOrEmpty(state.Trial)) throw new IOException("Radio Mini failed to start. No trial version was installed.");
                        store.Rollback(state);
                        if (!Start(store, state.Current, args)) throw new IOException("Both new and previous versions failed to start. Settings backup is in data/ota.");
                        File.WriteAllText(Path.Combine(store.Ota, "last-error.txt"), "New version failed its startup handshake; restored previous version and settings.");
                    }
                    state.Trial = ""; store.Save(state);
                    Trace(store, "healthy current=" + state.Current.Version);
                    // Hashing, extraction and cleanup never occur on the switch path.
                    try { store.Cleanup(state); store.CleanupStaging(); } catch (IOException) { }
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            try { Directory.CreateDirectory(store.Ota); File.WriteAllText(Path.Combine(store.Ota, "launcher-error.txt"), error.ToString()); } catch { }
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
        var info = new ProcessStartInfo(Path.Combine(store.PackagePath(package), "RadioMini.exe"));
        info.UseShellExecute = false;
        info.ErrorDialog = false;
        info.WorkingDirectory = store.PackagePath(package);
        info.Arguments = String.Join(" ", args.Where(a => a != "--restart-update").Select(Quote));
        info.EnvironmentVariables["RADIOMINI_ROOT"] = store.Root;
        info.EnvironmentVariables["RADIOMINI_READY"] = ready;
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
            var path = Path.Combine(store.Ota, "launcher.log"); Store.RejectLinks(path);
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
        foreach (var process in Process.GetProcessesByName("RadioMini"))
        {
            try
            {
                if (process.Id != own && process.MainModule.FileName.StartsWith(store.Prefix, StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(Path.GetDirectoryName(process.MainModule.FileName), store.Root, StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(10000))
                    throw new IOException("Old player process did not exit. No version was switched.");
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
    private static void FocusExisting(Store store)
    {
        foreach (var process in Process.GetProcessesByName("RadioMini"))
        {
            try
            {
                if (process.MainModule.FileName.StartsWith(store.Prefix, StringComparison.OrdinalIgnoreCase) && process.MainWindowHandle != IntPtr.Zero)
                { ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); }
            }
            catch { }
            finally { process.Dispose(); }
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);
}
