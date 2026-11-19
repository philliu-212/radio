using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

// Opt-in, muted, isolated process. Never changes portable settings or forces GC
// during the natural playback/switch measurements.
internal static class RfiSwitchMemoryChecks
{
    internal static async Task RunAsync(string[] args)
    {
        var mode = PlayerBackend.Option(args, "--mode", "switch-live");
        var seconds = Math.Clamp(int.Parse(PlayerBackend.Option(args, "--seconds", "60")), 10, 600);
        var cycles = Math.Clamp(int.Parse(PlayerBackend.Option(args, "--cycles", "3")), 1, 50);
        var recoverySeconds = Math.Clamp(int.Parse(PlayerBackend.Option(args, "--recovery-seconds", (seconds * 5).ToString())), 10, 7200);
        var useUi = mode is "switch-ui-live" or "best-ui-only";
        var bestOnly = mode is "best-only" or "best-ui-only";
        var output = Path.GetFullPath(PlayerBackend.Option(args, "--output", $"artifacts/rfi-memory-{mode}.jsonl"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var writer = new StreamWriter(output) { AutoFlush = true };
        using var process = Process.GetCurrentProcess();
        var clock = Stopwatch.StartNew();
        var retired = new List<(int Cycle, string Kind, WeakReference Reference)>();
        var completions = new List<Task>();
        var completedReceivers = 0;
        var failures = 0;
        using var player = new SystemAudioPlayer(rfiFactory: () => new WindowsAudioPlayer
        {
            UseBufferedRfiSource = true,
            BufferedRfiFactory = mode == "switch-synthetic"
                ? (_, token) => RfiBufferedStream.OpenFixtureAsync(new RfiMemoryChecks.SilentStream(), token)
                : RfiBufferedStream.OpenAsync
        }, mediaFactory: () => new MediaEngineAudioPlayer(), nativeFactory: () => new WindowsAudioPlayer())
        { IsMuted = true };
        using var form = useUi ? new MainForm(player, persistPreferences: false,
            autoPlayOnStartup: false, checkUpdates: false,
            settings: new ThemeSettings { MyStationIds = ["best-989", "rfi-monde"], StationSetupCompleted = true }) : null;
        void Write(string kind, object data)
        {
            var line = JsonSerializer.Serialize(new { seconds = clock.Elapsed.TotalSeconds, kind, data });
            writer.WriteLine(line);
            Console.WriteLine(line);
        }
        player.StatusChanged += status =>
        {
            if (status.State == AudioState.Failed) failures++;
            Write("status", new { state = status.State.ToString(), status.Message });
        };
        void Snapshot(string phase)
        {
            process.Refresh();
            var heap = GC.GetGCMemoryInfo();
            Write("memory", new
            {
                phase, process.Id, privateBytes = process.PrivateMemorySize64,
                workingSetBytes = process.WorkingSet64, managedBytes = GC.GetTotalMemory(false),
                committed = heap.TotalCommittedBytes, heap = heap.HeapSizeBytes,
                fragmented = heap.FragmentedBytes, pinned = heap.PinnedObjectsCount,
                handles = process.HandleCount, threads = process.Threads.Count,
                gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                detail = player.Describe(),
                ui = form is null ? null : new { form.PlayingStationId, form.ReconnectPending,
                    form.PlaybackWatchRunning, form.MetadataRunning, state = form.LatestStatus.State.ToString() },
                retired = retired.Select(r => new { r.Cycle, r.Kind, alive = r.Reference.IsAlive }).ToArray(),
                completedReceivers,
                virtualMemory = NativeMemory(),
                modules = process.Modules.Cast<ProcessModule>().Select(m => m.ModuleName).Order().ToArray()
            });
        }
        async Task Observe(string phase, int duration)
        {
            var initial = player.Position;
            Snapshot(phase);
            for (var elapsed = 0; elapsed < duration; elapsed += 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, duration - elapsed)));
                Snapshot(phase);
            }
            if (player.Position <= initial) throw new Exception($"No playback clock advance in {phase}.");
        }
        async Task Play(string station)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (form is not null)
            {
                await form.PlayForProbeAsync(station).WaitAsync(timeout.Token);
                if (form.PlayingStationId != station || form.ReconnectPending)
                    throw new Exception($"MainForm did not start requested station {station}.");
                return;
            }
            var entry = StationCatalog.Load().Single(s => s.Id == station);
            var stream = await new OfficialStreamResolver().ResolveAsync(entry, timeout.Token);
            await player.PlayAsync(stream, timeout.Token);
        }
        Write("configuration", new { mode, seconds, cycles, recoverySeconds,
            useUi, settingsPersisted = false, updatesEnabled = false,
            muted = true, version = typeof(WindowsAudioPlayer).Assembly.GetName().Version?.ToString() });
        Snapshot("before");
        try
        {
            form?.Show();
            await Play("best-989");
            if (bestOnly)
                await Observe("best-only", seconds * (2 * cycles + 1) + recoverySeconds);
            else
            {
                await Observe("baseline-best", seconds);
                for (var cycle = 1; cycle <= cycles; cycle++)
                {
                    await Play("rfi-monde");
                    await Observe($"rfi-{cycle}", seconds);
                    TrackRetiring(player, cycle, retired, completions);
                    await Play("best-989");
                    await Task.WhenAll(completions).WaitAsync(TimeSpan.FromSeconds(10));
                    completedReceivers += completions.Count;
                    completions.Clear(); // Do not retain receiver tasks/state machines while probing reachability.
                    await Observe($"best-after-{cycle}", seconds);
                }
                await Observe("best-recovery", recoverySeconds);
            }
            Snapshot("natural-end");
            // Diagnostic only, AFTER every natural recovery sample.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(2000);
            Snapshot("diagnostic-gc-best-active");
        }
        finally
        {
            form?.ExitPlayer();
            player.Stop();
        }
        await Task.WhenAll(completions).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(2000);
        Snapshot("stopped");
        Write("complete", new { failures, limitations =
            "Isolated muted process. UI modes include MainForm metadata/recovery policy but do not inject failures or guarantee a recovery occurred. Weak refs prove managed reachability only, not native allocator release. End-only GC is diagnostic, not a product fix. No audible acceptance." });
        if (failures != 0) throw new Exception("Playback failure during switch memory diagnostic.");
    }

    // Committed address-space categories, not residency/heap allocation ownership.
    // Private bytes also include private copy-on-write image pages; do not subtract
    // these categories from Process.PrivateMemorySize64 as an exact accounting.
    private static object NativeMemory()
    {
        ulong privateCommitted = 0, imageCommitted = 0, mappedCommitted = 0;
        nuint address = 0;
        while (VirtualQuery(address, out var region, (nuint)Marshal.SizeOf<MemoryRegion>()) != 0)
        {
            if (region.State == 0x1000)
            {
                if (region.Type == 0x20000) privateCommitted += (ulong)region.RegionSize;
                else if (region.Type == 0x1000000) imageCommitted += (ulong)region.RegionSize;
                else if (region.Type == 0x40000) mappedCommitted += (ulong)region.RegionSize;
            }
            var next = region.BaseAddress + region.RegionSize;
            if (next <= address) break;
            address = next;
        }
        return new { privateCommitted, imageCommitted, mappedCommitted };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    {
        internal nuint BaseAddress, AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal nuint RegionSize;
        internal uint State, Protect, Type;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nuint VirtualQuery(nuint address, out MemoryRegion information, nuint length);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void TrackRetiring(SystemAudioPlayer system, int cycle,
        List<(int Cycle, string Kind, WeakReference Reference)> retired, List<Task> completions)
    {
        static object? Field(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        var adapter = Field(system, "_current")!;
        retired.Add((cycle, "adapter", new(adapter)));
        foreach (var name in new[] { "_player", "_source", "_bufferedRfi", "_rfiRequest" })
            if (Field(adapter, name) is { } value) retired.Add((cycle, name, new(value)));
        if (Field(adapter, "_bufferedRfi") is RfiMediaStreamSource source)
        {
            retired.Add((cycle, "mediaStreamSource", new(source.Source)));
            if (Field(source, "_buffer") is RfiBufferedStream buffer)
            {
                retired.Add((cycle, "buffer", new(buffer)));
                completions.Add(buffer.Completion);
                if (Field(buffer, "_http") is { } http) retired.Add((cycle, "http", new(http)));
            }
        }
    }
}
