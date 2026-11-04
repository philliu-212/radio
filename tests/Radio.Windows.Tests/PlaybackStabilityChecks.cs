using Radio.Windows;

namespace Radio.Windows.Tests;

internal static class PlaybackStabilityChecks
{
    internal static void Run()
    {
        var policy = new PlaybackStability();
        policy.Reset(TimeSpan.Zero, 0);
        for (var second = 1; second <= 120; second++)
            Check(!policy.Observe(TimeSpan.FromSeconds(second), second), "Advancing playback remains healthy");
        policy.Reset(TimeSpan.Zero, 0);
        for (var second = 1; second <= 120; second++)
            Check(!policy.Observe(TimeSpan.FromSeconds(second), second / 2 * 2), "Coarse clock catch-up is not repeated playback loss");
        policy.Reset(TimeSpan.Zero, 0);
        for (var second = 1; second < 8; second++)
            Check(!policy.Observe(TimeSpan.FromSeconds(second), 0), "Short buffer does not switch");
        Check(policy.Observe(TimeSpan.FromSeconds(8), 0), "Eight seconds without progress switches");
        policy.Reset(TimeSpan.Zero, 0);
        var position = 0d; var triggered = false;
        for (var second = 1; second <= 30; second++)
        {
            if (second % 2 == 0) position++;
            triggered |= policy.Observe(TimeSpan.FromSeconds(second), position);
        }
        Check(triggered, "Repeated brief stalls accumulate to twelve lost seconds in thirty seconds");
        policy.Reset(TimeSpan.Zero, 0);
        position = 0;
        for (var second = 1; second <= 120; second++)
        {
            if (second % 5 != 0) position++;
            Check(!policy.Observe(TimeSpan.FromSeconds(second), position), "Old brief stalls expire from rolling window");
        }
        policy.Reset(TimeSpan.Zero, 0);
        Check(!policy.Observe(TimeSpan.FromSeconds(100), 0), "Sleep/long UI scheduling gap resets baseline, not source");
        Check(!policy.Observe(TimeSpan.FromSeconds(101), 500), "Large timeline jump resets baseline");
        Check(!policy.Observe(TimeSpan.FromSeconds(102), 1), "Backwards timeline resets baseline");
        policy.Reset(TimeSpan.Zero, double.NaN);
        for (var second = 1; second < 8; second++) policy.Observe(TimeSpan.FromSeconds(second), double.NaN);
        Check(policy.Observe(TimeSpan.FromSeconds(8), double.NaN), "Invalid clock cannot hide a sustained stall");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
