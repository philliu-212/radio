// Compiled with the same Framework compiler and sources as the shipped updater.
using System;
using System.Diagnostics;
using System.IO;
using Radio.Update;

internal static class LongPathTests
{
    private static int Main(string[] args)
    {
        if (args[0] == "legacy")
        {
            try { Path.GetFullPath(new string('a', 280)); }
            catch (PathTooLongException) { Console.WriteLine("PASS: legacy Framework reproduces PathTooLongException"); return 0; }
            throw new Exception("Legacy control did not reproduce the original failure.");
        }
        // Exercise the actual updater startup, not test-only AppContext switches.
        Program.ConfigurePaths();
        var root = Path.Combine(args[1], "store");
        Directory.CreateDirectory(root);
        var store = new Store(root);
        var old = MakePackage('a');
        var current = MakePackage('b');
        var oldFolder = store.PackagePath(old);
        Directory.CreateDirectory(oldFolder);
        Store.Write(Path.Combine(oldFolder, "package.json"), old);
        var deep = @"\\?\" + Path.Combine(oldFolder, new string('x', 100), new string('y', 100), "runtime.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(deep));
        File.WriteAllText(deep, "obsolete runtime");
        if (deep.Length <= 260) throw new Exception("Test path is not long enough.");
        Store.RejectTreeLinks(@"\\?\" + oldFolder);
        store.Cleanup(new UpdateState { Current = current });
        if (Directory.Exists(oldFolder)) throw new Exception("Old long-path resources survived cleanup.");
        Console.WriteLine("PASS: actual updater startup enables safe cleanup at " + deep.Length + " characters");

        var outside = Path.Combine(args[1], "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        Directory.CreateDirectory(oldFolder);
        Store.Write(Path.Combine(oldFolder, "package.json"), old);
        var junction = Path.Combine(oldFolder, new string('z', 100));
        var info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + outside + "\"");
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        using (var process = Process.Start(info))
        { process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Junction fixture creation failed."); }
        try
        {
            bool rejected = false;
            try { Store.RejectTreeLinks(oldFolder); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Reparse point was not rejected.");
            store.Cleanup(new UpdateState { Current = current });
            if (!File.Exists(sentinel) || File.ReadAllText(sentinel) != "keep" || !Directory.Exists(oldFolder))
                throw new Exception("Cleanup crossed a junction or removed a rejected tree.");
            Console.WriteLine("PASS: junction rejected; external data and rejected package preserved");
        }
        finally { Directory.Delete(junction); }
        return 0;
    }
    private static Package MakePackage(char identity)
    {
        var package = new Package { Version = "1.0.0", Commit = new string(identity, 40), Mode = "Lite" };
        foreach (var path in new[] { "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll" })
            package.Files.Add(new PackageFile { Path = path, Size = 1, Sha256 = new string('0', 64) });
        foreach (var path in new[] { "RadioMini.exe", "bin/updater.exe" })
            package.EntryFiles.Add(new PackageFile { Path = path, Size = 1, Sha256 = new string('0', 64) });
        return package;
    }
}
