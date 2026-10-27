using Radio.Windows;

namespace Radio.Portable;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--probe")) Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        if (args.Contains("--probe"))
        {
            var probe = new ProbeForm(args);
            Application.Run(probe);
            Environment.ExitCode = probe.ResultExitCode;
            return;
        }
        try { Application.Run(new MainForm(new SystemAudioPlayer())); }
        catch (Exception error) when (error is DllNotFoundException or System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show($"Windows 媒體元件無法使用（0x{error.HResult:X8}）。請確認已安裝 Windows Media Feature Pack。",
                "Radio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Environment.ExitCode = 1;
        }
    }
}
