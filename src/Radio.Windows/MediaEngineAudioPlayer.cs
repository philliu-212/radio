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
        // Ignore time-update/progress callbacks: the UI has no constantly redrawn clock/animation.
        if (eventId != 5 && eventId != 12 && eventId != 13 && eventId != 19 && eventId != 1005) return;
        _context.Post(_ =>
        {
            if (_disposed || generation != _generation || _engine is null) return;
            if (eventId == 13) Publish(AudioState.Playing, "直播播放中");
            else if (eventId == 12 || eventId == 1005) Publish(AudioState.Buffering, "直播緩衝中…");
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

    public void Stop()
    {
        _generation++;
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
