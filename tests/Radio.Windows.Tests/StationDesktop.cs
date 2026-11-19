using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Radio.Windows.Tests;

// Use a real, non-input Windows desktop, not a mock of ListBox's native hit-testing.
// Never call SwitchDesktop: the user's desktop, pointer and foreground window stay untouched.
internal static class StationDesktop
{
    private const string Prefix = "RadioStationTests-";

    internal static bool IsIsolated =>
        DesktopName(GetThreadDesktop(GetCurrentThreadId())).StartsWith(Prefix, StringComparison.Ordinal);

    internal static Task RunAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { RunChild(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true, Name = "Station desktop launcher" };
        thread.Start();
        return completion.Task;
    }

    private static void RunChild()
    {
        // CreateDesktop can attach its caller. Use a fresh thread with no HWND/hooks,
        // and restore it before launching the child or closing the desktop.
        var original = GetThreadDesktop(GetCurrentThreadId());
        var name = Prefix + Guid.NewGuid().ToString("N");
        var desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Create station desktop");
        Exception? failure = null;
        try
        {
            if (!SetThreadDesktop(original))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Restore launcher desktop");
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing runner executable.");
            var command = new StringBuilder($"\"{executable}\" ");
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                command.Append($"\"{typeof(StationDesktop).Assembly.Location}\" ");
            command.Append("--suite ui-stations");
            if (UiChecks.SaveScreenshots) command.Append(" --screenshots");
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Desktop = name,
                Flags = 0x00000100, // STARTF_USESTDHANDLES: preserve runner stdout/stderr logs.
                Input = GetStdHandle(-10),
                Output = GetStdHandle(-11),
                Error = GetStdHandle(-12)
            };
            Console.WriteLine("Station suite: isolated non-input desktop (no desktop switch).");
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0,
                IntPtr.Zero, Environment.CurrentDirectory, ref startup, out var child))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Launch station desktop runner");
            try
            {
                if (WaitForSingleObject(child.Process, uint.MaxValue) != 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Wait for station desktop runner");
                if (!GetExitCodeProcess(child.Process, out var exitCode))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Read station desktop runner exit code");
                if (exitCode != 0) throw new InvalidOperationException($"Isolated station suite failed (exit {exitCode}).");
            }
            finally
            {
                CloseHandle(child.Thread);
                CloseHandle(child.Process);
            }
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            if (!CloseDesktop(desktop))
            {
                var cleanup = new Win32Exception(Marshal.GetLastWin32Error(), "Close station desktop");
                if (failure is not null) throw new AggregateException(failure, cleanup);
                throw cleanup;
            }
        }
        Console.WriteLine("Station desktop runner exited; desktop handle closed.");
    }

    private static string DesktopName(IntPtr desktop)
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(desktop, 2, name, name.Capacity * sizeof(char), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Read desktop name");
        return name.ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        internal int Size;
        internal string? Reserved;
        internal string? Desktop;
        internal string? Title;
        internal int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        internal short Show, ReservedSize;
        internal IntPtr ReservedData, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        internal IntPtr Process, Thread;
        internal uint ProcessId, ThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder data, int length, out int needed);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity,
        IntPtr threadSecurity, bool inheritHandles, uint flags, IntPtr environment, string directory,
        ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
