using System.Runtime.InteropServices;

namespace Radio.Windows.Tests;

internal static class ExecutableIconChecks
{
    public static void Verify(string executable, IReadOnlyDictionary<int, byte[]> expected)
    {
        // Read the PE's own Win32 resources. Shell extraction can return an old
        // path-cached icon even when the executable on disk has new resources.
        var module = LoadLibraryEx(executable, IntPtr.Zero, 0x00000002 | 0x00000020);
        if (module == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var matched = false;
            Exception? error = null;
            EnumResourceNames(module, new IntPtr(14), (_, _, name, _) =>
            {
                try
                {
                    using var reader = new BinaryReader(new MemoryStream(ReadResource(module, 14, name)));
                    if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) return true;
                    var count = reader.ReadUInt16();
                    var actual = new Dictionary<int, byte[]>();
                    for (var i = 0; i < count; i++)
                    {
                        var width = reader.ReadByte(); reader.ReadBytes(7);
                        var length = reader.ReadUInt32(); var id = reader.ReadUInt16();
                        var payload = ReadResource(module, 3, new IntPtr(id));
                        if (payload.Length != length) throw new InvalidDataException("EXE icon resource length mismatch.");
                        actual[width == 0 ? 256 : width] = payload;
                    }
                    matched |= expected.Count == actual.Count && expected.All(pair =>
                        actual.TryGetValue(pair.Key, out var payload) && pair.Value.SequenceEqual(payload));
                }
                catch (Exception exception) { error = exception; return false; }
                return true;
            }, IntPtr.Zero);
            if (error is not null) throw error;
            if (!matched) throw new Exception($"EXE Win32 icon resources differ from current ICO: {executable}");
            Console.WriteLine($"PASS: EXE's own Win32 resources match all {expected.Count} current ICO frames (not Shell cache): {executable}");
        }
        finally { FreeLibrary(module); }
    }

    private static byte[] ReadResource(IntPtr module, int type, IntPtr name)
    {
        var resource = FindResource(module, name, new IntPtr(type));
        if (resource == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var size = checked((int)SizeofResource(module, resource));
        var pointer = LockResource(LoadResource(module, resource));
        if (pointer == IntPtr.Zero || size == 0) throw new InvalidDataException("Empty icon resource.");
        var bytes = new byte[size]; Marshal.Copy(pointer, bytes, 0, size); return bytes;
    }

    private delegate bool EnumResourceName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceName callback, IntPtr parameter);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
}
