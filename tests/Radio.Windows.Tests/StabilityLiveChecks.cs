using System.Diagnostics;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

// Validates bounded real-network failover behavior, NOT a claim that every source is stable.
internal static class StabilityLiveChecks
{
    internal static async Task RunAsync()
    {
        foreach (var id in new[] { "esradio", "rfi-monde" })
        {
            var station = StationCatalog.Load().Single(station => station.Id == id);
            var resolver = new Resolver();
            var player = new SystemAudioPlayer { IsMuted = true };
            var settings = new ThemeSettings { MyStationIds = [id] };
            using var form = new MainForm(player, false, true, resolver, settings: settings, autoPlayOnStartup: false);
            form.Show(); await form.PlayForProbeAsync(id);
            var watch = Stopwatch.StartNew(); var lastState = form.LatestStatus.State;
            Console.WriteLine($"LIVE {id}: initialState={lastState}");
            while (watch.Elapsed.TotalSeconds < 120 && !form.ReconnectPending)
            {
                await Task.Delay(1000);
                if (form.LatestStatus.State != lastState)
                {
                    lastState = form.LatestStatus.State;
                    Console.WriteLine($"LIVE {id}: elapsed={watch.Elapsed.TotalSeconds:F1}s state={lastState}, backupsAttempted={resolver.Indices.Count}");
                }
            }
            var counts = settings.PlayCounts.GetValueOrDefault(id);
            Console.WriteLine($"LIVE {id}: observed={watch.Elapsed.TotalSeconds:F1}s, finalState={form.LatestStatus.State}, primaryCalls={resolver.PrimaryCalls}, backupIndices=[{string.Join(',', resolver.Indices)}], listeningCount={counts}, monitor={form.PlaybackWatchRunning}");
            if (resolver.PrimaryCalls != 1 || !resolver.Indices.SequenceEqual(Enumerable.Range(0, resolver.Indices.Count)) ||
                resolver.Indices.Count > station.BackupCount || counts > 1 ||
                (form.ReconnectPending && (resolver.Indices.Count != station.BackupCount || !form.PlaybackWatchRunning)))
                throw new Exception(id + " bounded source policy violated");
            form.ExitPlayer();
        }
        Console.WriteLine("PASS: Real-network first-round failover is ordered/bounded and does not recount. ReconnectPending means this round exhausted, NOT stable playback acceptance.");
    }

    private sealed class Resolver : IStreamResolver
    {
        private readonly OfficialStreamResolver _inner = new();
        public int PrimaryCalls { get; private set; }
        public List<int> Indices { get; } = [];
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken token)
        {
            PrimaryCalls++;
            return _inner.ResolveAsync(station, token);
        }
        public Task<ResolvedStream?> ResolveFallbackAsync(Station station, CancellationToken token, int index = 0)
        {
            Indices.Add(index);
            Console.WriteLine($"LIVE {station.Id}: resolving backup {index} ({station.BackupSources![index].Name})");
            return _inner.ResolveFallbackAsync(station, token, index);
        }
    }
}
