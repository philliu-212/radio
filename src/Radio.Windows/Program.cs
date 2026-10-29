namespace Radio.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--probe"))
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        if (args.Contains("--probe"))
        {
            try
            {
                var probe = new ProbeForm(args);
                Application.Run(probe);
                Environment.ExitCode = probe.ResultExitCode;
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("artifacts");
                var details = new List<string>();
                for (Exception? detail = error; detail is not null; detail = detail.InnerException)
                    details.Add($"{detail.GetType().Name} (0x{detail.HResult:X8})\n{detail.StackTrace}");
                File.WriteAllText("artifacts/probe-fatal.txt", string.Join("\n", details));
                Environment.ExitCode = 1;
            }
        }
        else
        {
            try { Application.Run(new MainForm(PlayerBackend.Create(args), autoPlayOnStartup: !args.Contains("--no-autoplay"))); }
            catch (Exception error) when (error is FileNotFoundException or ArgumentException)
            {
                MessageBox.Show(error.Message, "Radio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Environment.ExitCode = 1;
            }
            catch (Exception error) when (error is DllNotFoundException or System.Runtime.InteropServices.COMException)
            {
                MessageBox.Show($"Windows 媒體元件無法使用（0x{error.HResult:X8}）。請確認已安裝 Windows Media Feature Pack。",
                    "Radio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Environment.ExitCode = 1;
            }
        }
    }
}
