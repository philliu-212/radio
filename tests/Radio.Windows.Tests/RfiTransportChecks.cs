using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Radio.Core;

namespace Radio.Windows.Tests;

// Explicit live experiment, not a production proxy or an offline acceptance test.
// Keep decoding/output identical; change only who opens the remote HTTP connection.
internal static class RfiTransportChecks
{
    internal static async Task RunOfflineAsync()
    {
        await RfiFrameSourcePlayer.CheckParserAsync();
        PlaybackContinuity.Check();
        await CheckFanoutAsync();
        // Synthetic bytes only: verify forwarding before upstream EOF, byte identity,
        // more than one read, and cancellation while the upstream connection is idle.
        using var upstream = new HttpListener();
        var upstreamUri = LoopbackUri();
        upstream.Prefixes.Add(upstreamUri.AbsoluteUri);
        upstream.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var releaseTail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payload = Enumerable.Range(0, 12000).Select(i => (byte)(i % 251)).ToArray();
        var serving = Task.Run(async () =>
        {
            var context = await upstream.GetContextAsync().WaitAsync(deadline.Token);
            try
            {
                context.Response.ContentType = "audio/mpeg";
                context.Response.SendChunked = true;
                await context.Response.OutputStream.WriteAsync(payload.AsMemory(0, 1000), deadline.Token);
                await context.Response.OutputStream.FlushAsync(deadline.Token);
                await releaseTail.Task.WaitAsync(deadline.Token);
                await context.Response.OutputStream.WriteAsync(payload.AsMemory(1000), deadline.Token);
            }
            finally { context.Response.Close(); }
            var idle = await upstream.GetContextAsync().WaitAsync(deadline.Token);
            try
            {
                idle.Response.SendChunked = true;
                await idle.Response.OutputStream.WriteAsync(new byte[] { 1 }, deadline.Token);
                await idle.Response.OutputStream.FlushAsync(deadline.Token);
                await Task.Delay(Timeout.Infinite, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            finally { idle.Response.Close(); }
        });
        await using var relay = new ImmediateRelay(upstreamUri, Stopwatch.StartNew());
        using var client = new HttpClient();
        try
        {
            using var response = await client.GetAsync(relay.Uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var received = new MemoryStream();
            var first = new byte[1000];
            await input.ReadExactlyAsync(first, deadline.Token);
            received.Write(first);
            releaseTail.SetResult();
            await input.CopyToAsync(received, deadline.Token);
            if (!received.ToArray().SequenceEqual(payload)) throw new Exception("Relay changed synthetic bytes.");
            using var idleResponse = await client.GetAsync(relay.Uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            using var idleInput = await idleResponse.Content.ReadAsStreamAsync(deadline.Token);
            await idleInput.ReadExactlyAsync(new byte[1], deadline.Token);
            await relay.DisposeAsync().AsTask().WaitAsync(deadline.Token);
            Console.WriteLine("PASS: immediate first chunk, unchanged synthetic bytes across reads, EOF and idle-read cancellation (loopback only).");
        }
        finally
        {
            releaseTail.TrySetResult();
            deadline.Cancel();
            try { await serving; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (HttpListenerException) when (deadline.IsCancellationRequested) { }
            finally { upstream.Close(); }
        }
    }

    private static Uri LoopbackUri()
    {
        // A rare free-port race fails explicitly, never binds another interface.
        var picker = new TcpListener(IPAddress.Loopback, 0);
        picker.Start();
        var port = ((IPEndPoint)picker.LocalEndpoint).Port;
        picker.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    private static async Task CheckFanoutAsync()
    {
        using var upstream = new HttpListener();
        var uri = LoopbackUri();
        upstream.Prefixes.Add(uri.AbsoluteUri);
        upstream.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var payload = Enumerable.Range(0, 16000).Select(i => (byte)(i % 239)).ToArray();
        var serving = Task.Run(async () =>
        {
            var context = await upstream.GetContextAsync().WaitAsync(deadline.Token);
            try
            {
                context.Response.ContentType = "audio/mpeg";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload, deadline.Token);
            }
            finally { context.Response.Close(); }
        });
        await using var relay = new ImmediateRelay(uri, Stopwatch.StartNew(), sameData: true);
        using var client = new HttpClient();
        var responses = await Task.WhenAll(client.GetByteArrayAsync(relay.Uri, deadline.Token),
            client.GetByteArrayAsync(relay.Uri, deadline.Token));
        await serving;
        await relay.DisposeAsync();
        if (responses.Any(r => !r.SequenceEqual(payload))) throw new Exception("Same-data fanout changed bytes.");
        var headers = relay.Observations.Select(o => JsonSerializer.SerializeToElement(o))
            .Where(o => o.GetProperty("kind").GetString() == "headers").ToArray();
        if (headers.Length != 1 || headers[0].GetProperty("consumers").GetInt32() != 2)
            throw new Exception("Same-data fanout must have one upstream and two consumers.");
        Console.WriteLine("PASS: one upstream sends identical synthetic bytes to two consumers without replay storage.");
    }

    internal static async Task RunAsync(string[] args)
    {
        var seconds = Math.Clamp(int.Parse(Option(args, "--seconds", "180")), 60, 1800);
        var output = Path.GetFullPath(Option(args, "--output", "artifacts/rfi-transport.json"));
        var station = StationCatalog.Load().Single(s => s.Id == "rfi-monde");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stream = await new OfficialStreamResolver().ResolveAsync(station, timeout.Token);
        var clock = Stopwatch.StartNew();
        var sameData = args.Contains("--same-data");
        await using var relay = new ImmediateRelay(stream.Uri, clock, sameData);
        using var direct = new SystemAudioPlayer(() => new MediaEngineAudioPlayer(), () => new WindowsAudioPlayer(),
            () => new MediaEngineAudioPlayer()) { IsMuted = true };
        using var forwarded = new SystemAudioPlayer(() => new MediaEngineAudioPlayer(), () => new WindowsAudioPlayer(),
            () => new MediaEngineAudioPlayer()) { IsMuted = true };
        using var frames = new RfiFrameSourcePlayer { IsMuted = true };
        using var trialHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var buffered = new SystemAudioPlayer(() => new MediaEngineAudioPlayer(), () => new WindowsAudioPlayer(),
            () => new WindowsAudioPlayer
            {
                UseBufferedRfiSource = true,
                BufferedRfiFactory = async (uri, token) => sameData
                    ? await RfiBufferedStream.OpenFixtureAsync(await trialHttp.GetStreamAsync(relay.Uri, token), token)
                    : await RfiBufferedStream.OpenAsync(uri, token)
            }) { IsMuted = true };
        var players = new Dictionary<string, IAudioPlayer> { ["direct"] = direct, ["dotnetHttp"] = forwarded };
        if (sameData)
        {
            players.Clear();
            players.Add("systemSameData", direct);
            players.Add(args.Contains("--buffered") ? "bufferedSameData" : "framesSameData", args.Contains("--buffered") ? buffered : frames);
        }
        else if (args.Contains("--buffered")) players.Add("bufferedTrial", buffered);
        else if (args.Contains("--frames")) players.Add("frameSource", frames);
        var samples = players.Keys.ToDictionary(n => n, _ => new List<RfiPlaybackChecks.Sample>());
        var states = players.Keys.ToDictionary(n => n, _ => AudioState.Stopped);
        var events = new List<object>();
        var diagnostics = new List<object>();
        var errors = new List<string>();
        foreach (var (name, player) in players)
            player.StatusChanged += status =>
            {
                states[name] = status.State;
                events.Add(new { time = clock.Elapsed.TotalSeconds, name, state = status.State.ToString(), status.Message });
                if (status.State == AudioState.Failed) errors.Add(name + ": " + status.Message);
            };
        try
        {
            var opening = Task.WhenAll(players.Select(async entry =>
            {
                try
                {
                    var source = (sameData && entry.Key != "bufferedSameData") || entry.Key == "dotnetHttp"
                        ? new ResolvedStream(relay.Uri, stream.Referrer, "audio/mpeg") : stream;
                    await entry.Value.PlayAsync(source, default);
                }
                catch (Exception error)
                {
                    states[entry.Key] = AudioState.Failed;
                    errors.Add(entry.Key + ": opening " + error.GetType().Name);
                }
            }));
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(250);
                var time = clock.Elapsed.TotalSeconds;
                foreach (var (name, player) in players)
                {
                    samples[name].Add(new(time, player.Position.TotalSeconds, states[name]));
                    diagnostics.Add(new { time, name, detail = ((IPlayerDiagnostics)player).Describe() });
                }
            }
            await opening;
        }
        finally
        {
            foreach (var player in players.Values) player.Stop();
            await relay.DisposeAsync();
        }
        var summaries = samples.ToDictionary(p => p.Key, p => RfiPlaybackChecks.Analyze(p.Value));
        var continuity = samples.ToDictionary(p => p.Key, p => PlaybackContinuity.Analyze(p.Value));
        var eventJson = JsonSerializer.SerializeToElement(events);
        var bufferingEvents = players.Keys.ToDictionary(name => name, name => PlaybackContinuity.AnalyzeEvents(
            eventJson.EnumerateArray().Where(e => e.GetProperty("name").GetString() == name)
                .Select(e => new PlaybackContinuity.Event(e.GetProperty("time").GetDouble(), e.GetProperty("state").GetString()!)),
            "Playing", "Buffering", "Paused", "Failed"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, seconds, muted = true, sameData, streamHost = stream.Uri.Host,
            summaries, continuity, bufferingEvents, events, errors, samples, diagnostics,
            transport = relay.Observations.ToArray(),
            limitations = "Diagnostic only. Default mode uses independent upstream TCP connections. sameData mode waits for both local consumers before one upstream connection, forwards identical chunks to both without a replay queue, and aborts if either consumer disconnects; local HTTP clients and source-reader paths still differ. direct/dotnetHttp/systemSameData use unchanged SystemAudioPlayer without MainForm recovery. Relay immediately forwards <=4096 bytes with no deliberate prebuffer or recording. Optional frameSource/framesSameData bypasses the system HTTP/MP3 source reader, uses the Windows decoder/output and requests a 1s system buffer (not a fixed startup delay). Clock stalls are not audible verification. ClockAdvanceSeconds from the existing analyzer is invalid after a clock reset: use per-stall intervals and failure events instead. No Edge comparison in this experiment. No stability pass/fail threshold."
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var (name, summary) in summaries)
            Console.WriteLine($"{name}: startup={summary.FirstClockAdvanceSeconds:F2}s, first-minute stalls={summary.FirstMinuteStallSeconds:F2}s, later stalls={summary.LaterStallSeconds:F2}s");
        foreach (var (name, profile) in continuity)
            Console.WriteLine($"{name}: interruptions(>=0.5s)={profile.Interruptions}, longest uninterrupted={profile.LongestUninterruptedSeconds:F2}s, completed interval median={profile.MedianCompletedUninterruptedSeconds:F2}s, current uninterrupted={profile.CurrentUninterruptedSeconds:F2}s");
        Console.WriteLine($"EXPERIMENT COMPLETE (not stability acceptance): errors={errors.Count}; report={output}");
    }

    private static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    internal sealed class ImmediateRelay : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly HttpClient _client = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) })
            { Timeout = Timeout.InfiniteTimeSpan };
        private readonly Uri _upstream;
        private readonly Stopwatch _clock;
        private readonly Task _accepting;
        private readonly List<Task> _requests = [];
        private readonly bool _sameData;
        private readonly List<HttpListenerContext> _peers = [];
        private int _disposed;
        internal Uri Uri { get; }
        internal ConcurrentQueue<object> Observations { get; } = new();

        internal ImmediateRelay(Uri upstream, Stopwatch clock, bool sameData = false)
        {
            _upstream = upstream;
            _clock = clock;
            _sameData = sameData;
            Uri = LoopbackUri();
            _listener.Prefixes.Add(Uri.AbsoluteUri);
            _listener.Start();
            _accepting = AcceptAsync();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    var path = context.Request.Url?.AbsolutePath;
                    if (path == "/probe.html")
                    {
                        var html = "<!doctype html><html><head><title>Controlled RFI audio comparison</title></head><body></body></html>"u8.ToArray();
                        context.Response.ContentType = "text/html; charset=utf-8";
                        context.Response.ContentLength64 = html.Length;
                        await context.Response.OutputStream.WriteAsync(html, _stop.Token).ConfigureAwait(false);
                        context.Response.Close();
                        continue;
                    }
                    if (path is not ("/" or "/live.mp3"))
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        continue;
                    }
                    if (_requests.Count >= 4 || (_sameData && _peers.Count >= 2))
                    {
                        context.Response.StatusCode = 503;
                        context.Response.Close();
                        continue;
                    }
                    if (_sameData)
                    {
                        _peers.Add(context);
                        if (_peers.Count == 2) _requests.Add(ForwardAsync(_peers.ToArray(), 1));
                    }
                    else _requests.Add(ForwardAsync([context], _requests.Count + 1));
                }
            }
            catch (Exception error) when (_stop.IsCancellationRequested && error is HttpListenerException or ObjectDisposedException) { }
        }

        private async Task ForwardAsync(HttpListenerContext[] contexts, int request)
        {
            var started = _clock.Elapsed.TotalSeconds;
            try
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(20));
                using var message = new HttpRequestMessage(HttpMethod.Get, _upstream);
                using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
                Observations.Enqueue(new { request, consumers = contexts.Length, time = _clock.Elapsed.TotalSeconds, kind = "headers", seconds = _clock.Elapsed.TotalSeconds - started,
                    status = (int)response.StatusCode, version = response.Version.ToString(), contentType = response.Content.Headers.ContentType?.MediaType });
                response.EnsureSuccessStatusCode();
                foreach (var context in contexts)
                {
                    context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
                    context.Response.SendChunked = true;
                    context.Response.Headers["Cache-Control"] = "no-store";
                }
                using var input = await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false);
                var buffer = new byte[4096];
                long total = 0;
                while (!_stop.IsCancellationRequested)
                {
                    idle.CancelAfter(TimeSpan.FromSeconds(20));
                    var beforeRead = _clock.Elapsed.TotalSeconds;
                    var count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
                    var afterRead = _clock.Elapsed.TotalSeconds;
                    if (count == 0)
                    {
                        Observations.Enqueue(new { request, time = afterRead, kind = "eof", total });
                        break;
                    }
                    await Task.WhenAll(contexts.Select(async context =>
                    {
                        await context.Response.OutputStream.WriteAsync(buffer.AsMemory(0, count), idle.Token).ConfigureAwait(false);
                        await context.Response.OutputStream.FlushAsync(idle.Token).ConfigureAwait(false);
                    })).ConfigureAwait(false);
                    total += count;
                    Observations.Enqueue(new { request, time = afterRead, kind = "read", count, total,
                        readSeconds = afterRead - beforeRead, writeSeconds = _clock.Elapsed.TotalSeconds - afterRead });
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or HttpListenerException or OperationCanceledException or ObjectDisposedException)
            {
                Observations.Enqueue(new { request, time = _clock.Elapsed.TotalSeconds, kind = "closed", cancelled = _stop.IsCancellationRequested,
                    error = error.GetType().Name, hresult = $"0x{error.HResult:X8}" });
            }
            finally
            {
                foreach (var context in contexts)
                {
                    try { context.Response.Close(); }
                    catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
                    catch (HttpListenerException) when (_stop.IsCancellationRequested) { }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _listener.Close();
            await _accepting.ConfigureAwait(false);
            await Task.WhenAll(_requests).ConfigureAwait(false);
            _client.Dispose();
            _stop.Dispose();
        }
    }
}
