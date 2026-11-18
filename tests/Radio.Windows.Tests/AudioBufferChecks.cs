using System.Text.Json;
using System.Threading.Channels;

namespace Radio.Windows.Tests;

internal static class AudioBufferChecks
{
    private sealed record Frame(int Id, byte[] Data, TimeSpan Duration);
    private static Frame Make(int id, int bytes = 100, double seconds = .25) =>
        new(id, new byte[bytes], TimeSpan.FromSeconds(seconds));
    private static AudioFrameBuffer<Frame> Create(AudioBufferOptions options) =>
        new(options, frame => frame.Data.Length, frame => frame.Duration);

    internal static async Task RunAsync()
    {
        foreach (var options in new[]
        {
            new AudioBufferOptions(TimeSpan.FromSeconds(1), 1000, 2),
            new AudioBufferOptions(TimeSpan.FromSeconds(.5), 1000, 10),
            new AudioBufferOptions(TimeSpan.FromSeconds(2), 200, 10)
        })
        {
            var buffer = Create(options);
            await buffer.WriteAsync(Make(1), default);
            await buffer.WriteAsync(Make(2), default);
            var blocked = buffer.WriteAsync(Make(3), default).AsTask();
            Check(!blocked.IsCompleted && buffer.Count == 2 && buffer.PayloadBytes == 200,
                "Frame count, time and byte limits each independently backpressure");
            Check((await buffer.ReadAsync(default)).Id == 1, "Frames remain ordered");
            await blocked.WaitAsync(TimeSpan.FromSeconds(2));
            Check((await buffer.ReadAsync(default)).Id == 2 && (await buffer.ReadAsync(default)).Id == 3,
                "Resume without dropping the blocked frame");
            buffer.Complete();
            await ClosedAsync(buffer.ReadAsync(default).AsTask());
        }

        // No RFI/MP3 assumption: different frame durations and payload sizes.
        var variable = Create(new(TimeSpan.FromSeconds(1), 600));
        await variable.WriteAsync(Make(1, 120, .1), default);
        await variable.WriteAsync(Make(2, 300, .4), default);
        Check(Math.Abs(variable.Seconds - .5) < .000001 && variable.PayloadBytes == 420,
            "Variable-duration/size frames use actual costs, not advertised bitrate");
        var waiting = variable.WriteAsync(Make(3, 200, .2), default).AsTask();
        Check(!waiting.IsCompleted, "Variable payload limit prevents overshoot");
        await variable.ReadAsync(default);
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        var snapshot = JsonSerializer.SerializeToElement(variable.Describe());
        Check(snapshot.GetProperty("peakPayloadBytes").GetInt32() == 500,
            "Diagnostics report measured queue payload peak");
        variable.Complete(new IOException("fixture"));
        await variable.ReadAsync(default);
        await variable.ReadAsync(default);
        try { await variable.ReadAsync(default); throw new Exception("Expected producer failure."); }
        catch (ChannelClosedException error) when (error.InnerException is IOException) { }

        var cancelled = Create(new(TimeSpan.FromSeconds(1), 100, 1));
        await cancelled.WriteAsync(Make(1), default);
        using (var stop = new CancellationTokenSource())
        {
            var pending = cancelled.WriteAsync(Make(2), stop.Token).AsTask();
            stop.Cancel();
            try { await pending; throw new Exception("Expected write cancellation."); }
            catch (OperationCanceledException) { }
        }
        var blockedWrite = cancelled.WriteAsync(Make(3), default).AsTask();
        cancelled.Complete(new OperationCanceledException(), discard: true);
        await ClosedAsync(blockedWrite);
        Check(cancelled.PayloadBytes == 0 && cancelled.Count == 0 && cancelled.Seconds == 0,
            "Stopping a full buffer releases queued payload and wakes the producer");
        var empty = Create(new(TimeSpan.FromSeconds(1), 100));
        var blockedRead = empty.ReadAsync(default).AsTask();
        empty.Complete(discard: true);
        await ClosedAsync(blockedRead);
        Check(true, "Stopping an empty buffer wakes the decoder");
        using (var stop = new CancellationTokenSource())
        {
            var idle = Create(new(TimeSpan.FromSeconds(1), 100));
            var pending = idle.ReadAsync(stop.Token).AsTask();
            stop.Cancel();
            try { await pending; throw new Exception("Expected read cancellation."); }
            catch (OperationCanceledException) { }
            Check(idle.Count == 0, "Cancelling a decoder wait does not fabricate data");
        }

        var invalid = Create(new(TimeSpan.FromSeconds(1), 100));
        foreach (var frame in new[] { Make(1, 101), Make(2, 1, 2), Make(3, 0), Make(4, 1, 0) })
        {
            try { await invalid.WriteAsync(frame, default); throw new Exception("Invalid frame accepted."); }
            catch (InvalidDataException) { }
        }
        foreach (var create in new Action[]
        {
            () => _ = new AudioBufferOptions(TimeSpan.Zero, 100),
            () => _ = new AudioBufferOptions(TimeSpan.FromSeconds(61), 100),
            () => _ = new AudioBufferOptions(TimeSpan.FromSeconds(1), 0),
            () => _ = new AudioBufferOptions(TimeSpan.FromSeconds(1), 8 * 1024 * 1024 + 1),
            () => _ = new AudioBufferOptions(TimeSpan.FromSeconds(1), 100, 0),
            () => _ = new AudioBufferOptions(TimeSpan.FromSeconds(1), 100, 8193)
        })
        {
            try { create(); throw new Exception("Unsafe capacity accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }
        Check(true, "Invalid frames and unsafe capacity fail promptly");

        var exact = Create(new(TimeSpan.FromSeconds(.5), 1000));
        await exact.WriteAsync(Make(1, 1, .5), default);
        var overTime = exact.WriteAsync(Make(2, 1, .001), default).AsTask();
        Check(!overTime.IsCompleted && exact.Seconds == .5, "An exactly full time budget cannot overshoot by a frame");
        exact.Complete(discard: true);
        await ClosedAsync(overTime);

        await CheckReceiverAsync();
        await CheckWaitersAsync();
        await RfiMp3FrameReaderChecks.RunAsync();
        await CheckRfiAdapterAsync();
        Console.WriteLine("PASS: shared audio buffer capacity, cancellation, payload accounting and RFI adapter.");
    }

    private static async Task CheckReceiverAsync()
    {
        var buffer = Create(new(TimeSpan.FromSeconds(1), 200, 2));
        using var stop = new CancellationTokenSource();
        var produced = 0;
        var delivered = 0;
        var receiving = buffer.ReceiveAsync(_ => ValueTask.FromResult(Make(++produced)),
            _ => delivered++, stop.Token);
        Check(produced == 3 && delivered == 2 && buffer.Count == 2,
            "Background receiving starts immediately with only one extra in-flight frame");
        for (var id = 1; id <= 2000; id++)
            CheckId((await buffer.ReadAsync(default)).Id, id);
        stop.Cancel();
        try { await receiving.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Receiver did not stop."); }
        catch (OperationCanceledException) { }
        buffer.Complete(discard: true);
        var snapshot = JsonSerializer.SerializeToElement(buffer.Describe());
        Check(snapshot.GetProperty("peakPayloadBytes").GetInt32() == 200 && buffer.PayloadBytes == 0,
            "Repeated receive/drain cycles keep measured payload bounded and release it on stop");

        var failed = Create(new(TimeSpan.FromSeconds(1), 200));
        var reads = 0;
        var ended = failed.ReceiveAsync(_ => ++reads == 1
            ? ValueTask.FromResult(Make(1))
            : ValueTask.FromException<Frame>(new EndOfStreamException("fixture")), _ => { }, default);
        try { await ended; throw new Exception("Producer EOF was lost."); }
        catch (EndOfStreamException) { }
        Check((await failed.ReadAsync(default)).Id == 1, "Producer failure preserves already received frames");
        try { await failed.ReadAsync(default); throw new Exception("Decoder did not receive EOF."); }
        catch (ChannelClosedException error) when (error.InnerException is EndOfStreamException) { }
    }

    private static async Task CheckRfiAdapterAsync()
    {
        var frame = new byte[209];
        new byte[] { 255, 243, 130, 0 }.CopyTo(frame, 0);
        using var buffer = await RfiBufferedStream.OpenFixtureAsync(
            new MemoryStream(Enumerable.Range(0, 20).SelectMany(_ => frame).ToArray()),
            new AudioBufferOptions(TimeSpan.FromSeconds(1), 418, 10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (buffer.QueuedFrames < 2) await Task.Delay(10, timeout.Token);
        var reserve = JsonSerializer.SerializeToElement(buffer.Describe()).GetProperty("reserve");
        Check(reserve.GetProperty("queuedPayloadBytes").GetInt32() == 418 && !buffer.Completion.IsCompleted,
            "RFI adapter uses configured byte capacity without waiting to fill before opening");
        buffer.Dispose();
        await buffer.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Check(buffer.QueuedFrames == 0, "RFI stop clears shared reserve");
    }

    private static async Task CheckWaitersAsync()
    {
        var buffer = Create(new(TimeSpan.FromSeconds(1), 200, 2));
        using var stop = new CancellationTokenSource();
        var cancelled = buffer.ReadAsync(stop.Token).AsTask();
        var first = buffer.ReadAsync(default).AsTask();
        var second = buffer.ReadAsync(default).AsTask();
        stop.Cancel();
        try { await cancelled; throw new Exception("Expected read cancellation."); }
        catch (OperationCanceledException) { }
        await buffer.WriteAsync(Make(1), default);
        await buffer.WriteAsync(Make(2), default);
        var result = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Check(result.Select(frame => frame.Id).Order().SequenceEqual(new[] { 1, 2 }),
            "Lazy notifications wake all readers without losing frames after another reader cancels");
        await buffer.WriteAsync(Make(3), default);
        await buffer.WriteAsync(Make(4), default);
        var writers = new[] { buffer.WriteAsync(Make(5), default).AsTask(), buffer.WriteAsync(Make(6), default).AsTask() };
        buffer.Complete(discard: true);
        foreach (var writer in writers) await ClosedAsync(writer);
        Check(buffer.PayloadBytes == 0, "Discard wakes all blocked writers and releases queue storage");
    }

    private static async Task ClosedAsync(Task pending)
    {
        try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Expected closed buffer."); }
        catch (ChannelClosedException) { }
    }

    private static void CheckId(int actual, int expected)
    {
        if (actual != expected) throw new Exception($"Frame order: {actual} != {expected}");
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
