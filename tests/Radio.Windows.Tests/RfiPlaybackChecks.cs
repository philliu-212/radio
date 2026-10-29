using System.Diagnostics;
using System.Text.Json;
using Radio.Core;

namespace Radio.Windows.Tests;

// Explicit online diagnostic, never part of --system/--ui. The connection is muted;
// no audio, credentials, signed URLs or portable preferences are written to the report.
internal static class RfiPlaybackChecks
{
    internal sealed record Sample(double Time, double Position, AudioState State);
    internal sealed record Stall(double Start, double Seconds);
    internal sealed record Summary(double? FirstClockAdvanceSeconds, double FirstMinuteStallSeconds,
        double LaterStallSeconds, double ClockAdvanceSeconds, double LongestAdvancingWhileBufferingSeconds,
        IReadOnlyList<Stall> Stalls);

    public static async Task RunAsync(string[] args)
    {
        var seconds = Math.Clamp(int.Parse(Option(args, "--seconds", "600")), 60, 1800);
        var output = Path.GetFullPath(Option(args, "--output", "artifacts/rfi-playback.json"));
        var station = StationCatalog.Load().Single(s => s.Id == "rfi-monde");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stream = await new OfficialStreamResolver().ResolveAsync(station, timeout.Token);
        using var systemPlayer = new SystemAudioPlayer();
        var players = new Dictionary<string, IAudioPlayer> { ["system"] = systemPlayer };
        var samples = players.Keys.ToDictionary(name => name, _ => new List<Sample>());
        var states = players.Keys.ToDictionary(name => name, _ => AudioState.Stopped);
        var events = new List<object>();
        var errors = new List<string>();
        var clock = Stopwatch.StartNew();
        foreach (var (name, player) in players)
        {
            player.IsMuted = true;
            player.StatusChanged += status =>
            {
                states[name] = status.State;
                events.Add(new { time = clock.Elapsed.TotalSeconds, name, state = status.State.ToString() });
                if (status.State == AudioState.Failed) errors.Add(name + ": " + status.Message);
            };
        }
        var diagnostics = new Dictionary<string, object>();
        try
        {
            // Sample from the request, not after PlayAsync, so startup is never omitted.
            var opening = Task.WhenAll(players.Select(async p =>
            {
                try { await p.Value.PlayAsync(stream, default); }
                catch (Exception error) { errors.Add(p.Key + ": opening " + error.GetType().Name); }
            }));
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(250);
                var time = clock.Elapsed.TotalSeconds;
                foreach (var (name, player) in players)
                    samples[name].Add(new Sample(time, player.Position.TotalSeconds, states[name]));
            }
            await opening;
            foreach (var (name, player) in players) diagnostics[name] = ((IPlayerDiagnostics)player).Describe();
        }
        finally { foreach (var player in players.Values) player.Stop(); }
        var summaries = samples.ToDictionary(p => p.Key, p => Analyze(p.Value));
        var passed = errors.Count == 0 && summaries.Values.All(s => s.FirstClockAdvanceSeconds is <= 40 &&
            s.LongestAdvancingWhileBufferingSeconds < 3);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, seconds, muted = true, streamHost = stream.Uri.Host,
            passed, errors, summaries, diagnostics, events, samples,
            limitations = "One live connection, no playback/buffering changes. 250ms clock samples, stalls >=0.5s after first advancing clock; first minute is relative to the first clock advance. Not an audible recording, controlled comparison, multi-hour, sleep or network recovery acceptance test. passed checks opening/state recovery, not a guaranteed stall reduction."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"RFI online diagnostic saved: {output}");
        foreach (var (name, summary) in summaries)
            Console.WriteLine($"{name}: startup={summary.FirstClockAdvanceSeconds:F2}s, first-minute stalls={summary.FirstMinuteStallSeconds:F2}s, later stalls={summary.LaterStallSeconds:F2}s");
        if (!passed) throw new Exception("RFI online playback opening/state recovery failed; inspect the report.");
    }

    internal static Summary Analyze(IReadOnlyList<Sample> samples)
    {
        var first = -1;
        var stalls = new List<Stall>();
        int? start = null;
        double stale = 0, longestStale = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            var advance = samples[i].Position - samples[i - 1].Position;
            if (first < 0)
            {
                if (advance > 0.05) first = i;
                continue;
            }
            if (advance < 0.05) start ??= i - 1;
            else if (start is { } stopped)
            {
                AddStall(stopped, i - 1);
                start = null;
            }
            stale = samples[i - 1].State == AudioState.Buffering && advance > 0.05 ? stale + advance : 0;
            longestStale = Math.Max(longestStale, stale);
        }
        if (start is { } tail) AddStall(tail, samples.Count - 1);
        if (first < 0) return new Summary(null, 0, 0, 0, longestStale, stalls);
        var firstTime = samples[first].Time;
        var cutoff = firstTime + 60;
        var early = stalls.Sum(s => Math.Max(0, Math.Min(s.Start + s.Seconds, cutoff) - s.Start));
        return new Summary(firstTime, early, stalls.Sum(s => s.Seconds) - early,
            samples[^1].Position - samples[first].Position, longestStale, stalls);

        void AddStall(int from, int to)
        {
            var duration = samples[to].Time - samples[from].Time;
            if (duration >= 0.5) stalls.Add(new Stall(samples[from].Time, duration));
        }
    }

    private static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
