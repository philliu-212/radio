using System.Threading.Channels;
using System.Collections.Concurrent;

namespace Radio.Windows;

// Opt-in trial. Continuously receives compressed audio independently of decoder
// demand; no fixed startup preload, local HTTP gateway, recording or disk cache.
internal sealed class RfiBufferedStream : IDisposable
{
    internal const int MaxFrames = 1148; // <30s of the verified 576/22050 format.
    internal const int MaxPayloadBytes = MaxFrames * RfiMp3FrameReader.MaxFrameBytes;
    private readonly Channel<RfiMp3Frame> _queue = Channel.CreateBounded<RfiMp3Frame>(new BoundedChannelOptions(MaxFrames)
    {
        // Stop drains the queue concurrently with cancellation of a native read.
        FullMode = BoundedChannelFullMode.Wait, SingleWriter = true, SingleReader = false,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _stop;
    private readonly HttpClient? _http;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _receiving;
    private RfiMp3FrameReader? _reader;
    private int _disposed;
    private long _receivedFrames;
    private readonly ConcurrentDictionary<int, long> _bitrates = new();
    private string? _error;

    private RfiBufferedStream(Uri? uri, Stream? fixture, CancellationToken token)
    {
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
    {
        if (!Supports(uri)) throw new ArgumentException("Only verified RFI Monde MP3 sources support this trial.", nameof(uri));
        return OpenCoreAsync(uri, null, token);
    }

    internal static Task<RfiBufferedStream> OpenFixtureAsync(Stream input, CancellationToken token = default) =>
        OpenCoreAsync(null, input, token);

    private static async Task<RfiBufferedStream> OpenCoreAsync(Uri? uri, Stream? fixture, CancellationToken token)
    {
        var stream = new RfiBufferedStream(uri, fixture, token);
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
    internal int QueuedFrames => _queue.Reader.Count;
    internal double QueuedSeconds => QueuedFrames * (576.0 / 22050);
    internal object Describe() => new
    {
        mode = "rfi-buffered-trial", queuedFrames = QueuedFrames, queuedSeconds = QueuedSeconds,
        maximumFrames = MaxFrames, maximumPayloadBytes = MaxPayloadBytes,
        receivedFrames = Interlocked.Read(ref _receivedFrames), receivedBytes = _reader?.ReceivedBytes ?? 0,
        skippedBytes = _reader?.SkippedBytes ?? 0, frameBitrates = _bitrates.ToArray(),
        receivingCompleted = _receiving.IsCompleted, error = _error
    };

    internal ValueTask<RfiMp3Frame> ReadAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _queue.Reader.ReadAsync(token);
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
            while (true)
            {
                var frame = await _reader.ReadAsync(token).ConfigureAwait(false);
                await _queue.Writer.WriteAsync(frame, token).ConfigureAwait(false);
                Interlocked.Increment(ref _receivedFrames);
                _bitrates.AddOrUpdate(frame.Bitrate, 1, (_, count) => count + 1);
                _ready.TrySetResult(); // Start after an initial synchronized frame, not after filling the reserve.
            }
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
            _queue.Writer.TryComplete(failure);
            input?.Dispose();
            response?.Dispose();
            if (Volatile.Read(ref _disposed) != 0) Drain();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _http?.Dispose();
        Drain();
        _ = _receiving.ContinueWith(_ => _stop.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Drain() { while (_queue.Reader.TryRead(out _)) { } }
}
