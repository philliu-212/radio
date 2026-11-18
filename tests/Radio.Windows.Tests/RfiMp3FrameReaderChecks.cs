namespace Radio.Windows.Tests;

internal static class RfiMp3FrameReaderChecks
{
    internal static async Task RunAsync()
    {
        var expected = new[] { Frame(8), Frame(14), Frame(7), Frame(9) };
        foreach (var chunk in new[] { 1, 3, 17, 4096 })
        {
            using var input = new FragmentedStream(expected.SelectMany(frame => frame).ToArray(), chunk);
            var reader = new RfiMp3FrameReader(input);
            var received = new List<RfiMp3Frame>();
            foreach (var frame in expected) received.Add(await reader.ReadAsync(default));
            // Check after all reads: earlier payloads must not share parser scratch.
            for (var i = 0; i < expected.Length; i++)
                if (!received[i].Data.SequenceEqual(expected[i]))
                    throw new Exception("Fragmented parsing changed or overwrote a delivered frame.");
            if (received[1].Data.Length != RfiMp3FrameReader.MaxFrameBytes || reader.SkippedBytes != 0)
                throw new Exception("Maximum-size frame was not preserved.");
            try { await reader.ReadAsync(default); throw new Exception("Missing EOF."); }
            catch (EndOfStreamException) { }
        }
        using (var input = new FragmentedStream(Frame(14)[..^1], 1))
        {
            var reader = new RfiMp3FrameReader(input);
            try { await reader.ReadAsync(default); throw new Exception("Truncated maximum-size frame accepted."); }
            catch (EndOfStreamException) { }
        }
        Console.WriteLine("PASS: fixed parser scratch preserves variable/max-size frames across 1/3/17/4096-byte reads, independent payload ownership and truncated EOF.");
    }

    private static byte[] Frame(int bitrateIndex)
    {
        var bitrate = new[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 }[bitrateIndex] * 1000;
        var data = Enumerable.Repeat((byte)bitrateIndex, 72 * bitrate / 22050 + 1).ToArray();
        new byte[] { 255, 242, (byte)((bitrateIndex << 4) | 2), 0 }.CopyTo(data, 0);
        return data;
    }

    private sealed class FragmentedStream(byte[] data, int chunk) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }
}
