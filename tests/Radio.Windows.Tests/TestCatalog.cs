using System.Diagnostics;
using Radio.Core;

namespace Radio.Windows.Tests;

// One owner per offline group. Profiles live in scripts/Test-Plan.ps1.
internal sealed record TestGroup(string Name, string Description, Func<Task> Run, bool StartupFocus = false, bool MessageLoop = true);

internal static class TestCatalog
{
    internal static readonly TestGroup[] Groups =
    [
        new("playback-policy", "Pure recovery and stability policies", () => Sync(() =>
        {
            PlaybackRecoveryChecks.Run();
            PlaybackStabilityChecks.Run();
        }), MessageLoop: false),
        new("diagnostics", "Offline playback diagnostics", () => Sync(PlaybackDiagnosticsChecks.Run), MessageLoop: false),
        new("runner-contract", "Command parsing and suite selection regression", () => Sync(TestCommandChecks.Run), MessageLoop: false),
        new("audio-buffer", "Bounded audio queue and MP3 parser", AudioBufferChecks.RunAsync, MessageLoop: false),
        new("allocations", "Audio queue/parser allocation regression", () => Sync(AudioBufferAllocationChecks.Run), MessageLoop: false),
        new("rfi-transport", "Loopback transport fixtures", RfiTransportChecks.RunOfflineAsync, MessageLoop: false),
        new("buffered-audio", "RFI buffer and native audio lifecycle fixtures", RfiBufferedChecks.RunAsync),
        new("hls-recovery", "HLS recovery with controlled time", HlsRecoveryChecks.RunAsync),
        new("backups", "Backup selection UI fixtures", async () =>
        {
            await UiChecks.CheckKissBackupAsync();
            await UiChecks.CheckGeneralBackupsAsync();
        }),
        new("stability-ui", "MainForm stability fixtures with controlled time", UiChecks.CheckPlaybackStabilityAsync),
        new("ui-shell", "Player shell, controls and tray lifecycle", UiChecks.RunShellAsync),
        new("ui-stations", "Station management, search and startup", UiChecks.RunStationsAsync),
        new("ui-appearance", "Tooltips, artwork and simulated DPI", UiChecks.RunAppearanceAsync),
        new("ui-focus", "Startup, pointer and window activation focus", UiChecks.RunFocusAsync, StartupFocus: true),
        new("single-instance", "Cross-process single-instance protocol", SingleInstanceChecks.RunAsync),
        new("system", "Local WAV native audio lifecycle", RunSystemAsync),
        new("updates", "Offline Lite/Full updater fixtures", UpdateChecks.RunAsync)
    ];

    internal static readonly string[] LegacyUi =
    [
        "ui-focus", "ui-appearance", "hls-recovery", "playback-policy",
        "ui-shell", "ui-stations", "backups", "stability-ui"
    ];

    internal static TestGroup[] Select(string names)
    {
        var selected = names.Split(',');
        if (selected.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Suite names must not be empty.");
        return selected.Distinct(StringComparer.Ordinal).Select(name =>
            Groups.SingleOrDefault(group => group.Name == name) ??
            throw new ArgumentException($"Unknown suite '{name}'. Use --list.")).ToArray();
    }

    internal static async Task RunAsync(IEnumerable<TestGroup> groups)
    {
        foreach (var group in groups)
        {
            var clock = Stopwatch.StartNew();
            Console.WriteLine($"GROUP START {group.Name}");
            try
            {
                await group.Run();
                Console.WriteLine($"GROUP PASS {group.Name} {clock.Elapsed.TotalSeconds:F3}s");
            }
            catch
            {
                Console.Error.WriteLine($"GROUP FAIL {group.Name} {clock.Elapsed.TotalSeconds:F3}s");
                throw;
            }
        }
    }

    private static Task Sync(Action run) { run(); return Task.CompletedTask; }

    private static async Task RunSystemAsync()
    {
        var path = Path.GetFullPath("artifacts/playback-test.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            const int bytes = 16000 * 2 * 30;
            writer.Write("RIFF"u8); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        var stream = new ResolvedStream(new Uri(path), new Uri("https://example.invalid/"), "audio/wav");
        await SystemPlayerChecks.RunAsync(stream);
        Console.WriteLine("All offline Windows system audio checks passed.");
    }
}
