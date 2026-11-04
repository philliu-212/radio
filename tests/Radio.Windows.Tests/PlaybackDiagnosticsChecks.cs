using System.Text.Json;
using Radio.Core;

namespace Radio.Windows.Tests;

internal static class PlaybackDiagnosticsChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "RadioMini-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var rotation = Path.Combine(root, "rotation");
            for (var session = 0; session < 3; session++)
            {
                using var log = new PlaybackDiagnostics(rotation, false, 1024, () => now);
                for (var i = 0; i < 80; i++)
                {
                    now += TimeSpan.FromMinutes(1);
                    log.Request("測試電台" + i);
                    log.Status(new(AudioState.Failed, "播放失敗（代碼 4／0x80004005）。"), "測試電台" + i);
                }
            }
            var files = Directory.GetFiles(rotation);
            Check(files.Length == 4, "rotation retains exactly four files across restarts");
            Check(files.All(p => new FileInfo(p).Length <= 1024), "all files respect byte limit");
            foreach (var file in files)
                foreach (var line in File.ReadLines(file))
                    using (JsonDocument.Parse(line)) { }

            var privacy = Path.Combine(root, "privacy");
            using (var log = new PlaybackDiagnostics(privacy, false, clock: () => now))
            {
                log.Request("https://secret.invalid/?token=password");
                for (var i = 0; i < 100; i++)
                    log.Status(new(AudioState.Failed, "播放失敗（代碼 4／0x80004005）。 https://secret.invalid/?token=password\n歌曲"), "secret");
                log.Error(new InvalidOperationException("password https://secret.invalid/"));
                log.Sample();
            }
            var lines = File.ReadAllLines(Path.Combine(privacy, "playback.log"));
            var text = string.Join('\n', lines);
            Check(lines.Count(l => l.Contains("\"kind\":\"playback\"")) == 1, "consecutive duplicate failures suppressed");
            Check(!text.Contains("secret") && !text.Contains("password") && !text.Contains("歌曲"), "URLs, station IDs, messages and metadata excluded");
            Check(text.Contains("0x80004005") && text.Contains("InvalidOperationException"), "native error and exception type retained");
            Check(text.Contains("workingSetBytes") && text.Contains("privateBytes") && text.Contains("managedBytes"), "distinct memory metrics recorded");

            var integration = Path.Combine(root, "integration");
            var player = new FailingPlayer();
            using (var log = new PlaybackDiagnostics(integration, false))
            using (var form = new MainForm(player, persistPreferences: false,
                autoPlayOnStartup: false, checkUpdates: false, diagnostics: log))
            {
                // Exercise the real adapter-status subscription without network or playback.
                player.Fail();
                Check(form.LatestStatus.State == AudioState.Failed, "diagnostics preserve failure UI state");
            }
            Check(File.ReadAllText(Path.Combine(integration, "playback.log")).Contains("0x80004005"),
                "MainForm forwards underlying native error rather than generic UI label");

            var rate = Path.Combine(root, "rate");
            using (var log = new PlaybackDiagnostics(rate, false, clock: () => now))
            {
                for (var i = 0; i < 200; i++) log.Request(i.ToString());
                Check(File.ReadAllLines(Path.Combine(rate, "playback.log")).Length == 30, "global burst limit");
                now += TimeSpan.FromMinutes(1);
                log.Sample();
                using var entry = JsonDocument.Parse(File.ReadAllLines(Path.Combine(rate, "playback.log")).Last());
                Check(entry.RootElement.GetProperty("suppressed").GetInt32() == 172, "suppressed count reported next window");
            }

            var blocked = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blocked, "blocked");
            using (var log = new PlaybackDiagnostics(blocked, false))
            {
                log.Request("station");
                log.Status(new(AudioState.Failed, "error"), "station");
                log.Sample();
            }
            var locked = Path.Combine(root, "locked");
            Directory.CreateDirectory(locked);
            using (var file = new FileStream(Path.Combine(locked, "playback.log"), FileMode.Create, FileAccess.Write, FileShare.None))
            using (var log = new PlaybackDiagnostics(locked, false))
                log.Sample();
            Check(true, "blocked directory and locked log are non-fatal");
            var disposedPath = Path.Combine(root, "disposed");
            var disposed = new PlaybackDiagnostics(disposedPath, false);
            disposed.Dispose();
            var length = new FileInfo(Path.Combine(disposedPath, "playback.log")).Length;
            disposed.Sample(); disposed.Request("station"); disposed.Dispose();
            Check(new FileInfo(Path.Combine(disposedPath, "playback.log")).Length == length, "no writes after disposal");
            Console.WriteLine("All offline playback diagnostics checks passed.");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class FailingPlayer : IAudioPlayer
    {
        public event Action<PlaybackStatus>? StatusChanged;
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public TimeSpan Position => TimeSpan.Zero;
        public Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Stop() { }
        public void Dispose() { }
        internal void Fail() => StatusChanged?.Invoke(new(AudioState.Failed, "播放失敗（代碼 2／0x80004005）。"));
    }
}
