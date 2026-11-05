namespace Radio.Windows;

// Session-only evidence, never inferred from codec/bitrate or saved as listening statistics.
internal sealed class PlaybackSourcePreference
{
    private TimeSpan[] _lastHealthyRun = [];
    private int _current = -1;
    private TimeSpan _bestHealthyRun;

    public void Reset(int sourceCount)
    {
        _lastHealthyRun = new TimeSpan[sourceCount];
        _current = -1; _bestHealthyRun = TimeSpan.Zero;
    }

    public void BeginAttempt(int index)
    {
        _current = index; _bestHealthyRun = TimeSpan.Zero;
    }

    public void Observe(TimeSpan healthyRun)
    {
        if (_current >= 0 && healthyRun > _bestHealthyRun) _bestHealthyRun = healthyRun;
    }

    public void EndAttempt()
    {
        // Replace old evidence: a formerly good endpoint can become unreliable too.
        if (_current >= 0) _lastHealthyRun[_current] = _bestHealthyRun;
        _current = -1;
    }

    public int[] Order() => Enumerable.Range(0, _lastHealthyRun.Length)
        .OrderByDescending(index => _lastHealthyRun[index]).ToArray();
}
