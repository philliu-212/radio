using Radio.Core;

namespace Radio.Windows.Tests;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var tests = new TestForm(args);
        Application.Run(tests);
        Environment.ExitCode = tests.ResultExitCode;
    }
}

internal sealed class TestForm(string[] args) : Form
{
    public int ResultExitCode { get; private set; } = 1;
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Hide();
        try
        {
            if (args.Contains("--ota-stage"))
            {
                await OtaPackageChecks.StageAsync(args);
                ResultExitCode = 0;
                return;
            }
            if (args.Contains("--updates"))
            {
                await UpdateChecks.RunAsync();
                ResultExitCode = 0;
                return;
            }
            if (args.Contains("--rfi"))
            {
                await RfiPlaybackChecks.RunAsync(args);
                ResultExitCode = 0;
                return;
            }
            if (args.Contains("--icons"))
            {
                var index = Array.IndexOf(args, "--exe");
                IconChecks.Run(index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null);
                ResultExitCode = 0;
                return;
            }
            if (args.Contains("--ui"))
            {
                await UiChecks.RunAsync();
                ResultExitCode = 0;
                return;
            }
            // Generate silence locally. These lifecycle tests never contact a radio station.
            var path = Path.GetFullPath("artifacts/playback-test.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                const int bytes = 16000 * 2 * 30;
                writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
            }
            var stream = new ResolvedStream(new Uri(path), new Uri("https://example.invalid/"), "audio/wav");
            await SystemPlayerChecks.RunAsync(stream);
            Console.WriteLine("All offline Windows system audio checks passed.");
            ResultExitCode = 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error.GetType().Name}: {error.Message}");
            Console.Error.WriteLine(error.StackTrace);
            ResultExitCode = 1;
        }
        finally { Close(); }
    }
}
