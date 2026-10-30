using System.Net;
using Radio.Core;

if (args.Contains("--metadata-live"))
{
    using var provider = new OfficialMetadataProvider();
    foreach (var station in StationCatalog.Load().Where(station => station.Id != "kiss-999"))
    {
        try
        {
            var metadata = await provider.GetAsync(station.Id, default);
            Console.WriteLine($"{station.Id}: {System.Text.Json.JsonSerializer.Serialize(metadata)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            Console.WriteLine($"{station.Id}: unavailable ({ex.GetType().Name})");
        }
    }
    return;
}

if (args.Contains("--metadata-best-live"))
{
    using var provider = new OfficialMetadataProvider();
    var metadata = await provider.GetAsync("best-989", default);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(metadata));
    Environment.ExitCode = metadata is null ? 1 : 0;
    return;
}

var stations = StationCatalog.Load();
Check(stations.Count == 7 && stations.Select(x => x.Id).Distinct().Count() == 7, "Seven distinct stations");
Check(stations.Select(x => x.Frequency).SequenceEqual(["91.7", "92.1", "96.3", "98.9", "99.9", "107.7", ""]), "Stations sorted by numeric FM frequency, then internet-only stations");
Check(stations.Single(x => x.Frequency == "96.3").Id == "bcc-ilike", "96.3 is BCC, not Alian");
Check(stations.Single(x => x.Id == "hit-north").Frequency == "107.7", "Hit FM is northern feed");

var best = stations.Single(x => x.Id == "best-989");
var bestResolver = Resolver(_ => Response("<video src=\"https://bestradiow-hichannel.cdn.hinet.net/live/RA000013/playlist.m3u8?token=test&amp;expires=9999999999\"></video>"));
var resolved = await bestResolver.ResolveAsync(best, default);
Check(resolved.Uri.Query == "?token=test&expires=9999999999", "Decode HTML entities without dropping signatures");
Check(!resolved.ToString().Contains("token="), "Diagnostic string does not reveal signed URLs");

foreach (var station in stations.Where(x => x.Resolver == ResolverKind.Form))
{
    var requests = new List<string>();
    var resolver = Resolver(request =>
    {
        requests.Add(request.Method.Method);
        Check(request.Headers.Referrer == station.PageUrl, "Official referrer");
        if (request.Method == HttpMethod.Get) return Response("official page");
        Check(request.RequestUri == station.EndpointUrl, "Official form endpoint");
        Check(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult() == "channelID=1&action=getLIVEURL", "Official form payload");
        return Response($"https://{station.StreamHost}/live/playlist.m3u8?token=fixture");
    });
    await resolver.ResolveAsync(station, default);
    Check(requests.SequenceEqual(["GET", "POST"]), "Initialize session before requesting URL");
}

var bcc = stations.Single(x => x.Id == "bcc-ilike");
var jsonResolver = Resolver(_ => Response("""
    [{"name":"中廣新聞網","iosStream":"https://stream.rcs.revma.com/wrong"},
     {"name":"中廣流行網","iosStream":"https://stream.rcs.revma.com/correct"}]
    """));
Check((await jsonResolver.ResolveAsync(bcc, default)).Uri.AbsolutePath == "/correct", "BCC channel matched by name, not array order");

var ufo = stations.Single(x => x.Id == "ufo-921");
var direct = Resolver(_ => throw new Exception("Direct source should not fetch the station homepage."));
Check((await direct.ResolveAsync(ufo, default)).Uri == ufo.EndpointUrl, "Reuse verified public UFO stream without fetching its homepage");
var rfi = stations.Single(x => x.Id == "rfi-monde");
var rfiStream = await direct.ResolveAsync(rfi, default);
Check(rfiStream.Uri == new Uri("https://rfimonde64k.ice.infomaniak.ch/rfimonde-64.mp3") &&
    rfiStream.MediaType == "audio/mpeg" && !rfiStream.IsHls && rfiStream.Referrer == rfi.PageUrl,
    "RFI Monde uses the official MP3 live stream, not Afrique or a replay");
Check(!rfi.HasFrequency && rfi.FrequencyLabel == "WEB" && !rfi.DisplayName.Contains("FM"),
    "Internet-only RFI does not invent an FM frequency");
await ExpectFailure(direct, rfi with { EndpointUrl = new Uri("https://other.example/live.mp3") }, "Direct RFI source still enforces the expected host");
await ExpectFailure(Resolver(_ => Response("maintenance")), best, "Fail clearly when page layout changes");
var pop = stations.Single(x => x.Id == "pop-917");
foreach (var invalid in new[] { "http://hitoradioweb-hichannel.cdn.hinet.net/test", "https://other.example/test", "https://user:pass@hitoradioweb-hichannel.cdn.hinet.net/test", "https://hitoradioweb-hichannel.cdn.hinet.net:444/test", "<html>error</html>" })
    await ExpectFailure(Resolver(_ => Response(invalid)), pop, "Reject unexpected URL");
await ExpectFailure(Resolver(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)), best, "Respect official HTTP denial");
await ExpectFailure(Resolver(_ => Response("not-json")), bcc, "Handle changed API format");
await ExpectFailure(Resolver(_ => Response("{}")), bcc, "Handle API object instead of array");
await ExpectFailure(Resolver(_ => Response("[]")), bcc, "Handle missing BCC channel");
await ExpectFailure(Resolver(_ => Response("""[{"name":"中廣流行網","iosStream":null}]""")), bcc, "Handle null stream");

using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await bestResolver.ResolveAsync(best, canceled.Token);
    throw new Exception("Cancellation was ignored.");
}
catch (OperationCanceledException) { Console.WriteLine("PASS: Cancellation"); }
await MetadataChecks.RunAsync();
Console.WriteLine("All offline checks passed.");

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}

static OfficialStreamResolver Resolver(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
    new(() => new FixtureHandler(respond));
static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
static async Task ExpectFailure(OfficialStreamResolver resolver, Station station, string name)
{
    try { await resolver.ResolveAsync(station, default); }
    catch (SourceResolutionException) { Console.WriteLine($"PASS: {name}"); return; }
    throw new Exception(name);
}

sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(respond(request));
    }
}
