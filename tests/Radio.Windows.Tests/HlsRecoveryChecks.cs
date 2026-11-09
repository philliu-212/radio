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
        var media = new FixturePlayer();
        var native = new FixturePlayer { StartPlaying = true };
        var mediaCalls = 0; var nativeCalls = 0;
        using var player = new SystemAudioPlayer(() => { mediaCalls++; return media; }, () => { nativeCalls++; return native; });
        player.Volume = .23; player.IsMuted = true;
        var logDirectory = Path.Combine(Path.GetTempPath(), "radio-hls-recovery-" + Guid.NewGuid().ToString("N"));
        using var diagnostics = new PlaybackDiagnostics(logDirectory, false);
        var settings = new ThemeSettings { MyStationIds = ["best-989"] };
        using var form = new MainForm(player, false, true, new Resolver(), new NoMetadata(), settings,
            autoPlayOnStartup: false, checkUpdates: false, playbackNow: () => now, diagnostics: diagnostics);
        form.Show();
        var opening = form.PlayForProbeAsync("best-989");
        var late = media.CaptureCallback();
        CancelOpening(form); // Same CancellationTokenSource path as the real 40-second deadline.
        await opening;
        using (var snapshot = JsonDocument.Parse(File.ReadLines(Path.Combine(logDirectory, "playback.log"))
            .First(line => line.Contains("\"kind\":\"player-snapshot\""))))
        {
            var data = snapshot.RootElement.GetProperty("data");
            Check(data.GetProperty("reason").GetString() == "opening-timeout" &&
                !data.GetProperty("player").GetProperty("active").GetProperty("disposed").GetBoolean(),
                "Timeout snapshot captures the live adapter before disposal");
            Check(!data.ToString().Contains("token", StringComparison.OrdinalIgnoreCase), "New recovery snapshots do not expose signed URLs");
        }
        Check(form.ReconnectPending && media.Disposed && mediaCalls == 1, "Silent opening timeout disposes the failed MediaEngine path");
        now = TimeSpan.FromSeconds(14); form.CheckPlaybackProgress(); await Task.Delay(20);
        Check(nativeCalls == 0, "Recovery does not bypass the existing 15-second backoff");
        now = TimeSpan.FromSeconds(15); form.CheckPlaybackProgress(); await Task.Delay(60);
        Check(nativeCalls == 1 && form.LatestStatus.State == AudioState.Playing && !form.ReconnectPending,
            "Automatic reconnect uses the other Windows adapter after a silent HLS timeout");
        Check(native.IsMuted && native.Volume == .23 && settings.PlayCounts.GetValueOrDefault("best-989") == 1,
            "Recovery preserves mute/volume and counts first playback once");
        late?.Invoke(new(AudioState.Failed, "stale failure"));
        Check(form.LatestStatus.State == AudioState.Playing && !native.Disposed, "Late callbacks cannot stop the replacement adapter");
        diagnostics.Snapshot("fixture-unavailable", new UnavailableDiagnostics());
        Check(form.LatestStatus.State == AudioState.Playing, "Unavailable native diagnostics cannot interrupt recovered playback");
        form.ExitPlayer();
        diagnostics.Dispose();
        Directory.Delete(logDirectory, true);

        var createdMedia = new List<FixturePlayer>(); var createdNative = new List<FixturePlayer>();
        using var routing = new SystemAudioPlayer(() => { var p = new FixturePlayer(); createdMedia.Add(p); return p; },
            () => { var p = new FixturePlayer(); createdNative.Add(p); return p; });
        await routing.PlayAsync(Stream, default);
        Check(routing.PreferCompatibilityForCurrentSource(), "A stalled HLS source enables host-scoped compatibility");
        routing.Stop(); await routing.PlayAsync(Stream, default);
        Check(createdNative.Count == 1 && !routing.PreferCompatibilityForCurrentSource(), "Compatibility recovery cannot loop between engines");
        routing.Stop(); await routing.PlayAsync(Stream, default);
        Check(createdNative.Count == 2, "A recovered host retains compatibility across Stop/Play");
        routing.Stop(); await routing.PlayAsync(new(new Uri("https://other.invalid/live.m3u8"), Stream.Referrer, Stream.MediaType), default);
        Check(createdMedia.Count == 2, "Recovery does not change unrelated HLS hosts");
        routing.Stop(); await routing.PlayAsync(new(new Uri("https://fixture.invalid/live.mp3"), Stream.Referrer, "audio/mpeg"), default);
        Check(createdMedia.Count == 3 && !routing.PreferCompatibilityForCurrentSource(), "Direct audio does not inherit HLS recovery");
        routing.Stop();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await routing.PlayAsync(Stream, canceled.Token); throw new Exception("Canceled play succeeded"); }
        catch (OperationCanceledException) { }
        Check(createdNative.Count == 2 && routing.Position == TimeSpan.Zero, "Canceled playback does not launch a replacement");

        // Exercise the playing-clock watchdog, not just the startup cancellation path.
        now = TimeSpan.Zero;
        var frozen = new FixturePlayer { StartPlaying = true };
        using var stalled = new SystemAudioPlayer(() => frozen, () => new FixturePlayer { StartPlaying = true });
        using var stalledForm = new MainForm(stalled, false, true, new Resolver(), new NoMetadata(),
            new ThemeSettings { MyStationIds = ["best-989"] }, autoPlayOnStartup: false, checkUpdates: false, playbackNow: () => now);
        stalledForm.Show(); await stalledForm.PlayForProbeAsync("best-989");
        for (var second = 1; second <= 10; second++) { now = TimeSpan.FromSeconds(second); stalledForm.CheckPlaybackProgress(); }
        await Task.Delay(40);
        Check(frozen.Disposed && stalledForm.ReconnectPending, "A frozen playback clock also prepares compatibility recovery");
        var description = JsonSerializer.SerializeToElement(stalled.Describe());
        Check(description.GetProperty("compatibilityHosts").EnumerateArray().Single().GetString() == "fixture.invalid",
            "Clock-stall recovery records only the host, not signed URLs");
        stalledForm.ExitPlayer();

        // A genuine outage must remain bounded even after switching the engine once.
        now = TimeSpan.Zero; var failedNativeCalls = 0;
        using var unavailable = new SystemAudioPlayer(() => new FixturePlayer(),
            () => { failedNativeCalls++; return new FixturePlayer { Fail = true }; });
        using var unavailableForm = new MainForm(unavailable, false, true, new Resolver(), new NoMetadata(),
            new ThemeSettings { MyStationIds = ["best-989"] }, autoPlayOnStartup: false, checkUpdates: false, playbackNow: () => now);
        unavailableForm.Show(); var pending = unavailableForm.PlayForProbeAsync("best-989"); CancelOpening(unavailableForm); await pending;
        now = TimeSpan.FromSeconds(15); unavailableForm.CheckPlaybackProgress(); await Task.Delay(50);
        Check(failedNativeCalls == 1 && unavailableForm.ReconnectPending, "If both paths fail the UI returns to bounded backoff");
        now = TimeSpan.FromSeconds(44); unavailableForm.CheckPlaybackProgress(); await Task.Delay(20);
        Check(failedNativeCalls == 1, "Repeated outage retains the increased backoff rather than spinning");
        unavailableForm.ExitPlayer(); now += TimeSpan.FromMinutes(10); unavailableForm.CheckPlaybackProgress(); await Task.Delay(20);
        Check(failedNativeCalls == 1 && !unavailableForm.ReconnectPending, "User exit cancels recovery even after both paths fail");

        var stoppedMedia = new FixturePlayer(); var replacements = 0;
        using var stopped = new SystemAudioPlayer(() => stoppedMedia, () => { replacements++; return new FixturePlayer(); });
        using var stoppedForm = new MainForm(stopped, false, true, new Resolver(), new NoMetadata(),
            new ThemeSettings { MyStationIds = ["best-989"] }, autoPlayOnStartup: false, checkUpdates: false);
        stoppedForm.Show(); var stoppedOpening = stoppedForm.PlayForProbeAsync("best-989"); stoppedForm.ExitPlayer(); await stoppedOpening;
        Check(stoppedMedia.Disposed && replacements == 0 && !stoppedForm.ReconnectPending,
            "Stopping during opening is not mistaken for an engine timeout");
    }

    internal static async Task RunLiveAsync()
    {
        // Deliberately silent primary adapter; real 40s opening deadline, 15s timer,
        // fresh official 98.9 resolution and real Windows compatibility playback.
        var mediaCalls = 0; var nativeCalls = 0;
        var player = new SystemAudioPlayer(() => { mediaCalls++; return new FixturePlayer(); },
            () => { nativeCalls++; return new WindowsAudioPlayer(); }) { IsMuted = true };
        var settings = new ThemeSettings { MyStationIds = ["best-989"] };
        using var form = new MainForm(player, false, true, metadataProvider: new NoMetadata(), settings: settings,
            autoPlayOnStartup: false, checkUpdates: false);
        form.Show(); form.Hide();
        var watch = Stopwatch.StartNew();
        await form.PlayForProbeAsync("best-989");
        Check(watch.Elapsed.TotalSeconds >= 39 && form.ReconnectPending && mediaCalls == 1 && nativeCalls == 0,
            "LIVE: actual opening deadline detects the injected silent primary");
        while (watch.Elapsed.TotalSeconds < 110 && (form.LatestStatus.State != AudioState.Playing || player.Position.TotalSeconds < 20))
            await Task.Delay(250);
        var active = JsonSerializer.SerializeToElement(player.Describe()).GetProperty("active");
        Console.WriteLine($"LIVE 98.9 recovery: elapsed={watch.Elapsed.TotalSeconds:F1}s, state={form.LatestStatus.State}, position={player.Position.TotalSeconds:F1}s, media={mediaCalls}, native={nativeCalls}");
        Check(form.LatestStatus.State == AudioState.Playing && player.Position.TotalSeconds >= 20 &&
            active.GetProperty("backend").GetString() == "native" && mediaCalls == 1 && nativeCalls == 1 &&
            settings.PlayCounts.GetValueOrDefault("best-989") == 1, "LIVE: same player/UI recovers to real 98.9 audio without restarting");
        form.ExitPlayer();
        Console.WriteLine("LIMIT: Injected primary failure, real compatibility audio/timers; not original native-fault reproduction, audible or overnight acceptance.");
    }

    private static void CancelOpening(MainForm form) => ((CancellationTokenSource)typeof(MainForm)
        .GetField("_playRequest", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!).Cancel();

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
        public TimeSpan Position => TimeSpan.Zero;
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
