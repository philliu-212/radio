using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Radio.Core;
using Radio.Windows.Native;

namespace Radio.Windows.Tests;

internal static class RfiEdgeChecks
{
    internal static async Task RunAsync(string[] args)
    {
        var seconds = Math.Clamp(int.Parse(Option(args, "--seconds", "600")), 30, 1800);
        var output = Path.GetFullPath(Option(args, "--output", "artifacts/rfi-edge.json"));
        var station = StationCatalog.Load().Single(s => s.Id == "rfi-monde");
        using var resolving = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stream = await new OfficialStreamResolver().ResolveAsync(station, resolving.Token);
        var clock = new Stopwatch();
        var sameData = args.Contains("--edge-same-data");
        await using var relay = sameData ? new RfiTransportChecks.ImmediateRelay(stream.Uri, clock, sameData: true) : null;
        var playbackUri = relay is null ? stream.Uri : new Uri(relay.Uri, "live.mp3");
        var headless = args.Contains("--edge-headless");
        await using var edge = await EdgeCdpProbe.OpenAsync(headless);
        var plain = args.Contains("--plain-audio") || sameData;
        await edge.NavigateAsync(sameData ? new Uri(relay!.Uri, "probe.html").AbsoluteUri : plain ? "about:blank" : "https://www.rfi.fr/fr/direct-monde");
        await edge.CallAsync("Page.bringToFront", new { });
        Value(await edge.EvaluateAsync("new Promise(r=>{if(document.readyState==='complete')setTimeout(()=>r(true),2000);else window.addEventListener('load',()=>setTimeout(()=>r(true),2000),{once:true});})"));
        var ready = plain ? JsonSerializer.SerializeToElement(new { title = "Controlled HTML audio (not official page)" }) : Value(await edge.EvaluateAsync("""
            new Promise((resolve,reject)=>{
              const start=performance.now();
              const check=()=>{
                const rejectCookies=[...document.querySelectorAll('button')].find(b=>b.textContent.trim()==='Refuser'&&b.getBoundingClientRect().width>0);
                if(rejectCookies){rejectCookies.click();setTimeout(check,500);return;}
                const b=[...document.querySelectorAll('button')].find(b=>b.textContent.includes('Écouter le direct'));
                if(b) resolve({title:document.title, button:b.outerHTML,
                  sources:[...document.querySelectorAll('script[data-media-id="direct_rfi_monde"]')].map(s=>JSON.parse(s.textContent))});
                else if(performance.now()-start>30000) reject(new Error('Official live button not found: '+document.body.innerText.slice(0,1500)));
                else setTimeout(check,100);
              };check();
            })
            """));
        Console.WriteLine($"EDGE {edge.Version}; page ready: {ready.GetProperty("title")}");
        // Keep the pre-fix URL-source baseline explicit in this comparison tool.
        using var player = new SystemAudioPlayer(() => new MediaEngineAudioPlayer(), () => new WindowsAudioPlayer(),
            () => new MediaEngineAudioPlayer()) { IsMuted = true };
        var state = AudioState.Stopped;
        clock.Restart();
        var events = new List<object>();
        var samples = new List<RfiPlaybackChecks.Sample>();
        var native = new List<object>();
        string? earlyFailure = null;
        player.StatusChanged += status =>
        {
            state = status.State;
            events.Add(new { time = clock.Elapsed.TotalSeconds, state = status.State.ToString(), status.Message });
        };
        JsonElement trace;
        try
        {
            if (plain)
            {
                Value(await edge.EvaluateAsync("(()=>{window.__rfiProbe.origin=performance.now();const a=document.createElement('audio');a.controls=true;a.src="
                    + JsonSerializer.Serialize(playbackUri.AbsoluteUri) + ";document.body.append(a);a.play().catch(e=>window.__rfiProbe.playError=String(e));return true;})()"));
            }
            else
            {
                Value(await edge.EvaluateAsync("""
                (()=>{window.__rfiProbe.origin=performance.now();
                  const b=[...document.querySelectorAll('button')].find(b=>b.textContent.includes('Écouter le direct'));
                  if(!b) throw new Error('Missing live button');b.click();return {button:b.outerHTML};})()
                """));
            }
            await player.PlayAsync(new ResolvedStream(playbackUri, stream.Referrer, "audio/mpeg"), default);
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(250);
                var time = clock.Elapsed.TotalSeconds;
                samples.Add(new(time, player.Position.TotalSeconds, state));
                native.Add(new { time, detail = Snapshot(player) });
                if ((time >= 3 && time < 3.5) || (time >= 30 && time < 30.5))
                {
                    var health = Value(await edge.EvaluateAsync("({error:window.__rfiProbe.audio?.error?.code??0,position:window.__rfiProbe.audio?.currentTime??0})"));
                    if (health.GetProperty("error").GetInt32() != 0 || (time >= 30 && health.GetProperty("position").GetDouble() <= 0))
                    {
                        earlyFailure = "Browser playback failed to start; aborting invalid comparison early.";
                        break;
                    }
                }
            }
            trace = Value(await edge.EvaluateAsync("""
                (()=>{const p=window.__rfiProbe;
                  const result={samples:p.samples,events:p.events,origin:p.origin,performanceTimeOrigin:performance.timeOrigin,
                    samplesTakenAt:performance.now(),source:p.audio?.currentSrc ?? null,muted:p.audio?.muted ?? null,
                    userAgent:navigator.userAgent,title:document.title,visibility:document.visibilityState,
                    mediaNodes:[...document.querySelectorAll('audio,video')].map(a=>({src:a.currentSrc,paused:a.paused,ready:a.readyState})),
                    ready:document.readyState,playError:p.playError??null,
                    storeType:typeof window.store,
                    buttons:[...document.querySelectorAll('button')].map(b=>({text:b.textContent,aria:b.getAttribute('aria-label'),visible:b.getBoundingClientRect().width>0})).slice(0,40)};
                  p.audio?.pause();p.origin=null;return result;})()
                """));
        }
        finally
        {
            player.Stop();
            if (relay is not null) await relay.DisposeAsync();
        }
        var browserSamples = trace.GetProperty("samples").EnumerateArray().Select(s => new RfiPlaybackChecks.Sample(
            s.GetProperty("time").GetDouble(), s.GetProperty("position").GetDouble(),
            s.GetProperty("paused").GetBoolean() ? AudioState.Stopped : AudioState.Playing)).ToArray();
        var systemContinuity = PlaybackContinuity.Analyze(samples);
        var edgeContinuity = PlaybackContinuity.Analyze(browserSamples);
        var edgeEventProfile = PlaybackContinuity.AnalyzeEvents(trace.GetProperty("events").EnumerateArray()
            .Select(e => new PlaybackContinuity.Event(e.GetProperty("time").GetDouble(), e.GetProperty("event").GetString()!)),
            "playing", "waiting", "pause", "error");
        var statusJson = JsonSerializer.SerializeToElement(events);
        var systemEventProfile = PlaybackContinuity.AnalyzeEvents(statusJson.EnumerateArray()
            .Select(e => new PlaybackContinuity.Event(e.GetProperty("time").GetDouble(), e.GetProperty("state").GetString()!)),
            "Playing", "Buffering", "Paused", "Failed");
        var sources = trace.GetProperty("samples").EnumerateArray().Select(s => s.GetProperty("source").GetString())
            .Where(s => !string.IsNullOrEmpty(s)).Distinct().ToArray();
        var sourceMatches = trace.GetProperty("source").GetString() == playbackUri.AbsoluteUri
            && sources.All(s => s == playbackUri.AbsoluteUri);
        var validComparison = edgeContinuity.Started && systemContinuity.Started && sourceMatches
            && edgeEventProfile.PausesAfterStart == 0;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow, seconds, observedSeconds = clock.Elapsed.TotalSeconds, earlyFailure,
            headless, sameData, plainAudio = plain, edgeVersion = edge.Version, officialPage = ready,
            officialSource = stream.Uri.AbsoluteUri, sourceMatches, sources, validComparison,
            edge = new { continuity = edgeContinuity, bufferingEvents = edgeEventProfile, trace, mediaEvents = edge.MediaEvents, networkEvents = edge.NetworkEvents },
            system = new { continuity = systemContinuity, bufferingEvents = systemEventProfile, samples, events, native },
            transport = relay?.Observations.ToArray(),
            limitations = "Installed Edge in an isolated temporary profile; explicit headless flag is recorded, not the user's normal browser configuration. Default uses actual official live button and independent upstream TCP connections. plainAudio is a controlled HTML audio page, NOT the official player. sameData uses one upstream connection, identical immediate chunks to Edge HTML audio and SystemAudioPlayer, no replay queue; local HTTP clients and pipeline differ. All muted. 250ms clock sampling and >=0.5s stalls are not audible verification. Browser throttling flags disable background timer throttling; visibility is recorded. No MainForm recovery, product or personal settings changes. Media properties describe browser pipeline, not proof that one decoder caused an interruption. Native GetBuffered returning no ranges is not proof of an empty internal buffer."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Edge: interruptions={edgeContinuity.Interruptions}, longest continuous={edgeContinuity.LongestUninterruptedSeconds:F2}s");
        Console.WriteLine($"System: interruptions={systemContinuity.Interruptions}, longest continuous={systemContinuity.LongestUninterruptedSeconds:F2}s");
        Console.WriteLine($"EXPERIMENT COMPLETE (not audible/hour-long acceptance): {output}");
        if (!validComparison)
            throw new Exception("Playback did not start on the expected source, or Edge paused; not a valid buffering-only comparison.");
    }

    private static JsonElement Value(JsonElement result)
    {
        if (result.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.ToString());
        return result.GetProperty("result").GetProperty("value").Clone();
    }

    private static string Option(string[] args, string name, string fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint RangeLength(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RangeBound(IntPtr self, uint index, out double value);

    internal static object Snapshot(SystemAudioPlayer player)
    {
        try
        {
            var adapter = typeof(SystemAudioPlayer).GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player);
            if (adapter is not MediaEngineAudioPlayer media) return new { active = false };
            var engine = (IMFMediaEngine?)typeof(MediaEngineAudioPlayer)
                .GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(media);
            if (engine is null) return new { active = false };
            var position = engine.GetCurrentTime();
            engine.GetBuffered(out var ranges);
            var buffered = new List<double[]>();
            try
            {
                if (ranges != IntPtr.Zero)
                {
                    var table = Marshal.ReadIntPtr(ranges);
                    var length = Marshal.GetDelegateForFunctionPointer<RangeLength>(Marshal.ReadIntPtr(table, 3 * IntPtr.Size));
                    var start = Marshal.GetDelegateForFunctionPointer<RangeBound>(Marshal.ReadIntPtr(table, 4 * IntPtr.Size));
                    var end = Marshal.GetDelegateForFunctionPointer<RangeBound>(Marshal.ReadIntPtr(table, 5 * IntPtr.Size));
                    for (uint i = 0, count = Math.Min(length(ranges), 64); i < count; i++)
                    {
                        Marshal.ThrowExceptionForHR(start(ranges, i, out var a));
                        Marshal.ThrowExceptionForHR(end(ranges, i, out var b));
                        buffered.Add([a, b]);
                    }
                }
            }
            finally { if (ranges != IntPtr.Zero) Marshal.Release(ranges); }
            return new { active = true, position, preload = engine.GetPreload(), readyState = engine.GetReadyState(),
                networkState = engine.GetNetworkState(), paused = engine.IsPaused(), ended = engine.IsEnded(), buffered };
        }
        catch (COMException error) { return new { error = $"0x{error.HResult:X8}" }; }
    }
}
