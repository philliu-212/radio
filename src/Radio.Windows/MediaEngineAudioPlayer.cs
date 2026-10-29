using System.Runtime.InteropServices;
using Radio.Core;
using Radio.Windows.Native;

namespace Radio.Windows;

// Our audio adapter over the Windows system engine. No bundled codec/player binaries.
internal sealed class MediaEngineAudioPlayer : IAudioPlayer, IPlayerDiagnostics
{
    private readonly SynchronizationContext _context = SynchronizationContext.Current
        ?? new WindowsFormsSynchronizationContext();
    private IMFMediaEngine? _engine;
    private MediaEngineNotify? _notify;
    private double _volume = 0.4;
    private bool _muted;
    private bool _disposed;
    private int _generation;
    private readonly PlaybackRecovery _recovery = new();
    private int _waitingForProgress;
    private int _progressCheckQueued;

    public MediaEngineAudioPlayer() => Marshal.ThrowExceptionForHR(MediaEngineInterop.MFStartup(0x20070, 0));
    public event Action<PlaybackStatus>? StatusChanged;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Min(1, Math.Max(0, value)); _engine?.SetVolume(_volume); }
    }
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; _engine?.SetMuted(value); }
    }
    public TimeSpan Position
    {
        get
        {
            var seconds = _engine?.GetCurrentTime() ?? 0;
            return double.IsNaN(seconds) || double.IsInfinity(seconds) ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
        }
    }
    public IReadOnlyList<int> ChildProcessIds => [];
    public string DisplayName => "Windows 純音訊";
    public object Describe() => new
    {
        backend = "mediaengine", audioOnlyFlag = true,
        hasVideo = _engine?.HasVideo() ?? false, hasAudio = _engine?.HasAudio() ?? false,
        volume = _engine?.GetVolume(), muted = _engine?.GetMuted()
    };

    public Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MediaEngineAudioPlayer));
        cancellationToken.ThrowIfCancellationRequested();
        Stop();
        var generation = _generation;
        _notify = new MediaEngineNotify(eventId => OnNativeEvent(generation, eventId));
        try
        {
            _engine = MediaEngineInterop.CreateAudioEngine(_notify);
            _engine.SetAutoPlay(false);
            _engine.SetVolume(_volume);
            _engine.SetMuted(_muted);
            Publish(AudioState.Opening, "正在連接純音訊直播…");
            _engine.SetSource(stream.Uri.AbsoluteUri);
            _engine.Play();
            return Task.CompletedTask;
        }
        catch { Stop(); throw; }
    }

    private void OnNativeEvent(int generation, uint eventId)
    {
        // Ignore progress and steady-playback time updates: there is no polling timer or UI clock.
        // BUFFERINGSTARTED (1005) describes source prefetch, not a playback stall.
        // WAITING (12) explicitly means playback stopped because the next frame is unavailable.
        if (eventId == 18)
        {
            if (Volatile.Read(ref _waitingForProgress) == 0 || Interlocked.Exchange(ref _progressCheckQueued, 1) != 0) return;
            _context.Post(_ =>
            {
                Interlocked.Exchange(ref _progressCheckQueued, 0);
                if (_disposed || generation != _generation || _engine is null) return;
                if (_recovery.TryRecover(_engine.GetCurrentTime(), _engine.GetReadyState(),
                    _engine.IsPaused(), _engine.IsSeeking(), _engine.IsEnded()))
                {
                    Volatile.Write(ref _waitingForProgress, 0);
                    Publish(AudioState.Playing, "直播播放中");
                }
            }, null);
            return;
        }
        if (eventId != 5 && eventId != 19 && PlaybackStateForEvent(eventId) is null) return;
        _context.Post(_ =>
        {
            if (_disposed || generation != _generation || _engine is null) return;
            if (PlaybackStateForEvent(eventId) == AudioState.Playing)
            {
                _recovery.Reset();
                Volatile.Write(ref _waitingForProgress, 0);
                Publish(AudioState.Playing, "直播播放中");
            }
            else if (PlaybackStateForEvent(eventId) == AudioState.Buffering)
            {
                _recovery.BeginWaiting(_engine.GetCurrentTime());
                Volatile.Write(ref _waitingForProgress, 1);
                Publish(AudioState.Buffering, "直播緩衝中…");
            }
            else
            {
                var message = "直播已結束，請重新播放。";
                if (eventId == 5)
                {
                    _engine.GetError(out var error);
                    try { message = $"播放失敗（代碼 {error.GetErrorCode()}／0x{error.GetExtendedErrorCode():X8}）。"; }
                    finally { Marshal.ReleaseComObject(error); }
                }
                Stop();
                Publish(AudioState.Failed, message);
            }
        }, null);
    }

    internal static AudioState? PlaybackStateForEvent(uint eventId) => eventId switch
    {
        13 => AudioState.Playing,
        12 => AudioState.Buffering,
        _ => null
    };

    public void Stop()
    {
        _generation++;
        _recovery.Reset();
        Volatile.Write(ref _waitingForProgress, 0);
        var engine = _engine;
        _engine = null;
        if (engine is not null)
        {
            try { engine.Shutdown(); }
            finally { Marshal.ReleaseComObject(engine); }
        }
        GC.KeepAlive(_notify);
        _notify = null;
        Publish(AudioState.Stopped, "已停止；重新播放會回到直播。");
    }

    private void Publish(AudioState state, string message) => StatusChanged?.Invoke(new PlaybackStatus(state, message));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Stop(); }
        finally { Marshal.ThrowExceptionForHR(MediaEngineInterop.MFShutdown()); }
    }
}
