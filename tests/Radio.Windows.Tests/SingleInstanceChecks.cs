using System.Diagnostics;
using System.IO.Pipes;

namespace Radio.Windows.Tests;

internal static class SingleInstanceChecks
{
    // A separate process is essential: Windows mutex ownership is recursive on
    // the same thread, so two objects in one UI-thread test are not two launches.
    internal static void RunHost(string scope, bool listen)
    {
        using var instance = new SingleInstance(scope);
        if (!instance.IsPrimary) throw new Exception("Test host could not acquire mutex.");
        using var form = new Form { Text = "Radio Mini single-instance fixture" };
        form.Shown += async (_, _) =>
        {
            Console.WriteLine("OWNED");
            await Task.Delay(700); // Intentionally expose the startup pipe race.
            if (listen) instance.Listen(form, () => Console.WriteLine("ACTIVATED"));
            _ = Task.Run(() => { Console.ReadLine(); form.BeginInvoke(form.Close); });
        };
        Application.Run(form);
    }

    internal static async Task RunAsync()
    {
        await CheckDirectoriesAsync();
        var scope = "RadioMini.Test." + Guid.NewGuid().ToString("N");
        using (var host = StartHost(scope))
        {
            using var cleanup = new HostCleanup(host);
            Check(await ReadLineAsync(host) == "OWNED", "First process owns the mutex before IPC initialization");
            using var secondary = new SingleInstance(scope);
            Check(!secondary.IsPrimary, "Second process cannot initialize another player");
            Check(await secondary.ActivateExistingAsync(), "Startup race retries until the pipe is ready");
            Check(await ReadLineAsync(host) == "ACTIVATED", "Activation runs on the original UI thread");

            // Disconnect and invalid commands must not poison subsequent launches.
            using (var invalid = new NamedPipeClientStream(".", scope + ".activation", PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await invalid.ConnectAsync(2000);
                await invalid.WriteAsync(new byte[] { 99 });
            }
            var launches = Enumerable.Range(0, 8).Select(_ => StartFixture("--instance-client", scope)).ToArray();
            try
            {
                await Task.WhenAll(launches.Select(process => process.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(15));
                Check(launches.All(process => process.ExitCode == 0), "Eight rapid launch processes all remain secondary and notify the original process");
                for (var i = 0; i < launches.Length; i++) Check(await ReadLineAsync(host) == "ACTIVATED", "Original window receives rapid activation");
            }
            finally
            {
                foreach (var process in launches) { new HostCleanup(process).Dispose(); process.Dispose(); }
            }
            await host.StandardInput.WriteLineAsync("exit");
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(host.ExitCode == 0, "Normal exit releases IPC and mutex");
        }
        using (var next = new SingleInstance(scope)) Check(next.IsPrimary, "New startup succeeds after normal exit");

        using (var host = StartHost(scope))
        {
            using var cleanup = new HostCleanup(host);
            Check(await ReadLineAsync(host) == "OWNED", "New host starts after normal exit");
            using var secondary = new SingleInstance(scope); // Keep the kernel mutex alive after the crash.
            Check(!secondary.IsPrimary, "Crash fixture has only one owner");
            host.Kill(); // Only this explicitly spawned fixture process, never a player.
            await host.WaitForExitAsync();
            using var recovered = new SingleInstance(scope);
            Check(recovered.IsPrimary, "Abandoned mutex is acquired after abnormal termination");
        }
        // An existing owner with no pipe must time out, not create a second UI.
        using (var host = StartHost(scope, listen: false))
        {
            try
            {
                Check(await ReadLineAsync(host) == "OWNED", "Timeout fixture owns the mutex");
                using var secondary = new SingleInstance(scope);
                var watch = Stopwatch.StartNew();
                Check(!await secondary.ActivateExistingAsync() && watch.Elapsed < TimeSpan.FromSeconds(10),
                    "Unavailable IPC exits with bounded retries and never starts another player");
            }
            finally { if (!host.HasExited) { host.Kill(); await host.WaitForExitAsync(); } }
        }
    }

    private static async Task CheckDirectoriesAsync()
    {
        // Synthetic unique directories: never acquire the real player's scope.
        var directory = Path.Combine(AppContext.BaseDirectory, "instance-" + Guid.NewGuid().ToString("N"));
        var scope = SingleInstance.CreateScope(directory);
        var equivalent = SingleInstance.CreateScope(directory.ToLowerInvariant() + Path.DirectorySeparatorChar);
        var other = SingleInstance.CreateScope(directory + "-other");
        Check(scope == equivalent, "Directory casing and trailing separator share a scope");
        Check(scope == SingleInstance.CreateScope(Path.Combine(directory, ".")), "Equivalent full paths share a scope");
        Check(scope != other, "Different executable directories have different scopes");
        Check(SingleInstance.Scope == SingleInstance.CreateScope(Path.GetDirectoryName(Environment.ProcessPath!)!),
            "Default scope uses the executable directory, not the working or managed package directory");
        using var first = StartHost(scope);
        using var firstCleanup = new HostCleanup(first);
        using var second = StartHost(other);
        using var secondCleanup = new HostCleanup(second);
        Check(await ReadLineAsync(first) == "OWNED" && await ReadLineAsync(second) == "OWNED",
            "Different directories can own independent players concurrently");
        using var firstClient = new SingleInstance(equivalent);
        using var secondClient = new SingleInstance(other);
        Check(!firstClient.IsPrimary && !secondClient.IsPrimary, "Each directory rejects duplicate players");
        Check(await firstClient.ActivateExistingAsync(), "Equivalent directory activates its original player");
        Check(await ReadLineAsync(first) == "ACTIVATED", "First directory receives its activation");
        Check(await secondClient.ActivateExistingAsync(), "Other directory activates independently");
        Check(await ReadLineAsync(second) == "ACTIVATED", "Other directory receives its own activation");
        await first.StandardInput.WriteLineAsync("exit");
        await second.StandardInput.WriteLineAsync("exit");
        await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        Check(first.ExitCode == 0 && second.ExitCode == 0, "Both directory hosts exit cleanly");
    }

    private static Process StartHost(string scope, bool listen = true)
        => StartFixture("--instance-host", scope, listen);

    private static Process StartFixture(string mode, string scope, bool listen = true)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(mode); start.ArgumentList.Add(scope);
        if (!listen) start.ArgumentList.Add("--no-listen");
        return Process.Start(start) ?? throw new Exception("Cannot start instance fixture.");
    }

    private static async Task<string?> ReadLineAsync(Process host) =>
        await host.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));

    private sealed class HostCleanup(Process host) : IDisposable
    {
        public void Dispose()
        {
            if (host.HasExited) return;
            host.Kill();
            host.WaitForExit(5000);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
