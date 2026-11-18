namespace Radio.Windows;

internal readonly record struct RfiMp3Frame(byte[] Data, int SampleRate, int Channels, int Bitrate, int Samples);

// Limited to the verified RFI MP3 format, not a general-purpose media parser.
internal sealed class RfiMp3FrameReader(Stream input)
{
    private static readonly int[] Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    internal const int MaxFrameBytes = 523;
    private readonly byte[] _buffer = new byte[4096];
    private readonly byte[] _pending = new byte[MaxFrameBytes + 4];
    private int _offset, _length, _pendingCount;
    private bool _ended, _synchronized;
    internal long ReceivedBytes { get; private set; }
    internal long SkippedBytes { get; private set; }

    internal async ValueTask<RfiMp3Frame> ReadAsync(CancellationToken token)
    {
        var skipped = 0;
        while (true)
        {
            if (!await EnsureAsync(4, token).ConfigureAwait(false)) throw new EndOfStreamException("RFI 直播資料已結束。");
            if (TryHeader(_pending.AsSpan(0, 4), out var length, out var bitrate))
            {
                if (!await EnsureAsync(length, token).ConfigureAwait(false)) throw new EndOfStreamException("RFI MP3 幀不完整。");
                // A partial first MP3 frame can contain sync-like payload bytes.
                // Confirm the next header before accepting initial/resynchronized data.
                var hasNext = _synchronized || !await EnsureAsync(length + 4, token).ConfigureAwait(false)
                    || ValidHeader(_pending.AsSpan(length, 4), out _);
                if (hasNext)
                {
                    // Keep the delivered payload independently owned: native
                    // samples may still use it after the next frame is parsed.
                    var data = _pending.AsSpan(0, length).ToArray();
                    Consume(length);
                    _synchronized = true;
                    return new(data, 22050, 2, bitrate, 576);
                }
            }
            _synchronized = false;
            Consume(1);
            SkippedBytes++;
            if (++skipped > 8192) throw new InvalidDataException("RFI MP3 同步資料超過上限。");
        }
    }

    internal static bool ValidHeader(ReadOnlySpan<byte> h, out int length) => TryHeader(h, out length, out _);

    private void Consume(int count)
    {
        _pendingCount -= count;
        if (_pendingCount != 0)
            Buffer.BlockCopy(_pending, count, _pending, 0, _pendingCount);
    }

    private static bool TryHeader(ReadOnlySpan<byte> h, out int length, out int bitrate)
    {
        length = bitrate = 0;
        // MPEG-2 Layer III / 22050Hz / stereo; CRC and per-frame bitrate changes
        // are allowed. The mount's advertised 64kbps is not a per-frame promise.
        if (h.Length < 4 || h[0] != 255 || (h[1] & 0xfe) != 0xf2 ||
            (h[2] & 0x0c) != 0 || h[2] >> 4 is 0 or 15 || (h[3] >> 6) == 3) return false;
        bitrate = Bitrates[h[2] >> 4] * 1000;
        length = 72 * bitrate / 22050 + ((h[2] >> 1) & 1);
        return true;
    }

    private async ValueTask<bool> EnsureAsync(int count, CancellationToken token)
    {
        while (_pendingCount < count)
        {
            token.ThrowIfCancellationRequested();
            if (_ended) return false;
            if (_offset == _length)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(TimeSpan.FromSeconds(30));
                _length = await input.ReadAsync(_buffer, idle.Token).ConfigureAwait(false);
                _offset = 0;
                if (_length == 0) { _ended = true; return false; }
                ReceivedBytes += _length;
            }
            var copy = Math.Min(count - _pendingCount, _length - _offset);
            Buffer.BlockCopy(_buffer, _offset, _pending, _pendingCount, copy);
            _offset += copy;
            _pendingCount += copy;
        }
        return true;
    }
}
