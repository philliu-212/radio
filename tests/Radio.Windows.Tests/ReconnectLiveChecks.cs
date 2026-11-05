using System.Diagnostics;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

// Explicit integration: simulated first-round outage, real timer/resolver/system engine recovery.
// Not a physical network interruption, audibility check or overnight acceptance test.
internal static class ReconnectLiveChecks
{
    internal static async Task RunAsync()
    {
        var resolver = new Resolver();
        var player = new SystemAudioPlayer { IsMuted = true };
        var settings = new ThemeSettings { MyStationIds = ["esradio"] };
        using var form = new MainForm(player, false, true, resolver, settings: settings,
            autoPlayOnStartup: false, checkUpdates: false);
        form.Show(); await form.PlayForProbeAsync("esradio");
        if (!form.ReconnectPending || resolver.PrimaryCalls != 1 || resolver.BackupCalls != 2)
            throw new Exception("Simulated first-round outage was not bounded");
        form.Hide();
        var watch = Stopwatch.StartNew();
        while (resolver.PrimaryCalls == 1 && watch.Elapsed.TotalSeconds < 20) await Task.Delay(100);
        Console.WriteLine($"LIVE reconnect: primary retry at {watch.Elapsed.TotalSeconds:F1}s");
        if (watch.Elapsed.TotalSeconds < 14.5 || resolver.PrimaryCalls != 2)
            throw new Exception("Real timer did not honor initial reconnect backoff");
        var recovered = false;
        while (watch.Elapsed.TotalSeconds < 80)
        {
            await Task.Delay(500);
            if (form.LatestStatus.State == AudioState.Playing && player.Position.TotalSeconds >= 10)
            {
                recovered = true;
                break;
            }
        }
        Console.WriteLine($"LIVE reconnect: elapsed={watch.Elapsed.TotalSeconds:F1}s, state={form.LatestStatus.State}, position={player.Position.TotalSeconds:F1}s, primaryCalls={resolver.PrimaryCalls}, backups={resolver.BackupCalls}, count={settings.PlayCounts.GetValueOrDefault("esradio")}");
        if (!recovered || !player.IsMuted || settings.PlayCounts.GetValueOrDefault("esradio") != 1)
            throw new Exception("Real esRadio recovery did not reach advancing playback within observation budget");
        form.ExitPlayer();
        Console.WriteLine("PASS: Hidden UI reconnects after simulated outage using real 15s timer and live esRadio audio (muted; short observation only).");
    }

    private sealed class Resolver : IStreamResolver
    {
        private readonly OfficialStreamResolver _inner = new();
        public int PrimaryCalls { get; private set; }
        public int BackupCalls { get; private set; }
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken token)
        {
            PrimaryCalls++;
            if (PrimaryCalls == 1) throw new SourceResolutionException("Simulated outage");
            return _inner.ResolveAsync(station, token);
        }
        public Task<ResolvedStream?> ResolveFallbackAsync(Station station, CancellationToken token, int index = 0)
        {
            BackupCalls++;
            if (PrimaryCalls == 1) throw new SourceResolutionException("Simulated outage");
            return _inner.ResolveFallbackAsync(station, token, index);
        }
    }
}
