using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Radio.Core;

namespace Radio.Windows;

// An isolated, audio-only child process. No signed URLs on the command line or in logs.
internal sealed class MpvAudioPlayer(string executable) : IAudioPlayer, IPlayerDiagnostics
{
    private readonly SynchronizationContext _context = SynchronizationContext.Current
        ?? new WindowsFormsSynchronizationContext();
    private Session? _session;
    private double _volume = 0.4;
    private bool _muted;
    private bool _disposed;

    public event Action<PlaybackStatus>? StatusChanged;
    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); UpdateOption("volume", _volume * 100); }
    }
    public bool IsMuted
    {
        get => _muted;
        set { _muted = value; UpdateOption("mute", value); }
    }
    public TimeSpan Position => TimeSpan.FromSeconds(_session?.Position ?? 0);
    public IReadOnlyList<int> ChildProcessIds => _session is { } session ? [session.ProcessId] : [];
    public string DisplayName => "mpv 純音訊";
    public object Describe() => new
    {
        backend = "mpv", version = _session?.Version, videoSelection = _session?.VideoSelection,
        audioCodec = _session?.AudioCodec, compressedCacheLimitMiB = 4
    };

    public async Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        Stop();
        var session = new Session(executable, _volume, _muted, OnSessionStatus);
        _session = session;
        Publish(AudioState.Opening, "mpv 正在連接直播…");
        try
        {
            await session.ConnectAsync(cancellationToken);
            await session.CommandAsync(cancellationToken, "set_property", "volume", _volume * 100);
            await session.CommandAsync(cancellationToken, "set_property", "mute", _muted);
            await session.CommandAsync(cancellationToken, "set_property", "referrer", stream.Referrer.AbsoluteUri);
            await session.CommandAsync(cancellationToken, "observe_property", 1, "time-pos");
            await session.CommandAsync(cancellationToken, "observe_property", 2, "paused-for-cache");
            await session.CommandAsync(cancellationToken, "observe_property", 3, "vid");
            await session.CommandAsync(cancellationToken, "observe_property", 4, "audio-codec-name");
            var version = await session.CommandAsync(cancellationToken, "get_property", "mpv-version");
            session.Version = version.TryGetProperty("data", out var data) ? data.GetString() : null;
            await session.CommandAsync(cancellationToken, "loadfile", stream.Uri.AbsoluteUri, "replace");
        }
        catch
        {
            if (_session == session) Stop();
            else session.Dispose();
            throw;
        }
    }

    internal async Task<JsonElement> ReadPropertyAsync(string name)
    {
        var session = _session ?? throw new InvalidOperationException("mpv 未播放。");
        var result = await session.CommandAsync(CancellationToken.None, "get_property", name);
        return result.GetProperty("data").Clone();
    }

    private async void UpdateOption(string name, object value)
    {
        var session = _session;
        if (session is null || !session.Connected) return;
        try { await session.CommandAsync(CancellationToken.None, "set_property", name, value); }
        catch (OperationCanceledException) { }
        catch (Exception) { OnSessionStatus(session, new(AudioState.Failed, "mpv 控制連線失敗，請重新播放。")); }
    }

    private void OnSessionStatus(Session session, PlaybackStatus status) => _context.Post(_ =>
    {
        if (_session != session || _disposed) return;
        if (status.State == AudioState.Failed) Stop();
        StatusChanged?.Invoke(status);
    }, null);

    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
        Publish(AudioState.Stopped, "已停止；重新播放會回到直播。");
    }

    private void Publish(AudioState state, string message) => StatusChanged?.Invoke(new(state, message));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly NamedPipeClientStream _pipe;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SemaphoreSlim _writes = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly Action<Session, PlaybackStatus> _onStatus;
        private StreamWriter? _writer;
        private Task? _readerTask;
        private long _nextRequest;
        private int _disposed;
        private double _position;
        private bool _started;
        private string? _videoSelection;
        private string? _audioCodec;

        public Session(string executable, double volume, bool muted, Action<Session, PlaybackStatus> onStatus)
        {
            _onStatus = onStatus;
            var pipeName = $"radio-{Guid.NewGuid():N}";
            _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[]
            {
                "--idle=yes", "--no-config", "--load-scripts=no", "--ytdl=no", "--osc=no",
                "--no-terminal", "--msg-level=all=no", "--no-video", "--audio-display=no", "--force-window=no",
                "--input-default-bindings=no", "--input-vo-keyboard=no", "--load-unsafe-playlists=no",
                "--cache=yes", "--demuxer-max-bytes=4MiB", "--demuxer-max-back-bytes=0",
                "--demuxer-readahead-secs=5", "--cache-pause=yes", "--cache-pause-initial=yes", "--cache-pause-wait=2",
                $"--volume={(volume * 100).ToString(CultureInfo.InvariantCulture)}", $"--mute={(muted ? "yes" : "no")}",
                $"--input-ipc-server=\\\\.\\pipe\\{pipeName}"
            }) start.ArgumentList.Add(arg);
            try { _process = Process.Start(start) ?? throw new InvalidOperationException("無法啟動 mpv。"); }
            catch { _pipe.Dispose(); _lifetime.Dispose(); _writes.Dispose(); throw; }
        }

        public int ProcessId => _process.Id;
        public bool Connected => _writer is not null && Volatile.Read(ref _disposed) == 0;
        public double Position => Volatile.Read(ref _position);
        public string? VideoSelection => Volatile.Read(ref _videoSelection);
        public string? AudioCodec => Volatile.Read(ref _audioCodec);
        public string? Version { get; set; }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _pipe.ConnectAsync(8000, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            _writer = new StreamWriter(_pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            _readerTask = ReadEventsAsync();
        }

        public async Task<JsonElement> CommandAsync(CancellationToken cancellationToken, params object[] command)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            var id = Interlocked.Increment(ref _nextRequest);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;
            try
            {
                var payload = JsonSerializer.Serialize(new { command, request_id = id });
                await _writes.WaitAsync(linked.Token);
                try { await _writer!.WriteLineAsync(payload.AsMemory(), linked.Token); }
                finally { _writes.Release(); }
                var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), linked.Token);
                if (!result.TryGetProperty("error", out var error) || error.GetString() != "success")
                    throw new InvalidOperationException("mpv 拒絕控制命令。");
                return result;
            }
            finally { _pending.TryRemove(id, out _); }
        }

        private async Task ReadEventsAsync()
        {
            try
            {
                using var reader = new StreamReader(_pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                while (await reader.ReadLineAsync(_lifetime.Token) is { } line)
                {
                    if (line.Length > 256 * 1024) throw new InvalidDataException("IPC response too large.");
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("request_id", out var id) &&
                        _pending.TryGetValue(id.GetInt64(), out var completion))
                        completion.TrySetResult(root.Clone());
                    if (root.TryGetProperty("event", out var eventName)) HandleEvent(eventName.GetString(), root);
                }
                if (!_lifetime.IsCancellationRequested) Fail("mpv 程序已結束，請重新播放。");
            }
            catch (Exception) when (_lifetime.IsCancellationRequested) { }
            catch (Exception) { Fail("mpv 控制連線失敗，請重新播放。"); }
            finally
            {
                foreach (var pending in _pending.Values) pending.TrySetCanceled();
            }
        }

        private void HandleEvent(string? name, JsonElement root)
        {
            if (name == "playback-restart")
            {
                _started = true;
                _onStatus(this, new(AudioState.Playing, "mpv 直播播放中"));
            }
            else if (name == "end-file")
            {
                Fail("直播已結束或來源無法播放，請重新播放或使用官方收聽頁。");
            }
            else if (name == "property-change" && root.TryGetProperty("data", out var data))
            {
                switch (root.GetProperty("name").GetString())
                {
                    case "time-pos" when data.TryGetDoubleSafe(out var position):
                        Volatile.Write(ref _position, position);
                        break;
                    case "paused-for-cache" when data.ValueKind == JsonValueKind.True:
                        _onStatus(this, new(AudioState.Buffering, "mpv 直播緩衝中…"));
                        break;
                    case "paused-for-cache" when data.ValueKind == JsonValueKind.False && _started:
                        _onStatus(this, new(AudioState.Playing, "mpv 直播播放中"));
                        break;
                    case "vid":
                        Volatile.Write(ref _videoSelection, data.ToString());
                        break;
                    case "audio-codec-name":
                        Volatile.Write(ref _audioCodec, data.ValueKind == JsonValueKind.String ? data.GetString() : null);
                        break;
                }
            }
        }

        private void Fail(string message) => _onStatus(this, new(AudioState.Failed, message));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime.Cancel();
            _pipe.Dispose();
            foreach (var pending in _pending.Values) pending.TrySetCanceled();
            try
            {
                if (!_process.HasExited) _process.Kill();
                _process.WaitForExit(2000);
            }
            finally { _process.Dispose(); }
            // Allow in-flight async commands/readers to observe cancellation before disposing shared state.
            // The small CTS/semaphore/writer objects are collected with this stopped session.
        }
    }
}

internal static class JsonElementExtensions
{
    public static bool TryGetDoubleSafe(this JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value) && double.IsFinite(value);
    }
}
