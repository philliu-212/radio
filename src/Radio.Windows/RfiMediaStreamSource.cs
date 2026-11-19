using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Security.Cryptography;

namespace Radio.Windows;

// Uses the existing Windows decoder/output. Only HTTP receiving, MP3 framing and
// compressed-data prefetch differ from the default URL source.
internal sealed class RfiMediaStreamSource : IDisposable
{
    private readonly RfiBufferedStream _buffer;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private readonly SemaphoreSlim _reading = new(1, 1);
    private long _samples, _deliveredFrames;
    private int _disposed, _failed;
    internal MediaStreamSource Source { get; }
    internal event Action<RfiMediaStreamSource, Exception>? Failed;

    internal RfiMediaStreamSource(RfiBufferedStream buffer)
    {
        _buffer = buffer;
        _token = _stop.Token;
        Source = new MediaStreamSource(new AudioStreamDescriptor(AudioEncodingProperties.CreateMp3(22050, 2, 64000)))
        {
            CanSeek = false, BufferTime = TimeSpan.FromSeconds(1)
        };
        Source.Starting += Starting;
        Source.SampleRequested += SampleRequested;
    }

    internal object Describe() => new { deliveredFrames = Interlocked.Read(ref _deliveredFrames), buffer = _buffer.Describe() };
    internal Task Completion => _buffer.Completion;

    private void Starting(MediaStreamSource sender, MediaStreamSourceStartingEventArgs args)
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 0 && !_token.IsCancellationRequested)
                args.Request.SetActualStartPosition(TimeSpan.Zero);
        }
        catch (Exception error) { ReportFailure(error); }
    }

    private async void SampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        MediaStreamSourceSampleRequestDeferral? deferral = null;
        var entered = false;
        try
        {
            deferral = args.Request.GetDeferral();
            await _reading.WaitAsync(_token).ConfigureAwait(false);
            entered = true;
            var frame = await _buffer.ReadAsync(_token).ConfigureAwait(false);
            _token.ThrowIfCancellationRequested();
            var start = TimeSpan.FromSeconds((double)_samples / frame.SampleRate);
            _samples += frame.Samples;
            var sample = CreateSample(frame.Data, start);
            sample.Duration = TimeSpan.FromSeconds((double)_samples / frame.SampleRate) - start;
            args.Request.Sample = sample;
            Interlocked.Increment(ref _deliveredFrames);
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
        finally
        {
            if (entered) _reading.Release();
            try { deferral?.Complete(); }
            catch (Exception error) when (error is COMException or ObjectDisposedException) { }
        }
    }

    // Give the decoder its own native storage instead of keeping the parser's
    // small managed array pinned across the sample/projection lifetime.
    internal static MediaStreamSample CreateSample(byte[] data, TimeSpan start)
    {
        var buffer = CryptographicBuffer.CreateFromByteArray(data);
        return MediaStreamSample.CreateFromBuffer(buffer, start);
    }

    private void ReportFailure(Exception error)
    {
        if (Volatile.Read(ref _disposed) == 0 && !_token.IsCancellationRequested && Interlocked.Exchange(ref _failed, 1) == 0)
            Failed?.Invoke(this, error is ChannelClosedException { InnerException: { } inner } ? inner : error);
    }

    // Cancel outstanding native requests before MediaPlayer/MediaSource teardown.
    internal void Cancel() { if (!_token.IsCancellationRequested) _stop.Cancel(); _buffer.Dispose(); }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Cancel();
        Source.Starting -= Starting;
        Source.SampleRequested -= SampleRequested;
        Failed = null;
        _stop.Dispose();
    }
}
