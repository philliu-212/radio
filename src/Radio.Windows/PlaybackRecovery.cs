namespace Radio.Windows;

// WAITING is a playback stall, but Windows does not always emit PLAYING again on recovery.
// A ready-state change alone is insufficient: require a genuinely advancing playback clock.
internal sealed class PlaybackRecovery
{
    private double _position;
    public bool IsWaiting { get; private set; }

    public void BeginWaiting(double position)
    {
        _position = position;
        IsWaiting = true;
    }

    public bool TryRecover(double position, ushort readyState, bool paused, bool seeking, bool ended)
    {
        if (!IsWaiting) return false;
        if (!double.IsFinite(position) || !double.IsFinite(_position) || position < _position || paused || seeking || ended)
        {
            _position = position;
            return false;
        }
        if (readyState < 3 || position - _position < 0.05) return false;
        Reset();
        return true;
    }

    public void Reset() => IsWaiting = false;
}
