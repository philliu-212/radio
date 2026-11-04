namespace Radio.Windows;

// Monotonic, clock-based policy, independent of UI visibility or native status chatter.
internal sealed class PlaybackStability
{
    private readonly Queue<(TimeSpan Start, TimeSpan End, double Lost)> _samples = new();
    private TimeSpan _lastSample;
    private TimeSpan _lastProgress;
    private double _position;

    public void Reset(TimeSpan now, double position)
    {
        _samples.Clear(); _lastSample = _lastProgress = now; _position = position;
    }

    public bool Observe(TimeSpan now, double position)
    {
        var elapsed = (now - _lastSample).TotalSeconds;
        var progress = position - _position;
        // Sleep/UI scheduling gaps and stream timeline resets aren't evidence of source failure.
        if (elapsed <= 0 || elapsed > 5 || (double.IsFinite(progress) && (progress < 0 || progress > elapsed + 3)))
        {
            Reset(now, position);
            return false;
        }
        if (double.IsFinite(progress) && progress >= .05) _lastProgress = now;
        // Credit the next clock update when a backend reports progress in coarse chunks;
        // 0s/2s deltas over two 1s samples must not look like 50% audio loss.
        var lost = elapsed - (double.IsFinite(progress) ? Math.Max(progress, 0) : 0);
        _samples.Enqueue((_lastSample, now, lost));
        _lastSample = now; _position = position;
        var windowStart = now - TimeSpan.FromSeconds(30);
        while (_samples.Count > 0 && _samples.Peek().End <= windowStart) _samples.Dequeue();
        var lostInWindow = _samples.Sum(sample => sample.Lost *
            (sample.End - (sample.Start > windowStart ? sample.Start : windowStart)).TotalSeconds /
            (sample.End - sample.Start).TotalSeconds);
        return now - _lastProgress >= TimeSpan.FromSeconds(8) || lostInWindow >= 12;
    }
}
