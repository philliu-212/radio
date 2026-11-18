namespace Radio.Windows.Tests;

// Clock-derived interruption episodes, not audible discontinuity detection.
internal static class PlaybackContinuity
{
    internal sealed record Segment(double Start, double Seconds, bool EndedByInterruption);
    internal sealed record Profile(bool Started, double ObservedPlaybackWindowSeconds, int Interruptions,
        double? ObservedInterruptionsPerHour, double? SecondsBeforeFirstInterruption,
        double LongestUninterruptedSeconds, double? MedianCompletedUninterruptedSeconds,
        double CurrentUninterruptedSeconds, IReadOnlyList<Segment> UninterruptedSegments,
        double MinimumClockStallSeconds = 0.5);
    internal sealed record Event(double Time, string Kind);
    internal sealed record BufferingEpisode(double Start, double? End);
    internal sealed record EventProfile(int BufferingEpisodes, int CompletedUnderHalfSecond,
        int PausesAfterStart, int Errors, IReadOnlyList<BufferingEpisode> Episodes);

    internal static EventProfile AnalyzeEvents(IEnumerable<Event> events, string playing, string waiting,
        string pause, string error)
    {
        var started = false;
        double? start = null;
        var episodes = new List<BufferingEpisode>();
        int pauses = 0, errors = 0;
        foreach (var entry in events)
        {
            if (entry.Kind == playing)
            {
                if (start is { } from) episodes.Add(new(from, entry.Time));
                start = null;
                started = true;
            }
            else if (entry.Kind == waiting && started) start ??= entry.Time;
            else if (entry.Kind == pause && started) pauses++;
            else if (entry.Kind == error) errors++;
        }
        if (start is { } pending) episodes.Add(new(pending, null));
        return new(episodes.Count, episodes.Count(e => e.End is { } end && end - e.Start < .5), pauses, errors, episodes);
    }

    internal static Profile Analyze(IReadOnlyList<RfiPlaybackChecks.Sample> samples)
    {
        var summary = RfiPlaybackChecks.Analyze(samples);
        if (summary.FirstClockAdvanceSeconds is not { } first)
            return new(false, 0, 0, null, null, 0, null, 0, []);
        var end = samples[^1].Time;
        var segments = new List<Segment>();
        var cursor = first;
        foreach (var stall in summary.Stalls)
        {
            segments.Add(new(cursor, Math.Max(0, stall.Start - cursor), true));
            cursor = stall.Start + stall.Seconds;
        }
        var current = Math.Max(0, end - cursor);
        if (current > 0) segments.Add(new(cursor, current, false));
        var completed = segments.Where(s => s.EndedByInterruption).Select(s => s.Seconds).Order().ToArray();
        double? median = completed.Length == 0 ? null : completed.Length % 2 == 1
            ? completed[completed.Length / 2]
            : (completed[completed.Length / 2 - 1] + completed[completed.Length / 2]) / 2;
        var observed = Math.Max(0, end - first);
        return new(true, observed, summary.Stalls.Count,
            observed > 0 ? summary.Stalls.Count * 3600 / observed : null,
            summary.Stalls.Count > 0 ? summary.Stalls[0].Start - first : null,
            segments.Count > 0 ? segments.Max(s => s.Seconds) : 0, median, current, segments);
    }

    internal static void Check()
    {
        var samples = new List<RfiPlaybackChecks.Sample>();
        double position = 0;
        for (var time = 0; time <= 130; time++)
        {
            if (time >= 10 && time is not (>= 31 and <= 32) && time is not (>= 63 and <= 65)) position++;
            samples.Add(new(time, position, time < 10 ? Radio.Core.AudioState.Opening : Radio.Core.AudioState.Playing));
        }
        var profile = Analyze(samples);
        if (!profile.Started || profile.Interruptions != 2 || profile.SecondsBeforeFirstInterruption != 20 ||
            profile.LongestUninterruptedSeconds != 65 || profile.MedianCompletedUninterruptedSeconds != 25 ||
            profile.CurrentUninterruptedSeconds != 65 || profile.UninterruptedSegments[^1].EndedByInterruption)
            throw new Exception("Interruption count/continuous interval analysis failed.");
        var uninterrupted = Analyze([new(0, 0, Radio.Core.AudioState.Opening), new(1, 1, Radio.Core.AudioState.Playing),
            new(61, 61, Radio.Core.AudioState.Playing)]);
        if (uninterrupted.Interruptions != 0 || uninterrupted.LongestUninterruptedSeconds != 60 ||
            uninterrupted.SecondsBeforeFirstInterruption is not null || uninterrupted.MedianCompletedUninterruptedSeconds is not null)
            throw new Exception("Uninterrupted observations must remain right-censored, not a completed stable interval.");
        var tail = Analyze(samples.Take(65).ToArray());
        if (tail.Interruptions != 2 || tail.CurrentUninterruptedSeconds != 0)
            throw new Exception("An ongoing interruption must not count as current uninterrupted playback.");
        if (Analyze([new(0, 0, Radio.Core.AudioState.Opening), new(60, 0, Radio.Core.AudioState.Buffering)]).Started)
            throw new Exception("Unstarted playback is not uninterrupted playback.");
        var eventProfile = AnalyzeEvents([new(0, "waiting"), new(1, "playing"), new(2, "waiting"),
            new(2.05, "waiting"), new(2.1, "playing"), new(3, "pause"), new(4, "playing"),
            new(5, "waiting"), new(6, "error")], "playing", "waiting", "pause", "error");
        if (eventProfile.BufferingEpisodes != 2 || eventProfile.CompletedUnderHalfSecond != 1 ||
            eventProfile.PausesAfterStart != 1 || eventProfile.Errors != 1 || eventProfile.Episodes[^1].End is not null)
            throw new Exception("Short buffering, startup exclusion, duplicate waiting and pause/error separation failed.");
        Console.WriteLine("PASS: interruption count, completed/ongoing uninterrupted intervals, startup exclusion and unfinished stalls.");
        Console.WriteLine("PASS: sub-0.5s buffering events, duplicate coalescing and pause/error separation.");
    }
}
