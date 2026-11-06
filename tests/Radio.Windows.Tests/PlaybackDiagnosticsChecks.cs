using System.Text.Json;
using Radio.Core;
using Radio.Windows.UI;

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

            var details = Path.Combine(root, "details");
            const string url = "https://stream.invalid/live.m3u8?token=public-token&signature=a%2Bb&expires=123456";
            const string referrer = "https://radio.invalid/player?station=989";
            const string message = "播放失敗（代碼 4／0x80004005）。 https://stream.invalid/live.m3u8?token=public-token\n網路錯誤";
            using (var log = new PlaybackDiagnostics(details, false, clock: () => now))
            {
                log.Request("best-989");
                log.Resolved("best-989", new(new Uri(url), new Uri(referrer), "application/vnd.apple.mpegurl"));
                for (var i = 0; i < 100; i++)
                    log.Status(new(AudioState.Failed, message), "best-989");
                try { throw new InvalidOperationException("outer failure", new IOException("inner failure " + url)); }
                catch (Exception error) { log.Error(error); }
                log.Sample();
            }
            var lines = File.ReadAllLines(Path.Combine(details, "playback.log"));
            var text = string.Join('\n', lines);
            Check(lines.Count(l => l.Contains("\"kind\":\"playback\"")) == 1, "consecutive duplicate failures suppressed");
            using (var resolved = JsonDocument.Parse(lines.Single(l => l.Contains("\"kind\":\"stream-resolved\""))))
            {
                var data = resolved.RootElement.GetProperty("data");
                Check(data.GetProperty("url").GetString() == url && data.GetProperty("referrer").GetString() == referrer,
                    "full URL, encoded signature, token, expiry and referrer preserved");
                Check(data.GetProperty("station").GetString() == "best-989" && data.GetProperty("isHls").GetBoolean(), "readable station and stream type recorded");
                Check(resolved.RootElement.GetProperty("time").GetDateTimeOffset() == now, "event timestamp retained");
            }
            using (var playback = JsonDocument.Parse(lines.Single(l => l.Contains("\"kind\":\"playback\""))))
                Check(playback.RootElement.GetProperty("data").GetProperty("message").GetString() == message,
                    "full multiline playback error preserved in one JSON line");
            using (var exception = JsonDocument.Parse(lines.Single(l => l.Contains("\"kind\":\"exception\""))))
            {
                var detail = exception.RootElement.GetProperty("data").GetProperty("details").GetString()!;
                Check(detail.Contains("outer failure") && detail.Contains("inner failure " + url) && detail.Contains(nameof(Run)),
                    "exception message, inner exception and stack retained");
            }
            Check(text.Contains("0x80004005") && text.Contains("InvalidOperationException"), "native error and exception type retained");
            Check(text.Contains("workingSetBytes") && text.Contains("privateBytes") && text.Contains("managedBytes"), "distinct memory metrics recorded");

            var twitch = Path.Combine(root, "twitch");
            using (var log = new PlaybackDiagnostics(twitch, false))
                log.Resolved("kiss-999", new(new Uri("https://fixture.hls.ttvnw.net/signed-secret/index.m3u8?token=secret"),
                    new Uri("https://www.twitch.tv/livekiss"), "application/vnd.apple.mpegurl"));
            var twitchText = File.ReadAllText(Path.Combine(twitch, "playback.log"));
            Check(twitchText.Contains("fixture.hls.ttvnw.net") && !twitchText.Contains("signed-secret") &&
                !twitchText.Contains("token="), "Twitch diagnostic retains CDN host but never persists signed path/query");

            var oversized = Path.Combine(root, "oversized");
            using (var log = new PlaybackDiagnostics(oversized, false))
            {
                log.Resolved("best-989", new(new Uri("https://stream.invalid/?token=" + new string('a', 20000)), new Uri(referrer), "audio/mpeg"));
                log.Error(new IOException(new string('錯', 100000)));
            }
            var oversizedLines = File.ReadAllLines(Path.Combine(oversized, "playback.log"));
            Check(oversizedLines.Count(l => l.Contains("[truncated]")) == 2, "oversized URL and error explicitly truncated rather than silently dropped");
            Check(new FileInfo(Path.Combine(oversized, "playback.log")).Length <= PlaybackDiagnostics.MaxFileBytes, "large details respect file byte cap");

            var integration = Path.Combine(root, "integration");
            var player = new FailingPlayer();
            using (var log = new PlaybackDiagnostics(integration, false))
            using (var form = new MainForm(player, persistPreferences: false,
                autoPlayOnStartup: false, checkUpdates: false, diagnostics: log,
                resolver: new FixtureResolver(new(new Uri(url), new Uri(referrer), "application/vnd.apple.mpegurl")),
                settings: new ThemeSettings { MyStationIds = ["best-989"] }))
            {
                Check(form.PlayForProbeAsync("best-989").IsCompletedSuccessfully,
                    "fixture resolves and fails synchronously without network");
                Check(form.LatestStatus.State == AudioState.Buffering && form.ReconnectPending && form.PlaybackWatchRunning,
                    "diagnostics preserve automatic reconnect backoff after playback failure");
                Check(form.LatestStatus.Message.Contains("0x80004005", StringComparison.Ordinal),
                    "reconnect status retains the underlying native error for diagnosis");
            }
            Check(File.ReadAllText(Path.Combine(integration, "playback.log")).Contains("0x80004005"),
                "MainForm forwards underlying native error rather than generic UI label");
            using (var entry = JsonDocument.Parse(File.ReadLines(Path.Combine(integration, "playback.log"))
                .Single(l => l.Contains("\"kind\":\"stream-resolved\""))))
                Check(entry.RootElement.GetProperty("data").GetProperty("url").GetString() == url,
                    "MainForm logs the full resolved source before handing it to the player");

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
        public Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken) { Fail(); return Task.CompletedTask; }
        public void Stop() { }
        public void Dispose() { }
        internal void Fail() => StatusChanged?.Invoke(new(AudioState.Failed, "播放失敗（代碼 2／0x80004005）。"));
    }

    private sealed class FixtureResolver(ResolvedStream stream) : IStreamResolver
    {
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken cancellationToken) => Task.FromResult(stream);
    }
}
