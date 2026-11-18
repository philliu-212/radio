using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Radio.Windows.Tests;

// Test-only, isolated real Edge process. Never attaches to the user's browser/profile.
internal sealed class EdgeCdpProbe : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentQueue<JsonElement> _mediaEvents = new();
    private readonly ConcurrentQueue<object> _networkEvents = new();
    private readonly HashSet<string> _mediaRequests = [];
    private readonly Process _process;
    private Task? _receiving;
    private int _id;
    internal string Version { get; private set; } = "";
    internal string ProfileDirectory { get; }
    internal JsonElement[] MediaEvents => _mediaEvents.ToArray();
    internal object[] NetworkEvents => _networkEvents.ToArray();

    private EdgeCdpProbe(Process process, string profile) { _process = process; ProfileDirectory = profile; }

    internal static async Task<EdgeCdpProbe> OpenAsync(bool headless = false)
    {
        var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe");
        if (!File.Exists(edge)) throw new FileNotFoundException("Installed Microsoft Edge required.", edge);
        var picker = new TcpListener(IPAddress.Loopback, 0);
        picker.Start();
        var port = ((IPEndPoint)picker.LocalEndpoint).Port;
        picker.Stop();
        var profile = Path.Combine(Path.GetTempPath(), "opencode", "radio43-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(edge) { UseShellExecute = false };
        foreach (var argument in new[] { "--no-first-run", "--no-default-browser-check", "--new-window",
            "--disable-background-timer-throttling", "--disable-renderer-backgrounding",
            "--disable-backgrounding-occluded-windows", "--disable-background-media-suspend",
            "--autoplay-policy=no-user-gesture-required", "--remote-debugging-address=127.0.0.1",
            "--remote-debugging-port=" + port, "--user-data-dir=" + profile, "about:blank" })
            start.ArgumentList.Add(argument);
        if (headless) start.ArgumentList.Add("--headless=new");
        var probe = new EdgeCdpProbe(Process.Start(start)!, profile);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            JsonElement pages;
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    pages = JsonSerializer.Deserialize<JsonElement>(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", deadline.Token));
                    if (pages.EnumerateArray().Any(p => p.GetProperty("type").GetString() == "page")) break;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(100, deadline.Token); // Local browser service startup, not test-result polling.
            }
            var page = pages.EnumerateArray().First(p => p.GetProperty("type").GetString() == "page");
            await probe._socket.ConnectAsync(new Uri(page.GetProperty("webSocketDebuggerUrl").GetString()!), deadline.Token);
            probe._receiving = probe.ReceiveAsync();
            var version = await probe.CallAsync("Browser.getVersion", new { });
            probe.Version = version.GetProperty("product").GetString()!;
            await probe.CallAsync("Page.enable", new { });
            var window = await probe.CallAsync("Browser.getWindowForTarget", new { });
            await probe.CallAsync("Browser.setWindowBounds", new { windowId = window.GetProperty("windowId").GetInt32(), bounds = new { windowState = "normal" } });
            await probe.CallAsync("Page.bringToFront", new { });
            EnumWindows((windowHandle, _) =>
            {
                GetWindowThreadProcessId(windowHandle, out var processId);
                if (processId == probe._process.Id)
                {
                    var name = new StringBuilder(128);
                    GetClassName(windowHandle, name, name.Capacity);
                    if (name.ToString() == "Chrome_WidgetWin_1")
                    {
                        ShowWindow(windowHandle, 5);
                        SetForegroundWindow(windowHandle);
                    }
                }
                return true;
            }, IntPtr.Zero);
            await probe.CallAsync("Media.enable", new { });
            await probe.CallAsync("Runtime.enable", new { });
            await probe.CallAsync("Network.enable", new { });
            await probe.CallAsync("Page.addScriptToEvaluateOnNewDocument", new { source = Instrumentation });
            return probe;
        }
        catch { await probe.DisposeAsync(); throw; }
    }

    internal async Task<JsonElement> EvaluateAsync(string expression) =>
        await CallAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true, userGesture = true });

    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    internal async Task NavigateAsync(string url) => await CallAsync("Page.navigate", new { url });

    internal async Task<JsonElement> CallAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _id);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            var message = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await _socket.SendAsync(message, WebSocketMessageType.Text, true, _stop.Token);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var response = await completion.Task.WaitAsync(timeout.Token);
            if (response.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
            return response.GetProperty("result").Clone();
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReceiveAsync()
    {
        var buffer = new byte[16384];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token);
                    if (part.MessageType == WebSocketMessageType.Close) return;
                    data.Write(buffer, 0, part.Count);
                    if (data.Length > 16 * 1024 * 1024) throw new IOException("CDP message too large.");
                } while (!part.EndOfMessage);
                using var document = JsonDocument.Parse(data.ToArray());
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && _pending.TryGetValue(id.GetInt32(), out var completion))
                    completion.TrySetResult(root.Clone());
                else if (root.TryGetProperty("method", out var method))
                {
                    var name = method.GetString()!;
                    if ((name.StartsWith("Media.", StringComparison.Ordinal) || name == "Runtime.exceptionThrown")
                        && _mediaEvents.Count < 20000) _mediaEvents.Enqueue(root.Clone());
                    else if (name.StartsWith("Network.", StringComparison.Ordinal)) RecordNetwork(name, root.GetProperty("params"));
                }
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException)
        {
            foreach (var completion in _pending.Values) completion.TrySetException(error);
        }
    }

    private void RecordNetwork(string method, JsonElement data)
    {
        if (!data.TryGetProperty("requestId", out var idValue)) return;
        var id = idValue.GetString()!;
        if (method == "Network.requestWillBeSent" && data.TryGetProperty("type", out var type) && type.GetString() == "Media")
        {
            var url = data.GetProperty("request").GetProperty("url").GetString()!;
            var uri = new Uri(url);
            if (uri.AbsolutePath.EndsWith("/rfimonde-64.mp3", StringComparison.Ordinal) || uri.Host == "127.0.0.1")
            {
                _mediaRequests.Add(id);
                _networkEvents.Enqueue(new { method, requestId = id, timestamp = data.GetProperty("timestamp").GetDouble(),
                    wallTime = data.GetProperty("wallTime").GetDouble(), url });
            }
        }
        if (!_mediaRequests.Contains(id) || _networkEvents.Count >= 50000) return;
        if (method == "Network.dataReceived")
            _networkEvents.Enqueue(new { method, requestId = id, timestamp = data.GetProperty("timestamp").GetDouble(),
                bytes = data.GetProperty("dataLength").GetInt32(), encodedBytes = data.GetProperty("encodedDataLength").GetInt32() });
        else if (method == "Network.responseReceived")
        {
            var response = data.GetProperty("response");
            _networkEvents.Enqueue(new { method, requestId = id, timestamp = data.GetProperty("timestamp").GetDouble(),
                status = response.GetProperty("status").GetInt32(), protocol = response.GetProperty("protocol").GetString(),
                mimeType = response.GetProperty("mimeType").GetString(),
                remoteAddress = response.TryGetProperty("remoteIPAddress", out var ip) ? ip.GetString() : null });
        }
        else if (method == "Network.loadingFailed")
            _networkEvents.Enqueue(new { method, requestId = id, timestamp = data.GetProperty("timestamp").GetDouble(),
                error = data.GetProperty("errorText").GetString() });
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _socket.Abort();
        if (_receiving is not null) await _receiving;
        _socket.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true); // Only this probe's dedicated browser process tree.
            await _process.WaitForExitAsync();
        }
        _process.Dispose();
        _stop.Dispose();
    }

    internal const string Instrumentation = """
        (() => {
          window.__rfiProbe = {origin:null, samples:[], events:[], audio:null};
          const p = window.__rfiProbe;
          const original = HTMLMediaElement.prototype.play;
          HTMLMediaElement.prototype.play = function(...args) {
            this.muted = true;
            p.audio = this;
            return original.apply(this,args);
          };
          for (const name of ['waiting','playing','stalled','ended','error','pause','loadedmetadata']) {
            document.addEventListener(name,e => {
              if (!(e.target instanceof HTMLMediaElement) || p.origin===null) return;
              const a=e.target;
              if (a!==p.audio) return;
              p.events.push({time:(performance.now()-p.origin)/1000, event:name,
                position:a.currentTime, readyState:a.readyState, networkState:a.networkState,
                buffered:Array.from({length:a.buffered.length},(_,i)=>[a.buffered.start(i),a.buffered.end(i)]),
                error:a.error?.code ?? null});
            },true);
          }
          setInterval(() => {
            if(p.origin===null) return;
            const a=p.audio;
            if(!a) return;
            p.samples.push({time:(performance.now()-p.origin)/1000, position:a.currentTime,
              paused:a.paused, ended:a.ended, readyState:a.readyState, networkState:a.networkState,
              visibility:document.visibilityState,
              buffered:Array.from({length:a.buffered.length},(_,i)=>[a.buffered.start(i),a.buffered.end(i)]),
              source:a.currentSrc});
          },250);
        })();
        """;
}
