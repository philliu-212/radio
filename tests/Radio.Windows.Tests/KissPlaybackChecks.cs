using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

// Explicit network check: real UI source-switch path, muted and without settings writes.
internal static class KissPlaybackChecks
{
    internal static async Task RunAsync()
    {
        foreach (var forcePrimaryFailure in new[] { false, true })
        {
            var resolver = new Resolver(forcePrimaryFailure);
            var player = new SystemAudioPlayer { IsMuted = true };
            var settings = new ThemeSettings { MyStationIds = ["kiss-999"] };
            using var form = new MainForm(player, false, true, resolver, settings: settings, autoPlayOnStartup: false);
            form.Show();
            await form.PlayForProbeAsync("kiss-999");
            if (form.LatestStatus.State != AudioState.Playing) throw new Exception("KISS did not start");
            var initial = player.Position.TotalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(20));
            var advance = player.Position.TotalSeconds - initial;
            Console.WriteLine($"LIVE KISS forcedPrimaryFailure={forcePrimaryFailure}, backupCalls={resolver.BackupCalls}, clockAdvance={advance:F1}s, state={form.LatestStatus.State}");
            if (advance < 15 || form.LatestStatus.State != AudioState.Playing ||
                settings.PlayCounts["kiss-999"] != 1 || (forcePrimaryFailure && resolver.BackupCalls != 1))
                throw new Exception("KISS live continuity/source switch failed");
            form.ExitPlayer();
        }
        Console.WriteLine("PASS: KISS live playback and forced primary-failure Twitch switch (muted; not an audibility/long-run test).");
    }

    private sealed class Resolver(bool forcePrimaryFailure) : IStreamResolver
    {
        private readonly OfficialStreamResolver _inner = new();
        public int BackupCalls { get; private set; }
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken token) => forcePrimaryFailure
            ? throw new SourceResolutionException("Simulated primary failure") : _inner.ResolveAsync(station, token);
        public Task<ResolvedStream?> ResolveFallbackAsync(Station station, CancellationToken token, int index = 0)
        {
            BackupCalls++;
            return _inner.ResolveFallbackAsync(station, token, index);
        }
    }
}
