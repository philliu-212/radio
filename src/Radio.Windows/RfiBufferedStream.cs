using System.Collections.Concurrent;

namespace Radio.Windows;

// Verified RFI HTTP/format adapter over the shared compressed-frame reserve.
// No fixed startup preload, local HTTP gateway, recording or disk cache.
internal sealed class RfiBufferedStream : IDisposable
{
    internal const int MaxFrames = 1148; // <30s of the verified 576/22050 format.
    internal const int MaxPayloadBytes = MaxFrames * RfiMp3FrameReader.MaxFrameBytes;
    internal static AudioBufferOptions DefaultOptions { get; } =
        new(TimeSpan.FromSeconds(30), MaxPayloadBytes, MaxFrames);
    private readonly AudioBufferOptions _options;
    private readonly AudioFrameBuffer<RfiMp3Frame> _queue;
    private readonly CancellationTokenSource _stop;
    private readonly HttpClient? _http;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _receiving;
    private RfiMp3FrameReader? _reader;
    private int _disposed;
    private long _receivedFrames;
    private readonly ConcurrentDictionary<int, long> _bitrates = new();
    private string? _error;

    private RfiBufferedStream(Uri? uri, Stream? fixture, AudioBufferOptions options, CancellationToken token)
    {
        _options = options;
        _queue = new(options, frame => frame.Data.Length,
            frame => TimeSpan.FromSeconds((double)frame.Samples / frame.SampleRate));
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (fixture is null)
            _http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) })
                { Timeout = Timeout.InfiniteTimeSpan };
        _receiving = Task.Run(() => ReceiveAsync(uri, fixture, _stop.Token));
    }

    internal static bool Supports(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
        && uri.Host is "rfimonde64k.ice.infomaniak.ch" or "rfiafrique64k.ice.infomaniak.ch"
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment)
        && uri.AbsolutePath == "/rfimonde-64.mp3" && string.IsNullOrEmpty(uri.Query);

    internal static Task<RfiBufferedStream> OpenAsync(Uri uri, CancellationToken token)
        => OpenAsync(uri, DefaultOptions, token);

    internal static Task<RfiBufferedStream> OpenAsync(Uri uri, AudioBufferOptions options, CancellationToken token)
    {
        if (!Supports(uri)) throw new ArgumentException("Only verified RFI Monde MP3 sources support this trial.", nameof(uri));
        return OpenCoreAsync(uri, null, options, token);
    }

    internal static Task<RfiBufferedStream> OpenFixtureAsync(Stream input, CancellationToken token = default) =>
        OpenCoreAsync(null, input, DefaultOptions, token);

    internal static Task<RfiBufferedStream> OpenFixtureAsync(Stream input, AudioBufferOptions options, CancellationToken token = default) =>
        OpenCoreAsync(null, input, options, token);

    private static async Task<RfiBufferedStream> OpenCoreAsync(Uri? uri, Stream? fixture, AudioBufferOptions options, CancellationToken token)
    {
        var stream = new RfiBufferedStream(uri, fixture, options, token);
        try
        {
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(token);
            opening.CancelAfter(TimeSpan.FromSeconds(30));
            await stream._ready.Task.WaitAsync(opening.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return stream;
        }
        catch
        {
            stream.Dispose();
            await stream.Completion.ConfigureAwait(false);
            throw;
        }
    }

    internal Task Completion => _receiving;
    internal int QueuedFrames => _queue.Count;
    internal double QueuedSeconds => _queue.Seconds;
    internal object Describe() => new
    {
        mode = "rfi-buffered-trial", queuedFrames = QueuedFrames, queuedSeconds = QueuedSeconds,
        maximumFrames = _options.MaxFrames, maximumPayloadBytes = _options.MaxPayloadBytes,
        reserve = _queue.Describe(),
        receivedFrames = Interlocked.Read(ref _receivedFrames), receivedBytes = _reader?.ReceivedBytes ?? 0,
        skippedBytes = _reader?.SkippedBytes ?? 0, frameBitrates = _bitrates.ToArray(),
        receivingCompleted = _receiving.IsCompleted, error = _error
    };

    internal ValueTask<RfiMp3Frame> ReadAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _queue.ReadAsync(token);
    }

    private async Task ReceiveAsync(Uri? uri, Stream? fixture, CancellationToken token)
    {
        HttpResponseMessage? response = null;
        Stream? input = fixture;
        Exception? failure = null;
        try
        {
            if (input is null)
            {
                using var headers = CancellationTokenSource.CreateLinkedTokenSource(token);
                headers.CancelAfter(TimeSpan.FromSeconds(30));
                response = await _http!.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                // Icecast metadata is not requested. Never forward arbitrary URLs or credentials.
                if (response.Headers.Contains("icy-metaint")) throw new InvalidDataException("Unexpected ICY metadata framing.");
                input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            }
            _reader = new RfiMp3FrameReader(input);
            await _queue.ReceiveAsync(_reader.ReadAsync, frame =>
            {
                Interlocked.Increment(ref _receivedFrames);
                _bitrates.AddOrUpdate(frame.Bitrate, 1, (_, count) => count + 1);
                _ready.TrySetResult(); // Start after an initial synchronized frame, not after filling the reserve.
            }, token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = error;
            if (token.IsCancellationRequested) _ready.TrySetCanceled(token);
            else
            {
                _error = error.GetType().Name + $" (0x{error.HResult:X8})";
                _ready.TrySetException(error);
            }
        }
        finally
        {
            _queue.Complete(failure, discard: Volatile.Read(ref _disposed) != 0);
            input?.Dispose();
            response?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _http?.Dispose();
        _queue.Complete(new OperationCanceledException(), discard: true);
        _ = _receiving.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
