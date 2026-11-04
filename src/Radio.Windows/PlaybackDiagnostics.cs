using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Radio.Core;

namespace Radio.Windows;

// Bounded, best-effort local diagnostics. No upload or metadata polling.
internal sealed class PlaybackDiagnostics : IDisposable
{
    internal const int MaxFileBytes = 256 * 1024;
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _maxBytes;
    private readonly Func<DateTimeOffset> _clock;
    private readonly System.Threading.Timer? _timer;
    private readonly string _session = Guid.NewGuid().ToString("N")[..8];
    private string? _lastStatus;
    private DateTimeOffset _window;
    private int _written, _suppressed;
    private bool _disposed;

    internal PlaybackDiagnostics(string directory, bool sampleAutomatically = true,
        int maxBytes = MaxFileBytes, Func<DateTimeOffset>? clock = null)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Write("session-start", new { version = MainForm.AppVersion });
        Sample();
        if (sampleAutomatically)
            _timer = new System.Threading.Timer(_ => Sample(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    internal void Request(string stationId)
    {
        lock (_gate)
        {
            _lastStatus = null;
            Write("play-request", new { station = Clip(stationId) });
        }
    }

    internal void Resolved(string stationId, ResolvedStream stream) => Write("stream-resolved", new
    {
        station = Clip(stationId), url = stream.Referrer.Host.Equals("www.twitch.tv", StringComparison.OrdinalIgnoreCase)
            ? stream.Uri.GetLeftPart(UriPartial.Authority) + "/[redacted-twitch-stream]" : Clip(stream.Uri.OriginalString),
        referrer = Clip(stream.Referrer.OriginalString), mediaType = Clip(stream.MediaType), isHls = stream.IsHls
    });

    internal void Status(PlaybackStatus status, string? stationId)
    {
        lock (_gate)
        {
            var message = Clip(status.Message ?? "");
            var codes = Regex.Matches(message, @"0x[0-9a-fA-F]{8}|(?<=代碼 )[0-9]+")
                .Cast<Match>().Take(4).Select(m => m.Value).ToArray();
            var station = Clip(stationId ?? "");
            var key = $"{station}:{status.State}:{message}";
            if (_lastStatus == key) return;
            _lastStatus = key;
            Write("playback", new { station, state = status.State.ToString(), message, codes });
            if (status.State == AudioState.Failed) Sample();
        }
    }

    internal void Error(Exception error) => Write("exception", new
    {
        type = error.GetType().Name, hresult = $"0x{error.HResult:X8}", details = Clip(error.ToString())
    });

    internal void Sample()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                using var process = Process.GetCurrentProcess();
                Write("memory", new { workingSetBytes = process.WorkingSet64,
                    privateBytes = process.PrivateMemorySize64, managedBytes = GC.GetTotalMemory(false),
                    handles = process.HandleCount, threads = process.Threads.Count,
                    gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2) });
            }
            catch (Exception) { /* Diagnostics must not interrupt playback. */ }
        }
    }

    // Bound even pathological messages/URLs; normal signed URLs remain verbatim.
    internal static string Clip(string value) => value.Length <= 8192 ? value : value[..8192] + "[truncated]";

    private void Write(string kind, object data)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                var now = _clock();
                if (now - _window >= TimeSpan.FromMinutes(1)) { _window = now; _written = 0; }
                if (_written >= 30) { _suppressed = Math.Min(_suppressed, int.MaxValue - 1) + 1; return; }
                _written++;
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { time = now, session = _session,
                    kind, suppressed = _suppressed, data }) + "\n");
                if (bytes.Length > _maxBytes) return;
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "playback.log");
                if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > _maxBytes)
                {
                    File.Delete(path + ".3");
                    for (var i = 2; i >= 1; i--)
                        if (File.Exists(path + $".{i}")) File.Move(path + $".{i}", path + $".{i + 1}", true);
                    File.Move(path, path + ".1", true);
                }
                using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                file.Write(bytes);
                _suppressed = 0;
            }
            catch (Exception) { /* Read-only/full/locked disks are non-fatal. */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Dispose();
            Sample();
            Write("session-end", new { });
            _disposed = true;
        }
    }
}
