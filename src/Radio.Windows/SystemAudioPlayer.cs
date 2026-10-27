using Radio.Core;

namespace Radio.Windows;

// Deterministic source compatibility, not an error-triggered retry or external-player fallback.
internal sealed class SystemAudioPlayer : IAudioPlayer, IPlayerDiagnostics
{
    private IAudioPlayer? _current;
    private double _volume = 0.4;
    private bool _muted;
    private bool _disposed;

    public event Action<PlaybackStatus>? StatusChanged;
    public string DisplayName => "Windows 系統音訊";
    public IReadOnlyList<int> ChildProcessIds => (_current as IPlayerDiagnostics)?.ChildProcessIds ?? [];
    public object Describe() => new { backend = "system", active = (_current as IPlayerDiagnostics)?.Describe() };
    public TimeSpan Position => _current?.Position ?? TimeSpan.Zero;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Min(1, Math.Max(0, value)); if (_current is { } player) player.Volume = _volume; }
    }
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; if (_current is { } player) player.IsMuted = value; }
    }

    public async Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Stop();
        IAudioPlayer player = stream.Uri.Host.Equals("hitoradio-ott2b2.cdn.hinet.net", StringComparison.OrdinalIgnoreCase)
            ? new WindowsAudioPlayer() : new MediaEngineAudioPlayer();
        _current = player;
        player.Volume = _volume;
        player.IsMuted = _muted;
        player.StatusChanged += ForwardStatus;
        try { await player.PlayAsync(stream, cancellationToken); }
        catch
        {
            if (_current == player) Stop();
            throw;
        }
    }

    private void ForwardStatus(PlaybackStatus status)
    {
        if (status.State == AudioState.Failed) Stop();
        StatusChanged?.Invoke(status);
    }

    public void Stop()
    {
        var player = _current;
        _current = null;
        if (player is not null)
        {
            player.StatusChanged -= ForwardStatus;
            player.Dispose();
        }
        StatusChanged?.Invoke(new PlaybackStatus(AudioState.Stopped, "已停止；重新播放會回到直播。"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
