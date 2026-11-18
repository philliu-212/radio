using System.Diagnostics;

namespace Radio.Windows.Tests;

// Controlled synchronous hot-path allocation measurement, not process working set.
internal static class AudioBufferAllocationChecks
{
    internal static void Run()
    {
        const int frames = 10000;
        MeasureQueue(1000);
        MeasureParser(1000);
        var queue = MeasureQueue(frames);
        var parser = MeasureParser(frames);
        Console.WriteLine($"Queue steady-state: {queue.Bytes} allocated bytes / {frames} frames ({(double)queue.Bytes / frames:F2} B/frame), {queue.Elapsed.TotalMilliseconds:F2}ms");
        Console.WriteLine($"MP3 parser: {parser.Bytes} allocated bytes / {frames - 1} frames ({(double)parser.Bytes / (frames - 1):F2} B/frame), {parser.Elapsed.TotalMilliseconds:F2}ms");
        Console.WriteLine("Measurement includes managed allocations on a synchronous thread; synthetic input and frame creation for the queue are outside the measured interval. Not working set, live latency or total playback memory.");
        if (queue.Bytes > frames || parser.Bytes > (frames - 1) * 280L)
            throw new Exception("Audio hot-path allocation budget regressed.");
        Console.WriteLine("PASS: allocation budgets (queue <=1 B/frame; parser <=280 B/frame for the 209-byte fixture).");
    }

    private static (long Bytes, TimeSpan Elapsed) MeasureQueue(int count)
    {
        var buffer = new AudioFrameBuffer<byte[]>(new(TimeSpan.FromSeconds(1), 4096),
            frame => frame.Length, _ => TimeSpan.FromMilliseconds(25));
        var frame = new byte[209];
        // Warm the queue backing storage and async methods before measuring.
        buffer.WriteAsync(frame, default).GetAwaiter().GetResult();
        buffer.ReadAsync(default).GetAwaiter().GetResult();
        var clock = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++)
        {
            buffer.WriteAsync(frame, default).GetAwaiter().GetResult();
            if (!ReferenceEquals(buffer.ReadAsync(default).GetAwaiter().GetResult(), frame))
                throw new Exception("Queue changed frame identity.");
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        clock.Stop();
        buffer.Complete(discard: true);
        return (bytes, clock.Elapsed);
    }

    private static (long Bytes, TimeSpan Elapsed) MeasureParser(int count)
    {
        var source = new byte[count * 209];
        for (var i = 0; i < count; i++)
            new byte[] { 255, 243, 130, 0 }.CopyTo(source, i * 209);
        using var input = new MemoryStream(source);
        var reader = new RfiMp3FrameReader(input);
        // Parse the initial header before measuring the synchronized hot path.
        reader.ReadAsync(default).GetAwaiter().GetResult();
        var clock = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i < count; i++)
        {
            var frame = reader.ReadAsync(default).GetAwaiter().GetResult();
            if (frame.Data.Length != 209 || frame.Data[0] != 255 || frame.Samples != 576)
                throw new Exception("Parser changed synthetic frame data.");
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        clock.Stop();
        return (bytes, clock.Elapsed);
    }
}
