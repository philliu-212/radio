using System.Diagnostics;
using Radio.Core;
using Radio.Windows;

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
            if (args.Contains("--system"))
            {
                await SystemPlayerChecks.RunAsync(stream);
                Console.WriteLine("All offline Windows system audio checks passed.");
                ResultExitCode = 0;
                return;
            }
            using var player = (MpvAudioPlayer)PlayerBackend.Create(["--backend", "mpv"]);
            player.IsMuted = true;
            for (var i = 0; i < 3; i++)
            {
                await StartAsync(player, stream);
                Check(player.ChildProcessIds.Count == 1, "Exactly one owned playback child");
                Check((await player.ReadPropertyAsync("mute")).GetBoolean(), "Playback starts muted");
                Check((await player.ReadPropertyAsync("vid")).ValueKind == System.Text.Json.JsonValueKind.False,
                    "Video selection is disabled");
                player.Volume = 0.25;
                Check(Math.Abs((await player.ReadPropertyAsync("volume")).GetDouble() - 25) < 0.01, "Volume command round-trip");
                player.IsMuted = false;
                Check(!(await player.ReadPropertyAsync("mute")).GetBoolean(), "Unmute command round-trip (local silence)");
                player.IsMuted = true;
                var position = player.Position;
                await Task.Delay(1200);
                Check(player.Position > position, "Playback clock advances");
                var ids = player.ChildProcessIds.ToArray();
                player.Stop();
                Check(ids.All(id => !Alive(id)) && player.ChildProcessIds.Count == 0, "Stop terminates owned child");
            }

            await StartAsync(player, stream);
            var oldIds = player.ChildProcessIds.ToArray();
            await StartAsync(player, stream);
            Check(oldIds.All(id => !Alive(id)), "Replacing playback terminates the previous child");
            player.Stop();

            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                try { await player.PlayAsync(stream, canceled.Token); throw new Exception("Canceled play succeeded."); }
                catch (OperationCanceledException) { }
                Check(player.ChildProcessIds.Count == 0, "Pre-canceled play does not spawn a child");
            }

            var opening = player.PlayAsync(stream, CancellationToken.None);
            var openingIds = player.ChildProcessIds.ToArray();
            player.Stop();
            try { await opening; }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or IOException) { }
            await Task.Delay(250);
            Check(player.ChildProcessIds.Count == 0 && openingIds.All(id => !Alive(id)), "Stop during opening cannot resume playback");

            var missing = new ResolvedStream(new Uri(path + ".missing"), stream.Referrer, "audio/wav");
            var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFailure(PlaybackStatus status) { if (status.State == AudioState.Failed) failure.TrySetResult(); }
            player.StatusChanged += OnFailure;
            await player.PlayAsync(missing, CancellationToken.None);
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(10));
            player.StatusChanged -= OnFailure;
            Check(player.ChildProcessIds.Count == 0, "Failed playback releases the child");

            await StartAsync(player, stream);
            var finalIds = player.ChildProcessIds.ToArray();
            player.Dispose();
            Check(finalIds.All(id => !Alive(id)), "Dispose terminates the owned child");
            Console.WriteLine("All offline mpv lifecycle checks passed.");
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

    private static async Task StartAsync(MpvAudioPlayer player, ResolvedStream stream)
    {
        var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStatus(PlaybackStatus status)
        {
            if (status.State == AudioState.Playing) playing.TrySetResult();
            if (status.State == AudioState.Failed) playing.TrySetException(new Exception(status.Message));
        }
        player.StatusChanged += OnStatus;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await player.PlayAsync(stream, timeout.Token);
            await playing.Task.WaitAsync(timeout.Token);
        }
        finally { player.StatusChanged -= OnStatus; }
    }

    private static bool Alive(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine($"PASS: {name}");
    }
}
