using System.Runtime.InteropServices.WindowsRuntime;
using Radio.Core;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;

namespace Radio.Windows.Tests;

// Experimental RFI-only source: keep the Windows decoder/output, bypass its HTTP/MP3
// source reader. Not wired into SystemAudioPlayer or shipped as a playback option.
internal sealed class RfiFrameSourcePlayer : IAudioPlayer, IPlayerDiagnostics
{
    private static readonly int[] Rates = [44100, 48000, 32000];
    private static readonly int[] Mpeg1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] Mpeg2Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private readonly SynchronizationContext _context = SynchronizationContext.Current!;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _reading = new(1, 1);
    private MediaPlayer? _player;
    private MediaSource? _source;
    private MediaStreamSource? _frames;
    private HttpResponseMessage? _response;
    private Stream? _input;
    private byte[]? _firstFrame;
    private readonly byte[] _buffer = new byte[4096];
    private int _offset, _length;
    private long _samples, _frameCount, _receivedBytes, _skippedBytes;
    private int _sampleRate, _channels, _bitrate, _samplesPerFrame;
    private volatile bool _stopped;
    private double _volume = .4;
    private bool _muted;

    internal RfiFrameSourcePlayer() => _token = _stop.Token;

    public event Action<PlaybackStatus>? StatusChanged;
    public double Volume { get => _volume; set { _volume = value; if (_player is { } p) p.Volume = value; } }
    public bool IsMuted { get => _muted; set { _muted = value; if (_player is { } p) p.IsMuted = value; } }
    public TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;
    public string DisplayName => "RFI frame-source experiment";
    public IReadOnlyList<int> ChildProcessIds => [];
    public object Describe() => new { backend = "experimental-mp3-frames", positionSeconds = Position.TotalSeconds,
        frames = Interlocked.Read(ref _frameCount), receivedBytes = Interlocked.Read(ref _receivedBytes),
        skippedBytes = Interlocked.Read(ref _skippedBytes), sampleRate = _sampleRate, channels = _channels,
        state = _player?.PlaybackSession.PlaybackState.ToString() };

    public async Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken)
    {
        if (_player is not null || _stopped) throw new InvalidOperationException("Single-use experimental player.");
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
        opening.CancelAfter(TimeSpan.FromSeconds(20));
        StatusChanged?.Invoke(new(AudioState.Opening, "Experimental MP3 frame source"));
        _response = await _http.GetAsync(stream.Uri, HttpCompletionOption.ResponseHeadersRead, opening.Token);
        _response.EnsureSuccessStatusCode();
        _input = await _response.Content.ReadAsStreamAsync(opening.Token);
        _firstFrame = await ReadFrameAsync(opening.Token);
        var encoding = AudioEncodingProperties.CreateMp3((uint)_sampleRate, (uint)_channels, (uint)_bitrate);
        _frames = new MediaStreamSource(new AudioStreamDescriptor(encoding))
        {
            CanSeek = false,
            BufferTime = TimeSpan.FromSeconds(1)
        };
        _frames.Starting += Starting;
        _frames.SampleRequested += SampleRequested;
        _source = MediaSource.CreateFromMediaStreamSource(_frames);
        _player = new MediaPlayer { Volume = _volume, IsMuted = _muted };
        _player.MediaFailed += Failed;
        _player.PlaybackSession.PlaybackStateChanged += StateChanged;
        _player.Source = _source;
        _player.Play();
    }

    private void Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args) =>
        args.Request.SetActualStartPosition(TimeSpan.Zero);

    private async void SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        var token = _token;
        var entered = false;
        try
        {
            await _reading.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(TimeSpan.FromSeconds(20));
            var frame = _firstFrame ?? await ReadFrameAsync(idle.Token).ConfigureAwait(false);
            _firstFrame = null;
            var start = TimeSpan.FromSeconds((double)_samples / _sampleRate);
            _samples += _samplesPerFrame;
            var end = TimeSpan.FromSeconds((double)_samples / _sampleRate);
            var sample = MediaStreamSample.CreateFromBuffer(frame.AsBuffer(), start);
            sample.Duration = end - start;
            args.Request.Sample = sample;
            Interlocked.Increment(ref _frameCount);
        }
        catch (Exception error)
        {
            if (!_stopped) Publish(new(AudioState.Failed, "Frame source: " + error.GetType().Name));
        }
        finally
        {
            if (entered) _reading.Release();
            deferral.Complete();
        }
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken token)
    {
        var header = new byte[4];
        for (var i = 0; i < 4; i++) header[i] = await ByteAsync(token).ConfigureAwait(false);
        var skipped = 0;
        while (!TryHeader(header, out _, out _, out _, out _, out _))
        {
            if (++skipped > 8192) throw new InvalidDataException("No bounded MP3 synchronization.");
            header[0] = header[1]; header[1] = header[2]; header[2] = header[3];
            header[3] = await ByteAsync(token).ConfigureAwait(false);
            Interlocked.Increment(ref _skippedBytes);
        }
        TryHeader(header, out var rate, out var channels, out var bitrate, out var samples, out var length);
        if (_sampleRate != 0 && (_sampleRate != rate || _channels != channels || _samplesPerFrame != samples))
            throw new InvalidDataException("MP3 format changed.");
        _sampleRate = rate; _channels = channels; _bitrate = bitrate; _samplesPerFrame = samples;
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        for (var i = 4; i < frame.Length; i++) frame[i] = await ByteAsync(token).ConfigureAwait(false);
        return frame;
    }

    private async ValueTask<byte> ByteAsync(CancellationToken token)
    {
        if (_offset == _length)
        {
            _length = await _input!.ReadAsync(_buffer, token).ConfigureAwait(false);
            _offset = 0;
            if (_length == 0) throw new EndOfStreamException();
            Interlocked.Add(ref _receivedBytes, _length);
        }
        return _buffer[_offset++];
    }

    internal static bool TryHeader(byte[] h, out int rate, out int channels, out int bitrate, out int samples, out int length)
    {
        rate = channels = bitrate = samples = length = 0;
        if (h.Length < 4) return false;
        var version = (h[1] >> 3) & 3;
        var index = h[2] >> 4;
        var frequency = (h[2] >> 2) & 3;
        if (h[0] != 255 || (h[1] & 0xe0) != 0xe0 || version == 1 || (h[1] & 6) != 2 ||
            index is 0 or 15 || frequency == 3) return false;
        rate = Rates[frequency] / (version == 3 ? 1 : version == 2 ? 2 : 4);
        bitrate = (version == 3 ? Mpeg1Bitrates[index] : Mpeg2Bitrates[index]) * 1000;
        channels = (h[3] >> 6) == 3 ? 1 : 2;
        samples = version == 3 ? 1152 : 576;
        length = (version == 3 ? 144 : 72) * bitrate / rate + ((h[2] >> 1) & 1);
        return true;
    }

    private void StateChanged(MediaPlaybackSession sender, object args)
    {
        var state = sender.PlaybackState;
        if (state == MediaPlaybackState.Playing) Publish(new(AudioState.Playing, "Experimental frame source playing"));
        else if (state == MediaPlaybackState.Buffering) Publish(new(AudioState.Buffering, "Experimental frame source buffering"));
    }

    private void Failed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
        Publish(new(AudioState.Failed, $"Frame decoder: {args.Error} 0x{args.ExtendedErrorCode.HResult:X8}"));

    private void Publish(PlaybackStatus status) => _context.Post(_ =>
    {
        if (!_stopped) StatusChanged?.Invoke(status);
    }, null);

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _stop.Cancel();
        if (_player is { } player)
        {
            player.MediaFailed -= Failed;
            player.PlaybackSession.PlaybackStateChanged -= StateChanged;
            player.Source = null;
            player.Dispose();
            _player = null;
        }
        if (_frames is { } frames)
        {
            frames.Starting -= Starting;
            frames.SampleRequested -= SampleRequested;
        }
        _source?.Dispose();
        _source = null;
        _frames = null;
        _input?.Dispose();
        _response?.Dispose();
        _http.Dispose();
        StatusChanged?.Invoke(new(AudioState.Stopped, "Experiment stopped"));
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }

    internal static async Task CheckParserAsync()
    {
        Check([255, 243, 128, 0], 22050, 2, 64000, 576, 208);
        Check([255, 243, 130, 0], 22050, 2, 64000, 576, 209);
        Check([255, 251, 144, 0], 44100, 2, 128000, 1152, 417);
        Check([255, 227, 128, 192], 11025, 1, 64000, 576, 417);
        foreach (byte[] invalid in new byte[][] { [], [255, 243, 128], [0, 243, 128, 0],
            [255, 235, 128, 0], [255, 245, 128, 0], [255, 243, 0, 0], [255, 243, 240, 0], [255, 243, 140, 0] })
            if (TryHeader(invalid, out _, out _, out _, out _, out _)) throw new Exception("Invalid MP3 header accepted.");

        var frame = new byte[208];
        new byte[] { 255, 243, 128, 0 }.CopyTo(frame, 0);
        using (var reader = new RfiFrameSourcePlayer())
        {
            reader._input = new FragmentedStream(new byte[] { 0, 1, 2 }.Concat(frame).Concat(frame).ToArray());
            if (!(await reader.ReadFrameAsync(default)).SequenceEqual(frame) ||
                !(await reader.ReadFrameAsync(default)).SequenceEqual(frame) || reader._skippedBytes != 3)
                throw new Exception("MP3 initial sync or consecutive frame identity failed.");
            try { await reader.ReadFrameAsync(default); throw new Exception("Expected MP3 EOF."); }
            catch (EndOfStreamException) { }
        }
        using (var reader = new RfiFrameSourcePlayer())
        {
            reader._input = new MemoryStream(new byte[9000]);
            try { await reader.ReadFrameAsync(default); throw new Exception("Expected bounded sync failure."); }
            catch (InvalidDataException) { }
        }
        using (var reader = new RfiFrameSourcePlayer())
        {
            reader._input = new MemoryStream(frame[..100]);
            try { await reader.ReadFrameAsync(default); throw new Exception("Expected truncated frame failure."); }
            catch (EndOfStreamException) { }
        }
        using (var reader = new RfiFrameSourcePlayer())
        {
            reader._input = new MemoryStream(frame.Concat(new byte[] { 255, 251, 144, 0 }).ToArray());
            await reader.ReadFrameAsync(default);
            try { await reader.ReadFrameAsync(default); throw new Exception("Expected format-change failure."); }
            catch (InvalidDataException) { }
        }
        using (var reader = new RfiFrameSourcePlayer())
        using (var cancelled = new CancellationTokenSource())
        {
            reader._input = new MemoryStream(frame);
            cancelled.Cancel();
            try { await reader.ReadFrameAsync(cancelled.Token); throw new Exception("Expected cancelled read."); }
            catch (OperationCanceledException) { }
        }
        Console.WriteLine("PASS: MP3 headers, bounded sync, fragmented frame identity, EOF, truncation, format change and cancellation (synthetic data only).");

        static void Check(byte[] header, int rate, int channels, int bitrate, int samples, int length)
        {
            if (!TryHeader(header, out var r, out var c, out var b, out var s, out var l) ||
                (r, c, b, s, l) != (rate, channels, bitrate, samples, length)) throw new Exception("MP3 header mismatch.");
        }
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], cancellationToken);
    }
}
