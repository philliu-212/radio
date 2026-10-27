using System.Text.Json;
using Radio.Core;

namespace Radio.Windows.Tests;

internal static class SystemPlayerChecks
{
    public static async Task RunAsync(ResolvedStream stream)
    {
        foreach (var backend in new[] { "mediaengine", "system" })
        {
            using var player = PlayerBackend.Create(["--backend", backend]);
            player.IsMuted = true;
            for (var i = 0; i < 3; i++)
            {
                await StartAsync(player, stream);
                var diagnostics = (IPlayerDiagnostics)player;
                Check(diagnostics.ChildProcessIds.Count == 0, "No external playback processes");
                var state = JsonSerializer.SerializeToElement(diagnostics.Describe());
                if (backend == "system") state = state.GetProperty("active");
                Check(state.GetProperty("hasAudio").GetBoolean() && !state.GetProperty("hasVideo").GetBoolean(), "Audio-only source");
                Check(state.GetProperty("muted").GetBoolean(), "System engine starts muted");
                player.Volume = 0.25;
                state = JsonSerializer.SerializeToElement(diagnostics.Describe());
                if (backend == "system") state = state.GetProperty("active");
                Check(Math.Abs(state.GetProperty("volume").GetDouble() - 0.25) < 0.001, "Native volume round-trip");
                var position = player.Position;
                await Task.Delay(1200);
                Check(player.Position > position, "System playback clock advances");
                player.Stop();
                Check(player.Position == TimeSpan.Zero, "Stop releases system playback");
            }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { await player.PlayAsync(stream, canceled.Token); throw new Exception("Canceled play succeeded."); }
            catch (OperationCanceledException) { }

            var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFailure(PlaybackStatus status) { if (status.State == AudioState.Failed) failure.TrySetResult(); }
            player.StatusChanged += OnFailure;
            await player.PlayAsync(new ResolvedStream(new Uri(stream.Uri.AbsoluteUri + ".missing"), stream.Referrer, "audio/wav"), default);
            await failure.Task.WaitAsync(TimeSpan.FromSeconds(10));
            player.StatusChanged -= OnFailure;
            Check(player.Position == TimeSpan.Zero, "Invalid source releases playback without crashing");
            await StartAsync(player, stream);
            player.Dispose();
            Check(player.Position == TimeSpan.Zero, "Dispose releases system playback");
        }
    }

    private static async Task StartAsync(IAudioPlayer player, ResolvedStream stream)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStatus(PlaybackStatus status)
        {
            if (status.State == AudioState.Playing) ready.TrySetResult();
            if (status.State == AudioState.Failed) ready.TrySetException(new Exception(status.Message));
        }
        player.StatusChanged += OnStatus;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await player.PlayAsync(stream, timeout.Token);
            await ready.Task.WaitAsync(timeout.Token);
        }
        finally { player.StatusChanged -= OnStatus; }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine($"PASS: {name}");
    }
}
