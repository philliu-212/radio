using System.Diagnostics;
using Radio.Core;

namespace Radio.Windows.Tests;

// Explicit real-network check; never included in --ui/--system. Muted, no recording or settings writes.
internal static class EsRadioPlaybackChecks
{
    internal static async Task RunAsync()
    {
        using var player = new SystemAudioPlayer { IsMuted = true };
        var catalog = StationCatalog.Load();
        var esradio = catalog.Single(s => s.Id == "esradio");
        var low = esradio with { EndpointUrl = new Uri("https://libertaddigital-radio-live1.flumotion.com/libertaddigital/ld-live1-low.mp3") };
        var failures = new List<string>();
        foreach (var (station, seconds) in new[] { (esradio, 120), (low, 60), (catalog.Single(s => s.Id == "rfi-monde"), 20), (esradio, 30) })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds + 40));
            var stream = await new OfficialStreamResolver().ResolveAsync(station, timeout.Token);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var errors = new List<string>();
            void Status(PlaybackStatus status)
            {
                if (status.State == AudioState.Playing) ready.TrySetResult();
                if (status.State == AudioState.Failed)
                {
                    errors.Add(status.Message ?? "Failed");
                    ready.TrySetException(new Exception("Live playback failed"));
                }
            }
            player.StatusChanged += Status;
            try
            {
                await player.PlayAsync(stream, timeout.Token);
                await ready.Task.WaitAsync(timeout.Token);
                var clock = Stopwatch.StartNew();
                var initial = player.Position.TotalSeconds;
                var last = initial; var stalled = 0; var longestStall = 0;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    await Task.Delay(1000, timeout.Token);
                    var position = player.Position.TotalSeconds;
                    stalled = position - last < .05 ? stalled + 1 : 0;
                    longestStall = Math.Max(longestStall, stalled); last = position;
                }
                Console.WriteLine($"LIVE {station.Id} {stream.Uri.AbsolutePath}: observed={clock.Elapsed.TotalSeconds:F1}s, advanced={last - initial:F1}s, longestStallSamples={longestStall}, errors={errors.Count}");
                if (errors.Count > 0 || last - initial < seconds - 10 || longestStall > 10)
                    failures.Add(station.Id + " " + stream.Uri.AbsolutePath + ": live clock continuity failed");
            }
            finally { player.StatusChanged -= Status; }
        }
        player.Stop();
        if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
        Console.WriteLine("PASS: esRadio high/low playback and switching to RFI/back using existing SystemAudioPlayer (muted).");
    }
}
