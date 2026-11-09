using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class HlsRecoveryChecks
{
    private static readonly ResolvedStream Stream = new(new Uri("https://fixture.invalid/live.m3u8?token=private"),
        new Uri("https://fixture.invalid/"), "application/vnd.apple.mpegurl");

    internal static async Task RunAsync()
    {
        var now = TimeSpan.Zero;
        var media = new List<FixturePlayer>(); var native = new List<FixturePlayer>();
        using var player = new SystemAudioPlayer(() =>
        {
            var p = new FixturePlayer { StartPlaying = media.Count >= 2 }; media.Add(p); return p;
        }, () => { var p = new FixturePlayer { StartPlaying = true }; native.Add(p); return p; });
        player.Volume = .23; player.IsMuted = true;
        var directory = Path.Combine(Path.GetTempPath(), "radio-hls-recovery-" + Guid.NewGuid().ToString("N"));
        using var diagnostics = new PlaybackDiagnostics(directory, false, clock: () => DateTimeOffset.UnixEpoch + now);
        var settings = new ThemeSettings { MyStationIds = ["best-989", "pop-917", "ufo-921"] };
        using var form = new MainForm(player, false, true, new Resolver(), new NoMetadata(), settings,
            autoPlayOnStartup: false, checkUpdates: false, playbackNow: () => now, diagnostics: diagnostics);
        form.Show();
        var menu = RetryMenu(form);
        Check(menu.Text == "使用相容模式重試" && !menu.CheckOnClick && !menu.Enabled,
            "Manual action follows metadata in the same tray menu; idle cannot retry");
        var opening = form.PlayForProbeAsync("best-989");
        var late = media[0].CaptureCallback();
        CancelOpening(form); await opening;
        Check(form.ReconnectPending && media[0].Disposed && native.Count == 0 && !player.PreferCompatibility,
            "Opening timeout snapshots and backs off without automatically changing engines");
        now = TimeSpan.FromSeconds(14); form.CheckPlaybackProgress(); await Task.Delay(20);
        Check(media.Count == 1, "Automatic reconnect retains its existing backoff");
        now = TimeSpan.FromSeconds(15); form.CheckPlaybackProgress(); await Task.Delay(40);
        Check(media.Count == 2 && native.Count == 0, "Automatic reconnect still uses the default engine");
        await ClickRetryAsync(form); // Replaces an in-flight opening, without waiting another 40 seconds.
        Check(native.Count == 1 && media[1].Disposed && form.LatestStatus.State == AudioState.Playing && !form.ReconnectPending,
            "Manual retry cancels pending opening and starts compatibility with a fresh resolution");
        Check(native[0].IsMuted && native[0].Volume == .23 && settings.PlayCounts.GetValueOrDefault("best-989") == 1,
            "Manual retry preserves mute/volume and first-success count");
        late?.Invoke(new(AudioState.Failed, "stale failure"));
        Check(form.LatestStatus.State == AudioState.Playing && !native[0].Disposed, "Old callbacks cannot stop replacement playback");
        Check(!RetryMenu(form).Enabled, "Duplicate retry is disabled while outcome measurement is pending");
        Check(!File.ReadAllText(Path.Combine(directory, "playback.log")).Contains("clock-progress"),
            "A Playing event alone is not reported as playback progress");
        native[0].Position = TimeSpan.FromSeconds(6); now += TimeSpan.FromSeconds(6); form.CheckPlaybackProgress();
        var lines = File.ReadAllLines(Path.Combine(directory, "playback.log"));
        Check(lines.Any(line => line.Contains("clock-progress")) && lines.Any(line => line.Contains("playing-event")),
            "Diagnostics distinguish a Playing event from five seconds of playback-clock progress");
        using (var snapshot = JsonDocument.Parse(lines.First(line => line.Contains("manual-compatibility-before"))))
            Check(!snapshot.RootElement.GetProperty("data").GetProperty("player").GetProperty("active").GetProperty("disposed").GetBoolean(),
                "Manual retry captures the old live adapter before disposal");
        Check(lines.Where(line => line.Contains("manual-compatibility")).All(line => !line.Contains("token", StringComparison.OrdinalIgnoreCase)),
            "Manual retry diagnostics contain no signed URLs or tokens");
        Check(RetryMenu(form).Enabled, "Another manual attempt is available after measurement completes");
        now += TimeSpan.FromMinutes(1); // Start a new diagnostic rate-limit window for the second attempt.
        await ClickRetryAsync(form);
        Check(native.Count == 2 && settings.PlayCounts.GetValueOrDefault("best-989") == 1,
            "Manual retry of an already-playing station does not count playback again");
        now += TimeSpan.FromSeconds(31); form.CheckPlaybackProgress();
        Check(File.ReadAllText(Path.Combine(directory, "playback.log")).Contains("no-clock-progress"),
            "Playing without clock progress has a separate unsuccessful measurement result");
        Stop(form);
        Check(!RetryMenu(form).Enabled && !form.ReconnectPending, "User stop cancels retry and disables the action");
        await form.PlayForProbeAsync("best-989");
        Check(native.Count == 3, "Explicit compatibility preference survives Stop/Play in this process");
        await form.PlayForProbeAsync("pop-917");
        Check(media.Count == 3 && !player.PreferCompatibility,
            "Preference is station-scoped even when two stations resolve to the same fixture host");
        diagnostics.Snapshot("fixture-unavailable", new UnavailableDiagnostics());
        Check(form.LatestStatus.State == AudioState.Playing, "Unavailable diagnostics do not interrupt playback");
        form.ExitPlayer(); diagnostics.Dispose(); Directory.Delete(directory, true);

        var nativeCalls = 0;
        using var direct = new SystemAudioPlayer(() => new FixturePlayer { StartPlaying = true },
            () => { nativeCalls++; return new FixturePlayer(); }) { PreferCompatibility = true };
        await direct.PlayAsync(new(Stream.Uri, Stream.Referrer, "audio/mpeg"), default);
        Check(nativeCalls == 0, "Non-HLS sources never inherit the manual HLS preference");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await direct.PlayAsync(Stream, canceled.Token); throw new Exception("Canceled play succeeded"); }
        catch (OperationCanceledException) { }
        Check(nativeCalls == 0, "Canceled request does not launch compatibility");

        now = TimeSpan.Zero;
        var frozen = new FixturePlayer { StartPlaying = true }; var replacements = 0;
        using var stalled = new SystemAudioPlayer(() => frozen, () => { replacements++; return new FixturePlayer(); });
        using var stalledForm = new MainForm(stalled, false, true, new Resolver(), new NoMetadata(),
            new ThemeSettings { MyStationIds = ["best-989"] }, autoPlayOnStartup: false, checkUpdates: false, playbackNow: () => now);
        stalledForm.Show(); await stalledForm.PlayForProbeAsync("best-989");
        for (var second = 1; second <= 10; second++) { now = TimeSpan.FromSeconds(second); stalledForm.CheckPlaybackProgress(); }
        await Task.Delay(40);
        Check(frozen.Disposed && stalledForm.ReconnectPending && replacements == 0 && !stalled.PreferCompatibility,
            "Clock stall does not automatically switch engines");
        stalledForm.ExitPlayer();

        now = TimeSpan.Zero; var failedNativeCalls = 0;
        using var unavailable = new SystemAudioPlayer(() => new FixturePlayer(),
            () => { failedNativeCalls++; return new FixturePlayer { Fail = true }; });
        using var unavailableForm = new MainForm(unavailable, false, true, new Resolver(), new NoMetadata(),
            new ThemeSettings { MyStationIds = ["best-989"] }, autoPlayOnStartup: false, checkUpdates: false, playbackNow: () => now);
        unavailableForm.Show(); var pending = unavailableForm.PlayForProbeAsync("best-989");
        CancelOpening(unavailableForm); await pending;
        await ClickRetryAsync(unavailableForm);
        Check(failedNativeCalls == 1 && unavailableForm.ReconnectPending, "Failed manual retry returns to bounded backoff");
        now = TimeSpan.FromSeconds(14); unavailableForm.CheckPlaybackProgress(); await Task.Delay(20);
        Check(failedNativeCalls == 1, "Failed retry does not spin");
        unavailableForm.ExitPlayer(); now += TimeSpan.FromMinutes(10); unavailableForm.CheckPlaybackProgress(); await Task.Delay(20);
        Check(failedNativeCalls == 1 && !unavailableForm.ReconnectPending, "Exit cancels all reconnects");

        var canceledNative = new FixturePlayer();
        using var stopping = new SystemAudioPlayer(() => new FixturePlayer(), () => canceledNative);
        using var stoppingForm = new MainForm(stopping, false, true, new Resolver(), new NoMetadata(),
            new ThemeSettings { MyStationIds = ["best-989"] }, autoPlayOnStartup: false, checkUpdates: false);
        stoppingForm.Show(); var firstOpening = stoppingForm.PlayForProbeAsync("best-989");
        var retry = ClickRetryAsync(stoppingForm); Stop(stoppingForm); await retry; await firstOpening;
        Check(canceledNative.Disposed && !stoppingForm.ReconnectPending && stoppingForm.LatestStatus.State == AudioState.Stopped,
            "Stop during compatibility opening cancels the attempt without scheduling reconnect");
    }

    internal static async Task RunLiveAsync()
    {
        // Inject only the primary failure. This does not reproduce the user's native fault.
        var mediaCalls = 0; var nativeCalls = 0;
        var player = new SystemAudioPlayer(() => { mediaCalls++; return mediaCalls == 1 ? new FixturePlayer() : new MediaEngineAudioPlayer(); },
            () => { nativeCalls++; return new WindowsAudioPlayer(); }) { IsMuted = true };
        var settings = new ThemeSettings { MyStationIds = ["best-989", "pop-917"] };
        using var form = new MainForm(player, false, true, metadataProvider: new NoMetadata(), settings: settings,
            autoPlayOnStartup: false, checkUpdates: false);
        form.Show(); form.Hide();
        var watch = Stopwatch.StartNew();
        await form.PlayForProbeAsync("best-989");
        Check(watch.Elapsed.TotalSeconds >= 39 && form.ReconnectPending && mediaCalls == 1 && nativeCalls == 0 && !player.PreferCompatibility,
            "LIVE: actual opening deadline backs off without switching engines");
        await ClickRetryAsync(form);
        await CheckLiveProgressAsync(form, player);
        Check(nativeCalls == 1 && mediaCalls == 1 && settings.PlayCounts.GetValueOrDefault("best-989") == 1,
            "LIVE: explicit tray action starts real 98.9 compatibility playback");
        Stop(form);
        Check(form.LatestStatus.State == AudioState.Stopped && !form.ReconnectPending && player.Position == TimeSpan.Zero,
            "LIVE: manual pause stops playback and cancels reconnect");
        await form.PlayForProbeAsync("best-989"); await CheckLiveProgressAsync(form, player);
        Check(mediaCalls == 1 && nativeCalls == 2, "LIVE: manual preference survives pause/replay");
        await form.PlayForProbeAsync("pop-917"); await CheckLiveProgressAsync(form, player);
        Check(mediaCalls == 2 && nativeCalls == 2, "LIVE: POP retains its default engine");
        await form.PlayForProbeAsync("best-989"); await CheckLiveProgressAsync(form, player);
        Check(mediaCalls == 2 && nativeCalls == 3, "LIVE: returning to 98.9 retains the explicit preference");
        form.ExitPlayer();
        Console.WriteLine("LIMIT: Injected primary failure; real streams and manual retry path. Not proof of recovery from the user's original native fault, audibility or long-run stability.");
    }

    private static ToolStripMenuItem RetryMenu(MainForm form)
    {
        var tray = (ThemedTrayIcon)typeof(MainForm).GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var menu = tray.ContextMenuStrip!;
        typeof(ContextMenuStrip).GetMethod("OnOpening", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(menu, [new CancelEventArgs()]);
        var metadata = menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "顯示節目／歌曲資訊");
        return (ToolStripMenuItem)menu.Items[menu.Items.IndexOf(metadata) + 1];
    }

    private static async Task ClickRetryAsync(MainForm form)
    {
        var item = RetryMenu(form);
        Check(item.Enabled, "Manual retry is available for the active HLS target");
        item.PerformClick(); await form.CompatibilityRetryTask;
    }

    private static void Stop(MainForm form) => typeof(MainForm).GetMethod("StopPlayback", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
    private static void CancelOpening(MainForm form) => ((CancellationTokenSource)typeof(MainForm)
        .GetField("_playRequest", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!).Cancel();

    private static async Task CheckLiveProgressAsync(MainForm form, SystemAudioPlayer player)
    {
        var watch = Stopwatch.StartNew(); var position = player.Position;
        while (watch.Elapsed.TotalSeconds < 25 && player.Position - position < TimeSpan.FromSeconds(5)) await Task.Delay(250);
        Console.WriteLine($"LIVE replay: state={form.LatestStatus.State}, advance={(player.Position - position).TotalSeconds:F1}s");
        Check(form.LatestStatus.State == AudioState.Playing && player.Position - position >= TimeSpan.FromSeconds(5),
            "LIVE: real playback clock advances, not only a Playing event");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class Resolver : IStreamResolver
    {
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken token) => Task.FromResult(Stream);
    }
    private sealed class NoMetadata : IStationMetadataProvider
    {
        public bool Supports(string id) => false;
        public Task<StationMetadata?> GetAsync(string id, CancellationToken token) => Task.FromResult<StationMetadata?>(null);
        public void Dispose() { }
    }
    private sealed class UnavailableDiagnostics : IPlayerDiagnostics
    {
        public string DisplayName => "fixture";
        public IReadOnlyList<int> ChildProcessIds => [];
        public object Describe() => throw new InvalidOperationException("Native state unavailable");
    }
    private sealed class FixturePlayer : IAudioPlayer, IPlayerDiagnostics
    {
        public bool StartPlaying { get; init; }
        public bool Fail { get; init; }
        public bool Disposed { get; private set; }
        public event Action<PlaybackStatus>? StatusChanged;
        public Action<PlaybackStatus>? CaptureCallback() => StatusChanged;
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public TimeSpan Position { get; set; }
        public string DisplayName => "fixture";
        public IReadOnlyList<int> ChildProcessIds => [];
        public object Describe() => new { backend = "fixture", disposed = Disposed };
        public Task PlayAsync(ResolvedStream stream, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            StatusChanged?.Invoke(new(Fail ? AudioState.Failed : StartPlaying ? AudioState.Playing : AudioState.Buffering, "fixture"));
            return Task.CompletedTask;
        }
        public void Stop() => StatusChanged?.Invoke(new(AudioState.Stopped, "fixture stop"));
        public void Dispose() { Disposed = true; Stop(); }
    }
}
