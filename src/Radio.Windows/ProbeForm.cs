using System.Diagnostics;
using System.Text.Json;
using Radio.Core;

namespace Radio.Windows;

// A hidden WinForms message loop, using exactly the same playback adapter as the UI.
internal sealed class ProbeForm(string[] args) : Form
{
    public int ResultExitCode { get; private set; } = 1;
    private static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Hide();
        ShowInTaskbar = false;
        var path = Path.GetFullPath(Option(args, "--output", "artifacts/probe.json"));
        try
        {
            var seconds = Math.Clamp(int.Parse(Option(args, "--seconds", "15")), 5, 300);
            if (args.Contains("--ui-profile"))
            {
                await ProfileUiAsync(path, seconds);
                return;
            }
            using var player = PlayerBackend.Create(args);
            player.IsMuted = true;
            var resolver = new OfficialStreamResolver();
            var results = new List<object>();
            var failures = 0;
            var idle = await MeasureAsync(player, 5);
            foreach (var station in StationCatalog.Load())
            {
                var filter = Option(args, "--station", "");
                if (filter.Length > 0 && !filter.Split(',').Contains(station.Id, StringComparer.Ordinal)) continue;
                var events = new List<object>();
                var ready = new TaskCompletionSource<PlaybackStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
                string? failure = null;
                void OnStatus(PlaybackStatus status)
                {
                    lock (events) events.Add(new { time = DateTimeOffset.UtcNow, state = status.State.ToString(), status.Message });
                    if (status.State is AudioState.Playing or AudioState.Failed) ready.TrySetResult(status);
                    if (status.State == AudioState.Failed) Volatile.Write(ref failure, status.Message);
                }
                player.StatusChanged += OnStatus;
                var startup = Stopwatch.StartNew();
                object? metrics = null;
                double positionAdvance = 0;
                string? host = null;
                string? error = null;
                object? media = null;
                int[] childPids = [];
                var childrenStopped = true;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                    var stream = await resolver.ResolveAsync(station, timeout.Token);
                    host = stream.Uri.Host; // Never store signed query parameters.
                    await player.PlayAsync(stream, timeout.Token);
                    var state = await ready.Task.WaitAsync(timeout.Token);
                    if (state.State == AudioState.Failed) throw new InvalidOperationException(state.Message);
                    startup.Stop();
                    await Task.Delay(3000); // Warm-up before sampling steady playback.
                    var position = player.Position;
                    metrics = await MeasureAsync(player, seconds);
                    positionAdvance = (player.Position - position).TotalSeconds;
                    media = (player as IPlayerDiagnostics)?.Describe();
                    if (Volatile.Read(ref failure) is { } playbackError) error = playbackError;
                    else if (positionAdvance < seconds * 0.9) error = "Playback clock did not advance sufficiently.";
                }
                catch (Exception exception)
                {
                    error = exception switch
                    {
                        SourceResolutionException => exception.Message,
                        OperationCanceledException => "Source/playback opening timed out (40s).",
                        _ => $"Playback error: {exception.GetType().Name} (0x{exception.HResult:X8})."
                    };
                }
                finally
                {
                    childPids = (player as IPlayerDiagnostics)?.ChildProcessIds.ToArray() ?? [];
                    player.Stop();
                    player.StatusChanged -= OnStatus;
                    childrenStopped = childPids.All(id => !IsProcessAlive(id));
                    if (!childrenStopped) error = "A player child process remained alive after Stop.";
                }
                var stopped = await MeasureAsync(player, 3);
                if (error is not null) failures++;
                object[] snapshots;
                lock (events) snapshots = events.ToArray();
                results.Add(new
                {
                    station.Id, station.Name, host, passed = error is null, error,
                    startupSeconds = Math.Round(startup.Elapsed.TotalSeconds, 2),
                    playbackClockAdvanceSeconds = Math.Round(positionAdvance, 2), media, metrics, stopped,
                    childrenStopped, events = snapshots
                });
            }
            if (results.Count == 0) throw new ArgumentException("No matching stations selected.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow, os = Environment.OSVersion.VersionString,
                runtime = Environment.Version.ToString(), logicalProcessors = Environment.ProcessorCount,
                backend = PlayerBackend.Option(args, "--backend", "system"),
                muted = true, sampleSeconds = seconds, idle, results,
                limitations = "App + owned playback child process metrics; shared Windows audio services/GPU allocations excluded. No browser baseline, audibility, long-run, sleep or network recovery test."
            }, new JsonSerializerOptions { WriteIndented = true }));
            ResultExitCode = failures == 0 ? 0 : 2;
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"Probe failed: {error.GetType().Name} (0x{error.HResult:X8})");
            ResultExitCode = 1;
        }
        finally { Close(); }
    }

    private async Task ProfileUiAsync(string path, int seconds)
    {
        var player = PlayerBackend.Create(args);
        player.IsMuted = true;
        using var form = new MainForm(player, persistPreferences: false, darkOverride: true,
            settings: new Radio.Windows.UI.ThemeSettings { MyStationIds = StationCatalog.Load().Select(s => s.Id).ToList() });
        form.Show();
        var stationId = Option(args, "--station", "bcc-ilike");
        await form.PlayForProbeAsync(stationId);
        if (form.LatestStatus.State != AudioState.Playing) throw new InvalidOperationException("UI playback did not start.");
        await Task.Delay(3000);
        var phases = new List<object>();
        var passed = true;
        foreach (var phase in new[] { "visible", "minimized", "restored" })
        {
            if (phase == "minimized") form.WindowState = FormWindowState.Minimized;
            else if (phase == "restored") form.ShowPlayer();
            await Task.Delay(500);
            var updates = form.PresentationUpdates;
            var position = player.Position;
            var metrics = await MeasureAsync(player, seconds);
            var advance = (player.Position - position).TotalSeconds;
            var updateDelta = form.PresentationUpdates - updates;
            var phasePassed = advance >= seconds * .9 && form.LatestStatus.State == AudioState.Playing &&
                (phase != "minimized" || (form.IsBackgroundMode && !form.MetadataRunning && updateDelta == 0));
            passed &= phasePassed;
            phases.Add(new { phase, passed = phasePassed, metrics, clockAdvanceSeconds = Math.Round(advance, 2),
                presentationUpdates = updateDelta, backgroundMode = form.IsBackgroundMode, metadataRunning = form.MetadataRunning });
        }
        // Screenshot contains only genuine current official metadata, not mock program/DJ content.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        form.Refresh();
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            bitmap.Save(Path.ChangeExtension(path, ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        var metadataTitle = form.Controls.Find("MetadataTitle", true).Single().Text;
        var metadataPerson = form.Controls.Find("MetadataPerson", true).Single().Text;
        form.Close();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(), stationId,
            muted = true, sampleSeconds = seconds, passed, phases, metadataTitle, metadataPerson,
            limitations = "Short sequential actual-package UI samples; shared audio services/GPU excluded. No audible/long-run or real multi-monitor DPI acceptance test."
        }, new JsonSerializerOptions { WriteIndented = true }));
        ResultExitCode = passed ? 0 : 2;
    }

    private static bool IsProcessAlive(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task<object> MeasureAsync(IAudioPlayer player, int seconds)
    {
        var ids = new[] { Environment.ProcessId }.Concat(
            (player as IPlayerDiagnostics)?.ChildProcessIds ?? []).Distinct().ToArray();
        var processes = new List<Process>();
        try
        {
            foreach (var id in ids)
                try { processes.Add(Process.GetProcessById(id)); }
                catch (ArgumentException) { }
            var startCpu = processes.ToDictionary(p => p.Id, p => p.TotalProcessorTime);
            var lastCpu = new Dictionary<int, TimeSpan>(startCpu);
            var clock = Stopwatch.StartNew();
            long privateSum = 0, workingSum = 0, privateMax = 0, parentPrivateSum = 0, childPrivateSum = 0;
            for (var i = 0; i < seconds; i++)
            {
                await Task.Delay(1000);
                long samplePrivate = 0, sampleWorking = 0;
                foreach (var process in processes)
                {
                    process.Refresh();
                    try { lastCpu[process.Id] = process.TotalProcessorTime; }
                    catch (InvalidOperationException) { }
                    if (process.HasExited) continue;
                    var memory = process.PrivateMemorySize64;
                    samplePrivate += memory;
                    sampleWorking += process.WorkingSet64;
                    if (process.Id == Environment.ProcessId) parentPrivateSum += memory;
                    else childPrivateSum += memory;
                }
                privateSum += samplePrivate;
                workingSum += sampleWorking;
                privateMax = Math.Max(privateMax, samplePrivate);
            }
            var cpuSeconds = processes.Sum(p => (lastCpu[p.Id] - startCpu[p.Id]).TotalSeconds);
            var cpuCorePercent = cpuSeconds / clock.Elapsed.TotalSeconds * 100;
            return new
            {
                measuredProcesses = processes.Count,
                wallSeconds = Math.Round(clock.Elapsed.TotalSeconds, 2),
                privateMiB = Math.Round(privateSum / seconds / 1048576d, 2),
                parentPrivateMiB = Math.Round(parentPrivateSum / seconds / 1048576d, 2),
                childPrivateMiB = Math.Round(childPrivateSum / seconds / 1048576d, 2),
                peakSamplePrivateMiB = Math.Round(privateMax / 1048576d, 2),
                workingSetMiB = Math.Round(workingSum / seconds / 1048576d, 2),
                cpuMachinePercent = Math.Round(cpuCorePercent / Environment.ProcessorCount, 3),
                cpuOneCorePercent = Math.Round(cpuCorePercent, 3)
            };
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
