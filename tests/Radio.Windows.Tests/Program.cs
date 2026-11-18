using Radio.Core;

namespace Radio.Windows.Tests;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            // Parse before creating a window, touching audio or contacting the network.
            var command = TestCommand.Parse(args);
            if (command.Name is "--list" or "--help")
            {
                Console.WriteLine("Usage: --suite name[,name] [--screenshots] | one legacy command. No arguments: --system.");
                foreach (var group in TestCatalog.Groups)
                    Console.WriteLine($"{group.Name,-20} {group.Description}");
                Console.WriteLine("Legacy: " + string.Join(" ", TestCommand.Names));
                return;
            }
            if (command.Name == "--instance-client")
            {
                using var instance = new SingleInstance(args[1]);
                Environment.ExitCode = !instance.IsPrimary && instance.ActivateExistingAsync().GetAwaiter().GetResult() ? 0 : 1;
                return;
            }
            if (command.Name == "--instance-host")
            {
                SingleInstanceChecks.RunHost(args[1], !args.Contains("--no-listen"));
                return;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UiChecks.SaveScreenshots = command.Screenshots;
            if (!command.MessageLoop)
            {
                StaTestContext.Run(command.Run);
                return;
            }
            if (command.StartupFocus && !UiChecks.CheckStartupFocus())
            {
                Environment.ExitCode = 1;
                return;
            }
            using var tests = new TestForm(command);
            Application.Run(tests);
            Environment.ExitCode = tests.ResultExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error}");
            Environment.ExitCode = 1;
        }
    }
}

internal sealed class TestForm(TestCommand command) : Form
{
    public int ResultExitCode { get; private set; } = 1;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Hide();
        try
        {
            await command.Run();
            ResultExitCode = 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error}");
        }
        finally { Close(); }
    }
}
