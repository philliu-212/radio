using System.Diagnostics;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

// Explicit online integration: fail preceding sources, then exercise real UI fallback and system audio.
internal static class BackupPlaybackChecks
{
    internal static async Task RunAsync()
    {
        var failures = new List<string>();
        foreach (var (id, index) in new[] { ("bcc-ilike", 0), ("rfi-monde", 0), ("esradio", 0), ("esradio", 1) })
        {
            var resolver = new Resolver(index);
            var player = new SystemAudioPlayer { IsMuted = true };
            var settings = new ThemeSettings { MyStationIds = [id] };
            using var form = new MainForm(player, false, true, resolver, settings: settings, autoPlayOnStartup: false);
            form.Show();
            await form.PlayForProbeAsync(id);
            if (form.LatestStatus.State != AudioState.Playing) throw new Exception(id + " backup did not start");
            await Task.Delay(3000);
            var initial = player.Position.TotalSeconds;
            var last = initial; var stall = 0; var longestStall = 0;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 30)
            {
                await Task.Delay(1000);
                var position = player.Position.TotalSeconds;
                stall = position - last < .05 ? stall + 1 : 0;
                longestStall = Math.Max(longestStall, stall); last = position;
            }
            var advance = last - initial;
            Console.WriteLine($"LIVE {id} backupIndex={index}, backupCalls={resolver.BackupCalls}, observed={watch.Elapsed.TotalSeconds:F1}s, clockAdvance={advance:F1}s, longestStallSamples={longestStall}, state={form.LatestStatus.State}");
            if (advance < watch.Elapsed.TotalSeconds * .85 || longestStall > 8 || form.LatestStatus.State != AudioState.Playing ||
                settings.PlayCounts[id] != 1 || resolver.BackupCalls != index + 1)
                failures.Add(id + " backup " + index + " continuity/source switch failed");
            form.ExitPlayer();
        }
        if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
        Console.WriteLine("PASS: All newly enabled backup sources play through actual UI fallback (muted; not audibility or long-run acceptance).");
    }

    private sealed class Resolver(int targetIndex) : IStreamResolver
    {
        private readonly OfficialStreamResolver _inner = new();
        public int BackupCalls { get; private set; }
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken token) =>
            throw new SourceResolutionException("Simulated primary failure");
        public Task<ResolvedStream?> ResolveFallbackAsync(Station station, CancellationToken token, int index = 0)
        {
            BackupCalls++;
            return index < targetIndex ? throw new SourceResolutionException("Simulated earlier backup failure")
                : _inner.ResolveFallbackAsync(station, token, index);
        }
    }
}
