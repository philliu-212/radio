using Radio.Core;

namespace Radio.Windows;

// Keep recovery inside the existing Windows adapters; never launch an external player.
internal sealed class SystemAudioPlayer : IAudioPlayer, IPlayerDiagnostics
{
    private readonly Func<IAudioPlayer> _mediaFactory;
    private readonly Func<IAudioPlayer> _nativeFactory;
    private readonly HashSet<string> _compatibilityHosts = new(StringComparer.OrdinalIgnoreCase);
    private IAudioPlayer? _current;
    private Action<PlaybackStatus>? _forwardStatus;
    private ResolvedStream? _stream;
    private bool _usingCompatibility;
    private double _volume = 0.4;
    private bool _muted;
    private bool _disposed;

    public SystemAudioPlayer() : this(() => new MediaEngineAudioPlayer(), () => new WindowsAudioPlayer()) { }
    internal SystemAudioPlayer(Func<IAudioPlayer> mediaFactory, Func<IAudioPlayer> nativeFactory)
    {
        _mediaFactory = mediaFactory;
        _nativeFactory = nativeFactory;
    }

    public event Action<PlaybackStatus>? StatusChanged;
    public string DisplayName => "Windows 系統音訊";
    public IReadOnlyList<int> ChildProcessIds => (_current as IPlayerDiagnostics)?.ChildProcessIds ?? [];
    public object Describe() => new { backend = "system", compatibilityHosts = _compatibilityHosts.ToArray(),
        active = (_current as IPlayerDiagnostics)?.Describe() };

    // Called before Stop disposes the stalled source. Recreating the same MediaEngine
    // cannot recover every process-local HLS failure; the other adapter has its own HTTP client.
    // Keep the alternative path for this host until exit, rather than oscillating.
    internal bool PreferCompatibilityForCurrentSource()
    {
        if (_current is null || _stream is not { IsHls: true } stream || _usingCompatibility) return false;
        return _compatibilityHosts.Add(stream.Uri.Host);
    }

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
        _usingCompatibility = stream.Uri.Host.Equals("hitoradio-ott2b2.cdn.hinet.net", StringComparison.OrdinalIgnoreCase)
            || (stream.IsHls && _compatibilityHosts.Contains(stream.Uri.Host));
        IAudioPlayer player = _usingCompatibility ? _nativeFactory() : _mediaFactory();
        _current = player;
        _stream = stream;
        player.Volume = _volume;
        player.IsMuted = _muted;
        _forwardStatus = status => { if (_current == player) ForwardStatus(status); };
        player.StatusChanged += _forwardStatus;
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
        _stream = null;
        if (player is not null)
        {
            player.StatusChanged -= _forwardStatus;
            player.Dispose();
        }
        _forwardStatus = null;
        StatusChanged?.Invoke(new PlaybackStatus(AudioState.Stopped, "已停止；重新播放會回到直播。"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
