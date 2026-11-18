using System.Threading.Channels;

namespace Radio.Windows;

// Format-independent compressed-frame reserve. A format adapter supplies complete
// frames and their costs; ownership transfers on WriteAsync (do not mutate them).
// Full queues backpressure the receiver rather than dropping live audio.
internal sealed class AudioFrameBuffer<T>(
    AudioBufferOptions options, Func<T, int> payloadBytes, Func<T, TimeSpan> duration)
{
    private sealed record Entry(T Frame, int Bytes, long Ticks);
    private readonly object _gate = new();
    private readonly Queue<Entry> _frames = new();
    private TaskCompletionSource _changed = NewSignal();
    private int _bytes, _peakBytes, _peakFrames;
    private long _ticks;
    private bool _completed;
    private Exception? _error;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int Count { get { lock (_gate) return _frames.Count; } }
    internal double Seconds { get { lock (_gate) return TimeSpan.FromTicks(_ticks).TotalSeconds; } }
    internal int PayloadBytes { get { lock (_gate) return _bytes; } }

    internal object Describe()
    {
        lock (_gate) return new
        {
            queuedFrames = _frames.Count, queuedSeconds = TimeSpan.FromTicks(_ticks).TotalSeconds,
            queuedPayloadBytes = _bytes, peakPayloadBytes = _peakBytes, peakFrames = _peakFrames,
            maximumSeconds = options.Capacity.TotalSeconds, maximumFrames = options.MaxFrames,
            maximumPayloadBytes = options.MaxPayloadBytes
        };
    }

    // Receive independently of decoder demand, with at most one additional
    // adapter-sized frame in flight beyond the bounded queue.
    internal async Task ReceiveAsync(Func<CancellationToken, ValueTask<T>> read,
        Action<T> received, CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var frame = await read(token).ConfigureAwait(false);
                await WriteAsync(frame, token).ConfigureAwait(false);
                received(frame);
            }
        }
        catch (Exception error)
        {
            Complete(error, discard: token.IsCancellationRequested);
            throw;
        }
    }

    internal async ValueTask WriteAsync(T frame, CancellationToken token)
    {
        var bytes = payloadBytes(frame);
        var ticks = duration(frame).Ticks;
        if (bytes <= 0 || bytes > options.MaxPayloadBytes || ticks <= 0 || ticks > options.Capacity.Ticks)
            throw new InvalidDataException("音訊幀超過緩衝容量或格式無效。");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Task changed;
            lock (_gate)
            {
                if (_completed) throw new ChannelClosedException(_error);
                if (_frames.Count < options.MaxFrames && _bytes + bytes <= options.MaxPayloadBytes
                    && _ticks + ticks <= options.Capacity.Ticks)
                {
                    _frames.Enqueue(new(frame, bytes, ticks));
                    _bytes += bytes;
                    _ticks += ticks;
                    _peakBytes = Math.Max(_peakBytes, _bytes);
                    _peakFrames = Math.Max(_peakFrames, _frames.Count);
                    Pulse();
                    return;
                }
                changed = _changed.Task;
            }
            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    internal async ValueTask<T> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Task changed;
            lock (_gate)
            {
                if (_frames.TryDequeue(out var entry))
                {
                    _bytes -= entry.Bytes;
                    _ticks -= entry.Ticks;
                    Pulse();
                    return entry.Frame;
                }
                if (_completed) throw new ChannelClosedException(_error);
                changed = _changed.Task;
            }
            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    internal void Complete(Exception? error = null, bool discard = false)
    {
        lock (_gate)
        {
            if (!_completed) { _completed = true; _error = error; }
            if (discard) { _frames.Clear(); _bytes = 0; _ticks = 0; }
            Pulse();
        }
    }

    private void Pulse()
    {
        var signal = _changed;
        _changed = NewSignal();
        signal.TrySetResult();
    }
}
