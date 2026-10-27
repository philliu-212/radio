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
            try { Application.Run(new ProbeForm(args)); }
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
            try { Application.Run(new MainForm(PlayerBackend.Create(args))); }
            catch (Exception error) when (error is FileNotFoundException or ArgumentException)
            {
                MessageBox.Show(error.Message, "Radio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Environment.ExitCode = 1;
            }
        }
    }
}
