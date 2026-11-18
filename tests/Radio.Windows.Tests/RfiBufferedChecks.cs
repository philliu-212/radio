using System.Text.Json;
using System.Threading.Channels;
using System.Diagnostics;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class RfiBufferedChecks
{
    internal static async Task RunLiveUiAsync(string[] args)
    {
        var seconds = Math.Clamp(int.Parse(PlayerBackend.Option(args, "--seconds", "90")), 60, 600);
        var output = Path.GetFullPath(PlayerBackend.Option(args, "--output", "artifacts/rfi-trial-ui.json"));
        using var player = new SystemAudioPlayer { EnableRfiBufferTrial = true, IsMuted = true };
        var clock = Stopwatch.StartNew();
        var events = new List<object>();
        player.StatusChanged += s => events.Add(new { time = clock.Elapsed.TotalSeconds, state = s.State.ToString(), s.Message });
        using var form = new MainForm(player, persistPreferences: false, autoPlayOnStartup: false, checkUpdates: false,
            settings: new ThemeSettings { MyStationIds = ["rfi-monde"], StationSetupCompleted = true });
        var samples = new List<RfiPlaybackChecks.Sample>();
        var snapshots = new List<object>();
        var phase = "visible";
        var phases = new List<object>();
        try
        {
            form.Show();
            var opening = form.PlayForProbeAsync("rfi-monde");
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(250);
                var time = clock.Elapsed.TotalSeconds;
                if (time >= seconds / 3d && phase == "visible")
                {
                    form.WindowState = FormWindowState.Minimized;
                    phase = "minimized";
                    phases.Add(new { time, phase });
                }
                if (time >= seconds * 2 / 3d && phase == "minimized")
                {
                    form.ShowPlayer();
                    phase = "restored";
                    phases.Add(new { time, phase });
                }
                samples.Add(new(time, player.Position.TotalSeconds, form.LatestStatus.State));
                snapshots.Add(new { time, phase, station = form.PlayingStationId, reconnectPending = form.ReconnectPending,
                    detail = player.Describe() });
            }
            await opening;
        }
        finally { form.ExitPlayer(); }
        var continuity = PlaybackContinuity.Analyze(samples);
        var eventJson = JsonSerializer.SerializeToElement(events);
        var buffering = PlaybackContinuity.AnalyzeEvents(eventJson.EnumerateArray()
            .Select(e => new PlaybackContinuity.Event(e.GetProperty("time").GetDouble(), e.GetProperty("state").GetString()!)),
            "Playing", "Buffering", "Paused", "Failed");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, seconds, muted = true, settingsPersisted = false,
            continuity, buffering, phases, events, samples, snapshots,
            limitations = "Actual MainForm with existing source-watch/recovery policy; visible/minimized/restored sequential phases. Muted clock/status diagnostic, not audible or multi-hour acceptance; no saved preferences, recordings or update checks."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RFI actual UI: interruptions={continuity.Interruptions}, longest continuous={continuity.LongestUninterruptedSeconds:F2}s, buffering events={buffering.BufferingEpisodes}, errors={buffering.Errors}; {output}");
        if (!continuity.Started || buffering.Errors != 0) throw new Exception("Trial UI failed to start or reported playback errors.");
    }

    internal static async Task RunAsync()
    {
        var official = new Uri("https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3");
        Check(RfiBufferedStream.Supports(official) && RfiBufferedStream.Supports(new("https://rfiafrique64k.ice.infomaniak.ch/rfimonde-64.mp3")), "Only verified RFI Monde entries are eligible");
        foreach (var uri in new[] { "http://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3", "https://example.invalid/rfimonde-64.mp3",
            "https://rfiafrique64k.ice.infomaniak.ch/rfiafrique-64.mp3", "https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3?token=test",
            "https://user:password@rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3", "https://rfimonde64k.ice.infomaniak.ch:444/rfimonde-64.mp3" })
            Check(!RfiBufferedStream.Supports(new(uri)), "Other source/credentials/port do not enter trial path");

        var immediateInput = new SyntheticStream(2);
        using (var buffer = await RfiBufferedStream.OpenFixtureAsync(immediateInput).WaitAsync(TimeSpan.FromSeconds(2)))
        {
            Check(buffer.QueuedFrames is >= 1 and <= 2, "Opening needs an initial synchronized frame, not a fixed preload");
            var frame = await buffer.ReadAsync(default);
            Check(frame.Data.Length == 209 && frame.SampleRate == 22050 && frame.Samples == 576, "Verified MP3 frame identity/format preserved");
            await buffer.ReadAsync(default);
            var pending = buffer.ReadAsync(default).AsTask();
            buffer.Dispose();
            await buffer.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Pending read survived disposal."); }
            catch (Exception e) when (e is ChannelClosedException or OperationCanceledException) { }
            Check(immediateInput.Disposed && buffer.QueuedFrames == 0, "Stop releases idle input and pending read, discards queued audio");
        }

        var fullInput = new SyntheticStream();
        using (var buffer = await RfiBufferedStream.OpenFixtureAsync(fullInput))
        {
            await UntilAsync(() => buffer.QueuedFrames == RfiBufferedStream.MaxFrames);
            var snapshot = JsonSerializer.SerializeToElement(buffer.Describe());
            Check(buffer.QueuedSeconds < 30 && snapshot.GetProperty("receivedFrames").GetInt64() <= RfiBufferedStream.MaxFrames + 1,
                "Background prefetch is bounded below 30 seconds without dropping frames");
            var initial = await buffer.ReadAsync(default);
            await UntilAsync(() => buffer.QueuedFrames == RfiBufferedStream.MaxFrames);
            Check(initial.Data[0] == 255 && RfiBufferedStream.MaxPayloadBytes < 1024 * 1024, "Compressed queue payload stays below 1 MiB even for variable-bitrate frames");
            buffer.Dispose();
            await buffer.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            Check(fullInput.Disposed && buffer.QueuedFrames == 0, "Cancel while queue is full releases input and memory");
        }

        using (var input = new MemoryStream(Frame()))
        using (var buffer = await RfiBufferedStream.OpenFixtureAsync(input))
        {
            await buffer.Completion;
            await buffer.ReadAsync(default);
            try { await buffer.ReadAsync(default); throw new Exception("Expected EOF failure after draining received frame."); }
            catch (ChannelClosedException error) when (error.InnerException is EndOfStreamException) { }
            Check(true, "Live EOF is not silently accepted as continued playback");
        }
        var variableFrames = new[] { Frame(), VariableFrame(9, 80000), VariableFrame(7, 56000), VariableFrame(8, 64000) };
        using (var input = new MemoryStream(variableFrames.SelectMany(f => f).ToArray()))
        using (var buffer = await RfiBufferedStream.OpenFixtureAsync(input))
        {
            await buffer.Completion;
            foreach (var expected in variableFrames)
            {
                var actual = await buffer.ReadAsync(default);
                Check(actual.Data.SequenceEqual(expected), "Per-frame bitrate changes preserve every complete frame without resynchronization loss");
            }
            Check(JsonSerializer.SerializeToElement(buffer.Describe()).GetProperty("skippedBytes").GetInt64() == 0,
                "Variable-bitrate frame sequence has zero discarded bytes");
        }
        var falseSync = new byte[] { 255, 243, 128, 0, 0, 0, 0 };
        using (var buffer = await RfiBufferedStream.OpenFixtureAsync(new MemoryStream(falseSync.Concat(Frame()).Concat(Frame()).ToArray())))
        {
            await buffer.Completion;
            Check((await buffer.ReadAsync(default)).Data.SequenceEqual(Frame()) && (await buffer.ReadAsync(default)).Data.SequenceEqual(Frame()),
                "Initial false sync is rejected using the next complete header");
            Check(JsonSerializer.SerializeToElement(buffer.Describe()).GetProperty("skippedBytes").GetInt64() == falseSync.Length,
                "Initial partial/false-sync bytes are counted separately from valid frames");
        }
        try { using var invalid = await RfiBufferedStream.OpenFixtureAsync(new MemoryStream(new byte[9000])); throw new Exception("Invalid MP3 opened."); }
        catch (InvalidDataException) { Check(true, "Invalid/unsupported data fails within bounded synchronization"); }
        using (var cancelled = new CancellationTokenSource())
        {
            var input = new SyntheticStream(0);
            var opening = RfiBufferedStream.OpenFixtureAsync(input, cancelled.Token);
            cancelled.Cancel();
            try { using var failed = await opening; throw new Exception("Cancelled opening succeeded."); }
            catch (OperationCanceledException) { }
            Check(input.Disposed, "Cancellation during opening releases the input");
        }

        await CheckRoutingAsync(official);
        await CheckNativeAsync(official);
    }

    private static async Task CheckRoutingAsync(Uri uri)
    {
        var routed = new List<string>();
        using var system = new SystemAudioPlayer(() => new RoutePlayer("media", routed),
            () => new RoutePlayer("native", routed), () => new RoutePlayer("rfi", routed));
        var rfi = new ResolvedStream(uri, new("https://www.rfi.fr/fr/direct-monde"), "audio/mpeg");
        await system.PlayAsync(rfi, default);
        system.EnableRfiBufferTrial = true;
        await system.PlayAsync(rfi, default);
        await system.PlayAsync(new(new("https://example.invalid/other.mp3"), rfi.Referrer, "audio/mpeg"), default);
        Check(routed.SequenceEqual(new[] { "media", "rfi", "media" }), "Default unchanged; opt-in only routes verified RFI; switching away restores normal engine");
        using var configured = PlayerBackend.Create(["--rfi-buffered"]);
        Check(JsonSerializer.SerializeToElement(((IPlayerDiagnostics)configured).Describe()).GetProperty("rfiBufferTrial").GetBoolean(), "Explicit trial switch reaches the system adapter");
    }

    private static async Task CheckNativeAsync(Uri uri)
    {
        var buffers = new List<RfiBufferedStream>();
        using var player = new WindowsAudioPlayer { UseBufferedRfiSource = true, IsMuted = true,
            BufferedRfiFactory = async (_, token) =>
            {
                var buffer = await RfiBufferedStream.OpenFixtureAsync(new SyntheticStream(120), token);
                buffers.Add(buffer);
                return buffer;
            } };
        var events = new List<AudioState>();
        player.StatusChanged += s => events.Add(s.State);
        var stream = new ResolvedStream(uri, new("https://www.rfi.fr/fr/direct-monde"), "audio/mpeg");
        for (var i = 0; i < 2; i++)
        {
            events.Clear();
            await player.PlayAsync(stream, default);
            await UntilAsync(() => events.Contains(AudioState.Playing));
            var initial = player.Position;
            await Task.Delay(500);
            Check(player.Position > initial && !events.Contains(AudioState.Failed), "Synthetic silent MP3 decodes through existing Windows output");
            player.Volume = .25;
            player.IsMuted = true;
            Check(player.Volume == .25 && player.IsMuted, "Trial preserves volume/mute controls");
            player.Stop();
            await buffers[^1].Completion.WaitAsync(TimeSpan.FromSeconds(2));
            var count = events.Count;
            await Task.Delay(300);
            Check(player.Position == TimeSpan.Zero && events.Count == count && buffers[^1].QueuedFrames == 0,
                "Stop/restart discards old audio and late native callbacks");
        }
        var stalledOpening = new SyntheticStream(0);
        player.BufferedRfiFactory = (_, token) => RfiBufferedStream.OpenFixtureAsync(stalledOpening, token);
        var pendingOpen = player.PlayAsync(stream, default);
        await Task.Delay(30);
        player.Stop();
        try { await pendingOpen.WaitAsync(TimeSpan.FromSeconds(2)); throw new Exception("Stop failed to cancel opening."); }
        catch (OperationCanceledException) { }
        Check(stalledOpening.Disposed, "Stop cancels opening even when the caller never cancels its token");
        Console.WriteLine("PASS: RFI buffered trial offline lifecycle, routing and synthetic native MP3 decoding.");
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private static byte[] Frame()
    {
        var frame = new byte[209];
        new byte[] { 255, 243, 130, 0 }.CopyTo(frame, 0);
        return frame; // Valid zero-side-info MPEG-2 Layer III silence, never a recording.
    }

    private static byte[] VariableFrame(int index, int bitrate)
    {
        var data = new byte[72 * bitrate / 22050 + 1];
        new byte[] { 255, 243, (byte)((index << 4) | 2), 0 }.CopyTo(data, 0);
        return data;
    }

    private sealed class SyntheticStream(int? framesBeforeIdle = null) : Stream
    {
        private readonly byte[] _frame = Frame();
        private long _position;
        internal bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = framesBeforeIdle is { } frames ? frames * 209L - _position : long.MaxValue;
            if (remaining <= 0) await Task.Delay(Timeout.Infinite, cancellationToken);
            var count = (int)Math.Min(buffer.Length, remaining);
            for (var i = 0; i < count; i++) buffer.Span[i] = _frame[(_position + i) % 209];
            _position += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RoutePlayer(string name, List<string> routed) : IAudioPlayer
    {
        public event Action<PlaybackStatus>? StatusChanged;
        public double Volume { get; set; }
        public bool IsMuted { get; set; }
        public TimeSpan Position => TimeSpan.Zero;
        public Task PlayAsync(ResolvedStream stream, CancellationToken token) { routed.Add(name); StatusChanged?.Invoke(new(AudioState.Playing, "fixture")); return Task.CompletedTask; }
        public void Stop() { }
        public void Dispose() { }
    }
}
