namespace Radio.Windows.Tests;

internal static class TestCommandChecks
{
    internal static void Run()
    {
        var thread = Environment.CurrentManagedThreadId;
        StaTestContext.Run(async () =>
        {
            await Task.Yield();
            if (Environment.CurrentManagedThreadId != thread || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new Exception("Non-UI awaits must retain the entry STA thread.");
        });
        var failurePreserved = false;
        try { StaTestContext.Run(() => Task.FromException(new InvalidOperationException("context fixture"))); }
        catch (InvalidOperationException error) when (error.Message == "context fixture") { failurePreserved = true; }
        if (!failurePreserved) throw new Exception("STA context swallowed a test failure.");
        foreach (var args in new[]
        {
            new[] { "--typo" }, new[] { "--ui", "--system" },
            new[] { "--suite" }, new[] { "--suite", "unknown" },
            new[] { "--suite", "system," }, new[] { "--list", "--system" },
            new[] { "--rfi", "--seconds" }, new[] { "--rfi", "--seconds", "oops" },
            new[] { "--rfi", "--seconds", "0" }, new[] { "--rfi", "--frames" },
            new[] { "--rfi", "--output", "" },
            new[] { "--rfi-transport", "--frames", "--buffered" },
            new[] { "--rfi", "--output", "a", "--output", "b" },
            new[] { "--ota-stage", "--root", "x" }, new[] { "--instance-host" },
            new[] { "--instance-client", "pipe", "--no-listen" }
        })
        {
            try { TestCommand.Parse(args); }
            catch (ArgumentException) { continue; }
            throw new Exception("Invalid command accepted: " + string.Join(' ', args));
        }
        foreach (var args in new[]
        {
            Array.Empty<string>(), new[] { "--system" }, new[] { "--ui" },
            new[] { "--suite", "ui-focus,system,system" },
            new[] { "--rfi", "--seconds", "60", "--output", "a path/report.json" },
            new[] { "--ota-stage", "--root", "r", "--zip", "z", "--report", "p" },
            new[] { "--instance-client", "pipe" }, new[] { "--instance-host", "pipe", "--no-listen" }
        }) TestCommand.Parse(args);
        if (TestCatalog.Select("system,system,updates").Length != 2 ||
            TestCatalog.Groups.Select(group => group.Name).Distinct().Count() != TestCatalog.Groups.Length ||
            !TestCommand.Parse(["--ui"]).StartupFocus ||
            !TestCommand.Parse(["--suite", "ui-focus"]).StartupFocus ||
            TestCommand.Parse(["--suite", "playback-policy"]).StartupFocus ||
            TestCommand.Parse(["--suite", "playback-policy,audio-buffer"]).MessageLoop ||
            !TestCommand.Parse(["--suite", "playback-policy,ui-shell"]).MessageLoop)
            throw new Exception("Suite deduplication/focus contract failed.");
        Console.WriteLine("PASS: CLI rejects invalid/conflicting/missing options before side effects; suites deduplicate and retain startup-focus requirements.");
    }
}
