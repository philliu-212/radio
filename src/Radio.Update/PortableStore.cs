// Shared with the temporary .NET Framework updater: deliberately C# 5 compatible.
#pragma warning disable 8618, 8625, 8600, 8601, 8602, 8603, 8604
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Radio.Update
{
    [DataContract]
    public sealed class Package
    {
        [DataMember] public int Protocol = 1;
        [DataMember] public int DataSchema = 2;
        [DataMember] public string Version = "";
        [DataMember] public string Commit = "";
        [DataMember] public string Mode = "";
        [DataMember] public List<PackageFile> Files = new List<PackageFile>();
        [DataMember] public List<PackageFile> EntryFiles = new List<PackageFile>();
        public string DirectoryName { get { return Commit.Substring(0, 12); } }
        public void Validate()
        {
            System.Version version;
            if (Protocol != 1 || DataSchema != 2 || String.IsNullOrEmpty(Version) || String.IsNullOrEmpty(Commit) || !System.Version.TryParse(Version, out version) || !Regex.IsMatch(Version, @"^\d+\.\d+\.\d+$") ||
                !Regex.IsMatch(Commit, "^[a-f0-9]{40}$") || (Mode != "Lite" && Mode != "Full") || Files == null || Files.Count < 4 || Files.Count > 5000)
                throw new InvalidDataException("Unsupported portable package protocol/schema/version.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in Files)
            {
                if (file == null || !Store.SafePath(file.Path) || file.Path.Equals("RadioMini.exe", StringComparison.OrdinalIgnoreCase) || !paths.Add(file.Path) || String.IsNullOrEmpty(file.Sha256) || !Regex.IsMatch(file.Sha256, "^[a-f0-9]{64}$") || file.Size < 0 || file.Size > 400000000)
                    throw new InvalidDataException("Unsafe package inventory.");
                total += file.Size;
                if (total > 1500000000) throw new InvalidDataException("Package is too large.");
            }
            foreach (var path in paths)
            {
                var parent = path;
                while (parent.Contains("/"))
                {
                    parent = parent.Substring(0, parent.LastIndexOf('/'));
                    if (paths.Contains(parent)) throw new InvalidDataException("File/directory collision.");
                }
            }
            foreach (var required in new[] { "RadioMini.dll", "RadioMini.deps.json", "RadioMini.runtimeconfig.json", "Radio.Core.dll" })
                if (!paths.Contains(required)) throw new InvalidDataException("Incomplete package.");
            if (paths.Contains("coreclr.dll") != (Mode == "Full")) throw new InvalidDataException("Runtime mode mismatch.");
            if (EntryFiles == null || EntryFiles.Count != 2 || EntryFiles.Any(f => f == null) || !new HashSet<string>(EntryFiles.Select(f => f.Path), StringComparer.Ordinal).SetEquals(new[] { "RadioMini.exe", "bin/updater.exe" }))
                throw new InvalidDataException("Invalid root entry inventory.");
            foreach (var entry in EntryFiles)
                if (entry.Size <= 0 || entry.Size > 10000000 || String.IsNullOrEmpty(entry.Sha256) || !Regex.IsMatch(entry.Sha256, "^[a-f0-9]{64}$"))
                    throw new InvalidDataException("Invalid root entry hash/size.");
        }
    }
    [DataContract]
    public sealed class PackageFile
    {
        [DataMember] public string Path = "";
        [DataMember] public long Size;
        [DataMember] public string Sha256 = "";
    }
    [DataContract]
    public sealed class UpdateState
    {
        [DataMember] public int Protocol = 1;
        [DataMember] public Package Current;
        [DataMember] public Package Pending;
        [DataMember] public Package Previous;
        [DataMember] public string Trial = "";
    }
    [DataContract]
    public sealed class SettingsHeader { [DataMember] public int SchemaVersion; }
    [DataContract]
    public sealed class SwitchRequest
    {
        [DataMember] public string Root = "";
        [DataMember] public int ParentId;
        [DataMember] public long ParentStart;
        [DataMember] public string[] Arguments = new string[0];
    }
    public sealed class Store
    {
        public readonly string Root;
        public string Prefix { get { return Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; } }
        public string Data { get { return Path.Combine(Root, "data"); } }
        public string Ota { get { return Path.Combine(Data, "ota"); } }
        public string Settings { get { return Path.Combine(Data, "radio.settings.json"); } }
        public string StatePath { get { return Path.Combine(Ota, "state.json"); } }
        public string RootExe { get { return Path.Combine(Root, "RadioMini.exe"); } }
        public string UpdaterExe { get { return Path.Combine(Root, "bin", "updater.exe"); } }
        public Store(string root)
        {
            var full = Path.GetFullPath(root);
            Root = full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd(Path.DirectorySeparatorChar);
            RejectLinks(Root);
        }
        public string PackagePath(Package package) { package.Validate(); return Path.Combine(Root, package.DirectoryName); }
        public static bool SafePath(string path)
        {
            return !String.IsNullOrEmpty(path) && path.Length <= 240 && path.Split('/').All(part =>
                Regex.IsMatch(part, @"^[A-Za-z0-9_.-]+$") && part != "." && part != ".." && !part.EndsWith(".") &&
                !part.Equals("radio.settings.json", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase));
        }
        public static void RejectLinks(string path)
        {
            var full = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(full))
            {
                if ((File.Exists(full) || Directory.Exists(full)) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Portable path contains a junction/symlink.");
                full = Path.GetDirectoryName(full);
            }
        }
        public static T Read<T>(string path)
        {
            RejectLinks(path);
            if (new FileInfo(path).Length > (typeof(T) == typeof(UpdateState) ? 8000000 : 2000000)) throw new InvalidDataException("State/manifest too large.");
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(input);
        }
        public static void Write<T>(string path, T value)
        {
            RejectLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(output, value);
                    output.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public UpdateState Load()
        {
            UpdateState state;
            try { state = Read<UpdateState>(StatePath); ValidateState(state); }
            catch (Exception error)
            {
                if (!(error is IOException || error is SerializationException || error is InvalidDataException)) throw;
                state = Read<UpdateState>(StatePath + ".bak"); ValidateState(state);
            }
            return state;
        }
        private static void ValidateState(UpdateState state)
        {
            if (state == null || state.Protocol != 1 || state.Current == null) throw new InvalidDataException("Invalid OTA state.");
            state.Current.Validate();
            if (state.Pending != null)
            {
                state.Pending.Validate();
                if (state.Pending.Mode != state.Current.Mode || System.Version.Parse(state.Pending.Version) <= System.Version.Parse(state.Current.Version)) throw new InvalidDataException("Pending mode/version mismatch.");
            }
            if (state.Previous != null) { state.Previous.Validate(); if (state.Previous.Mode != state.Current.Mode) throw new InvalidDataException("Rollback mode mismatch."); }
            if (!String.IsNullOrEmpty(state.Trial) && state.Trial != state.Current.Commit) throw new InvalidDataException("Invalid trial state.");
        }
        public void Save(UpdateState state) { ValidateState(state); Write(StatePath, state); }
        public FileStream Lock(string name)
        {
            if (!Regex.IsMatch(name, "^[a-z]+$")) throw new ArgumentException("Invalid lock name.");
            RejectLinks(Ota); Directory.CreateDirectory(Ota); var path = Path.Combine(Ota, name + ".lock"); RejectLinks(path);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        public static string Hash(string path)
        { using (var input = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
        public void Verify(Package package)
        {
            var folder = PackagePath(package); RejectLinks(folder);
            foreach (var file in package.Files)
            {
                var path = Path.Combine(folder, file.Path); RejectLinks(path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Size || Hash(path) != file.Sha256) throw new InvalidDataException("Corrupt package: " + file.Path);
            }
        }
        private static string EntryName(PackageFile entry) { return entry.Path == "RadioMini.exe" ? "app.bin" : "updater.bin"; }
        public string EntryPath(Package package, PackageFile entry) { return Path.Combine(Ota, "entries", package.DirectoryName, EntryName(entry)); }
        private static void VerifyFile(string path, PackageFile file)
        {
            RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size || Hash(path) != file.Sha256) throw new InvalidDataException("Entry corrupt: " + file.Path);
        }
        private static void CopyDurable(string source, string target)
        {
            RejectLinks(source); RejectLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
        }
        public void PrepareEntries(Package package, string extractedRoot, string temporary)
        {
            package.Validate();
            foreach (var entry in package.EntryFiles) VerifyFile(Path.Combine(extractedRoot, entry.Path), entry);
            var destination = Path.Combine(Ota, "entries", package.DirectoryName); RejectLinks(destination);
            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(temporary);
                foreach (var entry in package.EntryFiles) CopyDurable(Path.Combine(extractedRoot, entry.Path), Path.Combine(temporary, EntryName(entry)));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                RetryIo(delegate { Directory.Move(temporary, destination); });
            }
            VerifyPreparedEntries(package);
        }
        public void VerifyPreparedEntries(Package package)
        { foreach (var entry in package.EntryFiles) VerifyFile(EntryPath(package, entry), entry); }
        public void VerifyInstalledEntries(Package package)
        { foreach (var entry in package.EntryFiles) VerifyFile(Path.Combine(Root, entry.Path), entry); }
        private static void RetryIo(Action action)
        {
            // Windows scanners may briefly hold newly written PE files without
            // delete sharing. Never disable protection; retry a bounded interval.
            for (var attempt = 0; ; attempt++)
            {
                try { action(); return; }
                catch (IOException) { if (attempt >= 20) throw; }
                catch (UnauthorizedAccessException) { if (attempt >= 20) throw; }
                System.Threading.Thread.Sleep(100);
            }
        }
        private void BackupEntries(Package package)
        {
            VerifyInstalledEntries(package);
            foreach (var entry in package.EntryFiles) CopyDurable(Path.Combine(Root, entry.Path), Path.Combine(Ota, "recovery", EntryName(entry)));
        }
        private void ReplaceEntry(string source, string target)
        {
            RejectLinks(source); RejectLinks(target);
            var temp = Path.Combine(Ota, "replace-" + Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                CopyDurable(source, temp); Directory.CreateDirectory(Path.GetDirectoryName(target));
                RetryIo(delegate {
                    if (File.Exists(target)) File.Replace(temp, target, null, true);
                    else File.Move(temp, target);
                });
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private void InstallEntries(Package package)
        {
            VerifyPreparedEntries(package);
            foreach (var entry in package.EntryFiles) ReplaceEntry(EntryPath(package, entry), Path.Combine(Root, entry.Path));
        }
        private void RestoreEntries(Package package)
        {
            foreach (var entry in package.EntryFiles)
            {
                var backup = Path.Combine(Ota, "recovery", EntryName(entry)); VerifyFile(backup, entry);
                var target = Path.Combine(Root, entry.Path); RejectLinks(target);
                if (File.Exists(target) && new FileInfo(target).Length == entry.Size && Hash(target) == entry.Sha256) continue;
                ReplaceEntry(backup, Path.Combine(Root, entry.Path));
            }
        }
        public void MigrateSettings()
        {
            Directory.CreateDirectory(Data); RejectLinks(Settings);
            var legacy = Path.Combine(Root, "radio.settings.json"); RejectLinks(legacy);
            if (!File.Exists(Settings) && File.Exists(legacy))
            {
                if (new FileInfo(legacy).Length > 1048576) throw new InvalidDataException("Legacy settings too large.");
                ValidateSettings(legacy); File.Copy(legacy, Settings); // Never move/delete the original.
            }
            if (File.Exists(Settings)) ValidateSettings(Settings);
        }
        public static void ValidateSettings(string path)
        {
            if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("Settings too large.");
            var header = Read<SettingsHeader>(path);
            if (header == null || header.SchemaVersion < 0 || header.SchemaVersion > 2) throw new InvalidDataException("Unsupported settings schema. No data was migrated.");
        }
        public void CleanupStaging()
        {
            var staging = Path.Combine(Ota, "staging"); RejectLinks(staging);
            if (!Directory.Exists(staging)) return;
            foreach (var folder in Directory.GetDirectories(staging))
                if (Regex.IsMatch(Path.GetFileName(folder), "^[a-f0-9]{32}$"))
                { RejectTreeLinks(folder); Directory.Delete(folder, true); }
        }
        public void CleanupWorkers()
        {
            var workers = Path.Combine(Ota, "workers"); RejectLinks(workers);
            if (!Directory.Exists(workers)) return;
            foreach (var folder in Directory.GetDirectories(workers))
            {
                // Only reclaim aged temporary copies, never a just-starting helper.
                if (!Regex.IsMatch(Path.GetFileName(folder), "^[a-f0-9]{32}$") || Directory.GetCreationTimeUtc(folder) > DateTime.UtcNow.AddDays(-1)) continue;
                try
                {
                    RejectTreeLinks(folder);
                    var exe = Path.Combine(folder, "updater.exe");
                    if (File.Exists(exe)) File.Delete(exe); // A running image stays locked.
                    Directory.Delete(folder, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        public void BackupData()
        {
            var backup = Path.Combine(Ota, "settings.rollback"); RejectLinks(Settings); RejectLinks(backup);
            if (File.Exists(Settings))
            {
                using (var input = File.OpenRead(Settings))
                using (var output = new FileStream(backup, FileMode.Create, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
            }
            else if (File.Exists(backup)) File.Delete(backup);
            File.WriteAllText(Path.Combine(Ota, "settings.existed"), File.Exists(Settings) ? "yes" : "no");
        }
        public void RestoreData()
        {
            var backup = Path.Combine(Ota, "settings.rollback"); RejectLinks(backup); RejectLinks(Settings);
            if (File.Exists(backup)) File.Copy(backup, Settings, true);
            else if (File.Exists(Path.Combine(Ota, "settings.existed")) && File.ReadAllText(Path.Combine(Ota, "settings.existed")) == "no" && File.Exists(Settings)) File.Delete(Settings);
        }
        public void Promote(UpdateState state)
        {
            if (state.Pending == null) return;
            // Complete verification happens in the background, not at restart.
            var disk = Read<Package>(Path.Combine(PackagePath(state.Pending), "package.json")); disk.Validate();
            if (disk.Commit != state.Pending.Commit || disk.Version != state.Pending.Version || disk.Mode != state.Pending.Mode) throw new InvalidDataException("Pending identity changed.");
            VerifyPreparedEntries(state.Pending); BackupData(); BackupEntries(state.Current);
            state.Previous = state.Current; state.Current = state.Pending; state.Pending = null; state.Trial = state.Current.Commit; Save(state);
            InstallEntries(state.Current);
        }
        public void Rollback(UpdateState state)
        {
            if (state.Previous == null) throw new InvalidDataException("No rollback package.");
            RestoreEntries(state.Previous); RestoreData(); state.Current = state.Previous; state.Previous = null; state.Trial = ""; Save(state);
        }
        public void Confirm(UpdateState state)
        {
            state.Trial = ""; state.Previous = null;
            // Both durable records must stop referencing rollback files before
            // deleting anything: corrupt primary must not resurrect old Trial.
            Save(state); Save(state); Cleanup(state); CleanupEntryFiles(state);
            foreach (var name in new[] { "settings.rollback", "settings.existed" })
            { var path = Path.Combine(Ota, name); RejectLinks(path); if (File.Exists(path)) File.Delete(path); }
            var recovery = Path.Combine(Ota, "recovery");
            if (Directory.Exists(recovery)) { RejectTreeLinks(recovery); Directory.Delete(recovery, true); }
        }
        public void CleanupEntryFiles(UpdateState state)
        {
            var folder = Path.Combine(Ota, "entries"); RejectLinks(folder);
            if (!Directory.Exists(folder)) return;
            foreach (var entry in Directory.GetDirectories(folder))
                if (Regex.IsMatch(Path.GetFileName(entry), "^[a-f0-9]{12}$") && (state.Pending == null || Path.GetFileName(entry) != state.Pending.DirectoryName))
                { RejectTreeLinks(entry); Directory.Delete(entry, true); }
        }
        public void Cleanup(UpdateState state)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { state.Current.DirectoryName };
            if (state.Pending != null) keep.Add(state.Pending.DirectoryName);
            if (state.Previous != null) keep.Add(state.Previous.DirectoryName);
            foreach (var folder in Directory.GetDirectories(Root))
            {
                var name = Path.GetFileName(folder);
                if (!Regex.IsMatch(name, "^[a-f0-9]{12}$") || keep.Contains(name)) continue;
                try
                {
                    var package = Read<Package>(Path.Combine(folder, "package.json")); package.Validate();
                    if (package.DirectoryName != name) continue;
                    RejectTreeLinks(folder);
                    Directory.Delete(folder, true);
                }
                catch (Exception error)
                { if (!(error is IOException || error is UnauthorizedAccessException || error is SerializationException || error is InvalidDataException)) throw; }
            }
        }
        public static void RejectTreeLinks(string folder)
        {
            var queue = new Queue<string>(); queue.Enqueue(folder);
            while (queue.Count > 0)
            {
                var next = queue.Dequeue(); RejectLinks(next);
                foreach (var path in Directory.GetFileSystemEntries(next))
                { RejectLinks(path); if (Directory.Exists(path)) queue.Enqueue(path); }
            }
        }
    }
}
