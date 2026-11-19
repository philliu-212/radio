using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Radio.Core;

namespace Radio.Windows.Tests;

// Explicit long-running diagnostic; never runs as part of an offline profile.
internal static class RfiMemoryChecks
{
    internal static async Task RunAsync(string[] args)
    {
        var mode = PlayerBackend.Option(args, "--mode", "synthetic");
        if (mode is "best-only" or "switch-live" or "switch-synthetic" or "switch-ui-live" or "best-ui-only")
        {
            await RfiSwitchMemoryChecks.RunAsync(args);
            return;
        }
        if (mode is not ("synthetic" or "queue" or "live" or "direct" or "managed-samples" or "native-samples"))
            throw new ArgumentException("Memory mode must be synthetic, queue, live, direct, managed-samples, native-samples, best-only, switch-live, switch-synthetic, switch-ui-live or best-ui-only.");
        var seconds = Math.Clamp(int.Parse(PlayerBackend.Option(args, "--seconds", "300")), 30, 7200);
        var output = Path.GetFullPath(PlayerBackend.Option(args, "--output", $"artifacts/rfi-memory-{mode}.jsonl"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var writer = new StreamWriter(output);
        writer.AutoFlush = true;
        var clock = Stopwatch.StartNew();
        void Write(string kind, object data)
        {
            var line = JsonSerializer.Serialize(new { seconds = clock.Elapsed.TotalSeconds, kind, data });
            writer.WriteLine(line);
            Console.WriteLine(line);
        }
        using var process = Process.GetCurrentProcess();
        using var player = new WindowsAudioPlayer { IsMuted = true, UseBufferedRfiSource = mode != "direct" };
        var failures = 0;
        player.StatusChanged += status =>
        {
            if (status.State == AudioState.Failed) failures++;
            Write("status", status);
        };
        if (mode == "synthetic")
            player.BufferedRfiFactory = (_, token) => RfiBufferedStream.OpenFixtureAsync(new SilentStream(), token);
        var source = new ResolvedStream(new("https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3"),
            new("https://www.rfi.fr/fr/direct-monde"), "audio/mpeg");
        void Snapshot(string phase, object detail)
        {
            process.Refresh();
            Write("memory", new { phase, process.Id, privateBytes = process.PrivateMemorySize64,
                workingSetBytes = process.WorkingSet64, managedBytes = GC.GetTotalMemory(false),
                heap = GC.GetGCMemoryInfo().HeapSizeBytes, committed = GC.GetGCMemoryInfo().TotalCommittedBytes,
                fragmented = GC.GetGCMemoryInfo().FragmentedBytes, pinned = GC.GetGCMemoryInfo().PinnedObjectsCount,
                handles = process.HandleCount, threads = process.Threads.Count,
                gen2 = GC.CollectionCount(2), detail });
        }
        Snapshot("before", player.Describe());
        var lastSnapshot = 0d;
        long samplesCreated = 0;
        var finalPositionSeconds = 0d;
        RfiBufferedStream? queue = null;
        try
        {
            if (mode == "queue") queue = await RfiBufferedStream.OpenFixtureAsync(new SilentStream());
            else if (!mode.EndsWith("-samples")) await player.PlayAsync(source, default);
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                if (mode.EndsWith("-samples"))
                {
                    CreateSamples(mode == "native-samples");
                    samplesCreated += 200;
                    await Task.Delay(100);
                }
                else if (queue is null) await Task.Delay(1000);
                else
                {
                    // Drain one real-time second without any Windows samples.
                    for (var i = 0; i < 38; i++) await queue.ReadAsync(default);
                    await Task.Delay(1000);
                }
                if ((int)clock.Elapsed.TotalSeconds % 10 == 0 && clock.Elapsed.TotalSeconds - lastSnapshot >= 9)
                {
                    Snapshot("playing", queue?.Describe() ?? player.Describe());
                    lastSnapshot = clock.Elapsed.TotalSeconds;
                }
            }
        }
        finally
        {
            finalPositionSeconds = player.Position.TotalSeconds;
            player.Stop();
            queue?.Dispose();
        }
        await Task.Delay(2000);
        Snapshot("stopped", player.Describe());
        // Diagnostic only: distinguish reachable objects from delayed collection.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Snapshot("diagnostic-gc", player.Describe());
        Write("complete", new { mode, seconds, muted = true, samplesCreated, finalPositionSeconds, failures,
            limitations = "Isolated player, no UI/recovery. Synthetic silence is not live reception. End-only forced GC is diagnostic, not a production fix." });
        if (failures != 0 || ((mode is "synthetic" or "live" or "direct") && finalPositionSeconds <= 0))
            throw new Exception("Memory diagnostic reported playback failure or no clock advance; inspect the report.");
    }

    private static void CreateSamples(bool native)
    {
        for (var i = 0; i < 200; i++)
        {
            var bytes = new byte[209];
            var sample = native ? RfiMediaStreamSource.CreateSample(bytes, TimeSpan.Zero)
                : MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), TimeSpan.Zero);
            sample.Duration = TimeSpan.FromTicks(261224);
        }
    }

    internal sealed class SilentStream : Stream
    {
        private readonly byte[] _frame = CreateFrame();
        private long _position;
        private static byte[] CreateFrame()
        {
            var frame = new byte[209];
            new byte[] { 255, 243, 130, 0 }.CopyTo(frame, 0);
            return frame;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < buffer.Length; i++) buffer.Span[i] = _frame[(_position + i) % _frame.Length];
            _position += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
