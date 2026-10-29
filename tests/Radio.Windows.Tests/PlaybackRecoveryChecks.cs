using System.Reflection;
using Radio.Core;
using Radio.Windows.Native;

namespace Radio.Windows.Tests;

internal static class PlaybackRecoveryChecks
{
    public static void Run()
    {
        var profile = RfiPlaybackChecks.Analyze([
            new(0, 0, AudioState.Opening), new(1, 0, AudioState.Buffering), new(2, 1, AudioState.Playing),
            new(60, 59, AudioState.Playing), new(61, 59, AudioState.Buffering), new(62, 59, AudioState.Buffering),
            new(63, 59, AudioState.Buffering), new(64, 60, AudioState.Playing)]);
        Check(profile.FirstClockAdvanceSeconds == 2 && profile.FirstMinuteStallSeconds == 2 && profile.LaterStallSeconds == 1,
            "Online analysis excludes initial opening and splits a stall crossing the first-minute boundary");
        Check(RfiPlaybackChecks.Analyze([new(0, 0, AudioState.Opening), new(1, 0, AudioState.Buffering)]).FirstClockAdvanceSeconds is null,
            "Online analysis never reports an unstarted stream as successful playback");
        var recovery = new PlaybackRecovery();
        Check(!recovery.TryRecover(10, 4, false, false, false), "Steady playback never starts recovery monitoring");
        recovery.BeginWaiting(10);
        Check(!recovery.TryRecover(10, 4, false, false, false), "Enough buffered data without clock advance is not recovery");
        Check(!recovery.TryRecover(10.01, 4, false, false, false), "Clock rounding noise is not recovery");
        Check(!recovery.TryRecover(10.5, 2, false, false, false), "Clock advance without future data is not recovery");
        Check(recovery.TryRecover(11, 3, false, false, false) && !recovery.IsWaiting,
            "Advancing clock with future data recovers without another PLAYING event");
        Check(!recovery.TryRecover(12, 4, false, false, false), "Recovery is published only once");
        foreach (var state in new[] { (true, false, false), (false, true, false), (false, false, true) })
        {
            recovery.BeginWaiting(10);
            Check(!recovery.TryRecover(11, 4, state.Item1, state.Item2, state.Item3), "Paused/seeking/ended playback cannot report recovery");
            Check(!recovery.TryRecover(11, 4, false, false, false), "A position jump during inactive playback is not recovery");
        }
        recovery.BeginWaiting(10);
        Check(!recovery.TryRecover(1, 4, false, false, false), "Backwards position does not recover a previous playback timeline");
        Check(recovery.TryRecover(1.2, 4, false, false, false), "Playback must advance again after a timeline reset");
        recovery.BeginWaiting(double.NaN);
        Check(!recovery.TryRecover(1, 4, false, false, false), "Invalid initial clock does not imply recovery");
        Check(!recovery.TryRecover(double.PositiveInfinity, 4, false, false, false), "Invalid sampled clock does not imply recovery");
        recovery.BeginWaiting(10);
        recovery.Reset();
        Check(!recovery.IsWaiting && !recovery.TryRecover(11, 4, false, false, false), "Stop/explicit PLAYING clears pending recovery");
    }

    public static async Task RunNativeAsync(ResolvedStream stream)
    {
        using var player = new MediaEngineAudioPlayer { IsMuted = true };
        var states = new List<AudioState>();
        player.StatusChanged += status => states.Add(status.State);
        await player.PlayAsync(stream, default);
        await WaitUntilAsync(() => states.Contains(AudioState.Playing));
        await Task.Delay(300);
        var notify = (MediaEngineNotify)typeof(MediaEngineAudioPlayer)
            .GetField("_notify", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!;
        states.Clear();
        // Inject only WAITING into the adapter. The native engine continues playing the local WAV;
        // its real TIMEUPDATE events must recover the status without any new Play/seek call.
        notify.EventNotify(12, UIntPtr.Zero, 0);
        await WaitUntilAsync(() => states.Contains(AudioState.Buffering));
        await WaitUntilAsync(() => states.Contains(AudioState.Playing));
        Check(player.Position > TimeSpan.Zero && states.Count(s => s == AudioState.Playing) == 1,
            "Native TIMEUPDATE restores Playing after WAITING without re-opening the source");
        notify.EventNotify(12, UIntPtr.Zero, 0);
        notify.EventNotify(18, UIntPtr.Zero, 0);
        player.Stop();
        states.Clear();
        await Task.Delay(500);
        Check(states.Count == 0 && player.Position == TimeSpan.Zero, "Queued old-generation recovery is ignored after Stop");
        await player.PlayAsync(stream, default);
        await WaitUntilAsync(() => states.Contains(AudioState.Playing));
        states.Clear();
        notify.EventNotify(12, UIntPtr.Zero, 0);
        notify.EventNotify(18, UIntPtr.Zero, 0);
        await Task.Delay(500);
        Check(states.Count == 0, "Old source callbacks cannot change a new source's playback state");
        player.Dispose();
        notify.EventNotify(12, UIntPtr.Zero, 0);
        notify.EventNotify(18, UIntPtr.Zero, 0);
        states.Clear();
        await Task.Delay(300);
        Check(states.Count == 0, "Disposed adapter ignores late WAITING/TIMEUPDATE callbacks");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine($"PASS: {name}");
    }
}
