using Radio.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;

namespace Radio.Windows;

public sealed class WindowsAudioPlayer : IAudioPlayer, IPlayerDiagnostics
{
    // This adapter is owned by the UI thread; marshal native callbacks back to it.
    private readonly SynchronizationContext _context = SynchronizationContext.Current
        ?? new WindowsFormsSynchronizationContext();
    private MediaPlayer? _player;
    private MediaSource? _source;
    private AdaptiveMediaSource? _adaptive;
    private global::Windows.Web.Http.HttpClient? _httpClient;
    private int _generation;
    private double _volume = 0.4;
    private bool _muted;
    private bool _disposed;

    public event Action<PlaybackStatus>? StatusChanged;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); if (_player is { } player) player.Volume = _volume; }
    }
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; if (_player is { } player) player.IsMuted = value; }
    }
    public TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;
    internal bool? HlsAudioOnly => _adaptive?.AudioOnlyPlayback;
    internal string VideoSize => _player is { } player
        ? $"{player.PlaybackSession.NaturalVideoWidth}x{player.PlaybackSession.NaturalVideoHeight}" : "0x0";
    public IReadOnlyList<int> ChildProcessIds => [];
    public string DisplayName => "Windows 相容模式";
    public object Describe() => new { backend = "native", hlsAudioOnly = HlsAudioOnly, videoSize = VideoSize };

    public async Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        var generation = _generation;
        MediaPlayer? player = null;
        MediaSource? source = null;
        AdaptiveMediaSource? adaptive = null;
        global::Windows.Web.Http.HttpClient? httpClient = null;
        Publish(AudioState.Opening, "正在連接直播…");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stream.IsHls)
            {
                httpClient = new global::Windows.Web.Http.HttpClient();
                httpClient.DefaultRequestHeaders.Referer = stream.Referrer;
                var result = await AdaptiveMediaSource.CreateFromUriAsync(stream.Uri, httpClient)
                    .AsTask(cancellationToken);
                if (result.Status != AdaptiveMediaSourceCreationStatus.Success)
                    throw new InvalidOperationException($"Windows HLS 初始化失敗：{result.Status}");
                adaptive = result.MediaSource;
                // Limit the requested seek window; actual buffering is controlled by Windows.
                // No application-managed recording or time-shift buffer.
                adaptive.DesiredSeekableWindowSize = TimeSpan.FromSeconds(30);
                source = MediaSource.CreateFromAdaptiveMediaSource(adaptive);
            }
            else
            {
                source = MediaSource.CreateFromUri(stream.Uri);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _generation || _disposed)
                throw new OperationCanceledException(cancellationToken);

            player = new MediaPlayer { Volume = _volume, IsMuted = _muted };
            player.MediaFailed += OnMediaFailed;
            player.MediaEnded += OnMediaEnded;
            player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;
            player.Source = source;
            _player = player;
            _source = source;
            _adaptive = adaptive;
            _httpClient = httpClient;
            player = null;
            source = null;
            adaptive = null;
            httpClient = null;
            _player.Play();
        }
        catch
        {
            if (generation == _generation) Stop();
            throw;
        }
        finally
        {
            player?.Dispose();
            source?.Dispose();
            adaptive?.Dispose();
            httpClient?.Dispose();
        }
    }

    public void Stop()
    {
        _generation++;
        var player = _player;
        _player = null;
        if (player is not null)
        {
            player.MediaFailed -= OnMediaFailed;
            player.MediaEnded -= OnMediaEnded;
            player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;
            player.Pause();
            player.Source = null;
            player.Dispose();
        }
        _source?.Dispose();
        _source = null;
        _adaptive?.Dispose();
        _adaptive = null;
        _httpClient?.Dispose();
        _httpClient = null;
        Publish(AudioState.Stopped, "已停止；重新播放會回到直播。");
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = $"播放失敗：{args.Error}（0x{args.ExtendedErrorCode.HResult:X8}）";
        _context.Post(_ =>
        {
            if (sender != _player) return;
            Stop();
            Publish(AudioState.Failed, message);
        }, null);
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        _context.Post(_ =>
        {
            if (sender != _player) return;
            Stop();
            Publish(AudioState.Failed, "直播已結束，請按播放重新連接。");
        }, null);
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        _context.Post(_ =>
        {
            if (_player is not { } player || sender != player.PlaybackSession) return;
            var state = sender.PlaybackState;
            if (state == MediaPlaybackState.Playing) Publish(AudioState.Playing, "直播播放中");
            else if (state == MediaPlaybackState.Buffering) Publish(AudioState.Buffering, "直播緩衝中…");
        }, null);
    }

    private void Publish(AudioState state, string message) => StatusChanged?.Invoke(new(state, message));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
