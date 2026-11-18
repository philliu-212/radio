namespace Radio.Windows.Tests;

internal sealed record TestCommand(string Name, Func<Task> Run, bool StartupFocus = false, bool MessageLoop = true, bool Screenshots = false)
{
    private sealed record Legacy(Func<string[], Task> Run, string Values = "", string Flags = "", string Required = "");

    private static Task Sync(Action run) { run(); return Task.CompletedTask; }
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["--system"] = "system",
        ["--ui"] = string.Join(',', TestCatalog.LegacyUi),
        ["--updates"] = "updates",
        ["--diagnostics"] = "diagnostics",
        ["--audio-buffer-allocations"] = "allocations",
        ["--audio-buffer-offline"] = "audio-buffer",
        ["--rfi-buffered-offline"] = "buffered-audio",
        ["--rfi-transport-offline"] = "rfi-transport",
        ["--hls-recovery"] = "hls-recovery",
        ["--backups"] = "backups",
        ["--kiss-backup"] = "backups",
        ["--stability"] = "playback-policy,stability-ui"
    };

    private static readonly Dictionary<string, Legacy> LegacyCommands = new(StringComparer.Ordinal)
    {
        ["--focus"] = new(_ => UiChecks.CheckPointerFocusAsync()),
        ["--dpi-ui"] = new(_ => UiChecks.CheckStationManagerDpiAsync()),
        ["--icons"] = new(args => Sync(() => IconChecks.Run(
            args.Contains("--exe") ? Path.GetFullPath(args[Array.IndexOf(args, "--exe") + 1]) : null)), "--exe"),
        ["--single-instance"] = new(async _ =>
        {
            await SingleInstanceChecks.RunAsync();
            await UiChecks.CheckWindowActivationAsync();
        }),
        ["--kiss-live"] = new(_ => KissPlaybackChecks.RunAsync()),
        ["--backups-live"] = new(_ => BackupPlaybackChecks.RunAsync()),
        ["--stability-live"] = new(_ => StabilityLiveChecks.RunAsync()),
        ["--reconnect-live"] = new(_ => ReconnectLiveChecks.RunAsync()),
        ["--esradio"] = new(_ => EsRadioPlaybackChecks.RunAsync()),
        ["--hls-recovery-live"] = new(async _ =>
        {
            await HlsRecoveryChecks.RunAsync();
            await HlsRecoveryChecks.RunLiveAsync();
        }),
        ["--rfi"] = new(RfiPlaybackChecks.RunAsync, "--seconds --output"),
        ["--rfi-buffered-ui-live"] = new(RfiBufferedChecks.RunLiveUiAsync, "--seconds --output"),
        ["--rfi-transport"] = new(RfiTransportChecks.RunAsync, "--seconds --output", "--same-data --frames --buffered"),
        ["--rfi-edge"] = new(RfiEdgeChecks.RunAsync, "--seconds --output", "--edge-same-data --edge-headless --plain-audio"),
        ["--ota-stage"] = new(OtaPackageChecks.StageAsync, "--root --zip --report", Required: "--root --zip --report"),
        // These private child-process commands are handled before WinForms initialization.
        ["--instance-client"] = new(_ => Task.CompletedTask),
        ["--instance-host"] = new(_ => Task.CompletedTask, Flags: "--no-listen")
    };

    internal static IEnumerable<string> Names => Aliases.Keys.Concat(LegacyCommands.Keys);

    internal static TestCommand Parse(string[] args)
    {
        if (args.Length == 0) return Suites("--system", "system");
        var name = args[0];
        if (name is "--list" or "--help")
        {
            Require(args.Length == 1, "List/help cannot be combined with tests.");
            return new(name, () => Task.CompletedTask);
        }
        if (name == "--suite")
        {
            Require(args.Length == 2 || (args.Length == 3 && args[2] == "--screenshots"), "Use --suite name[,name] [--screenshots].");
            return Suites(name, args[1]) with { Screenshots = args.Length == 3 };
        }
        if (Aliases.TryGetValue(name, out var suites))
        {
            Require(args.Length == 1 || (args.Length == 2 && args[1] == "--screenshots"), "Choose one legacy command, or use --suite name[,name].");
            return Suites(name, suites) with { Screenshots = args.Length == 2 };
        }
        Require(LegacyCommands.ContainsKey(name), $"Unknown command '{name}'. Use --list.");
        var legacy = LegacyCommands[name];
        var start = 1;
        if (name is "--instance-client" or "--instance-host")
        {
            Require(args.Length >= 2 && !string.IsNullOrWhiteSpace(args[1]) && !args[1].StartsWith("--"), "Instance command requires a pipe name.");
            start = 2;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = start; i < args.Length; i++)
        {
            var option = args[i];
            Require(seen.Add(option), $"Duplicate option '{option}'.");
            if (legacy.Values.Split(' ').Contains(option))
            {
                Require(i + 1 < args.Length && !args[i + 1].StartsWith("--"), $"Missing value for '{option}'.");
                var value = args[++i];
                Require(!string.IsNullOrWhiteSpace(value), $"Empty value for '{option}'.");
                if (option == "--seconds")
                    Require(int.TryParse(value, out var seconds) && seconds > 0, "--seconds must be a positive integer.");
            }
            else Require(legacy.Flags.Split(' ').Contains(option), $"Unknown/conflicting option '{option}' for {name}.");
        }
        foreach (var required in legacy.Required.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            Require(seen.Contains(required), $"Missing required option '{required}'.");
        Require(!(seen.Contains("--frames") && seen.Contains("--buffered")), "Choose --frames or --buffered, not both.");
        return new(name, () => legacy.Run(args), name == "--focus");
    }

    private static TestCommand Suites(string name, string suites)
    {
        var selected = TestCatalog.Select(suites);
        return new(name, () => TestCatalog.RunAsync(selected), selected.Any(group => group.StartupFocus),
            selected.Any(group => group.MessageLoop));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
