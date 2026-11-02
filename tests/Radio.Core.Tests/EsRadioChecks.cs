using System.Net;
using System.Text;
using System.Text.Json;
using Radio.Core;

internal static class EsRadioChecks
{
    private static string Fixture(string start = "06:00", string end = "12:00", string? presenter = "Federico Jiménez Losantos", string title = "Es la Mañana de Federico") =>
        JsonSerializer.Serialize(new
        {
            nombre = title, presentadores = presenter,
            url = "https://esradio.libertaddigital.com/es-la-manana-de-federico/",
            hora_inicio = start, hora_final = end,
            siguiente = new { nombre = "Kilómetro Cero", url = "https://esradio.libertaddigital.com/kilometro-cero/" },
            audios = new[] { new { titulo = "NOT A LIVE SONG" } }
        });

    public static async Task RunAsync()
    {
        var catalog = StationCatalog.Load();
        var station = catalog.Single(s => s.Id == "esradio");
        Check(station is { Country: "Spain", Genre: "News / Talk", ListLabel: "ES", Frequency: "" } && station.Languages!.SequenceEqual(["es"]), "esRadio verified catalog fields");
        Check(station is { LocationLabel: "Spain Madrid", CardFrequencyLabel: "99.1", HasCardFrequency: true, FrequencyLabel: "WEB", HasFrequency: false }, "Verified Madrid FM is card-only; original frequency and sorting remain untouched");
        Check(catalog.Single(s => s.Id == "rfi-monde") is { LocationLabel: "France", CardFrequencyLabel: "WEB", HasCardFrequency: false } &&
            catalog.Where(s => s.Country == "Taiwan").All(s => s.LocationLabel == "" && s.CardFrequencyLabel == s.Frequency), "RFI location/WEB and original Taiwan frequency presentation");
        Check((station with { Country = null, City = "Madrid", BroadcastFrequency = null }).LocationLabel == "" &&
            (station with { BroadcastFrequency = " " }).CardFrequencyLabel == "WEB", "Missing location/frequency stays unknown without guessing");
        var stream = await new OfficialStreamResolver().ResolveAsync(station, default);
        Check(stream.Uri == station.EndpointUrl && stream.MediaType == "audio/mpeg" && stream.Uri.AbsolutePath.EndsWith("-high.mp3"), "esRadio direct high MP3 uses existing resolver");
        Check(catalog.Single(s => s.Id == "rfi-monde").ListLabel == "FR" &&
            catalog.Where(s => s.Country == "Taiwan").All(s => s.ListLabel == s.Frequency), "Foreign language labels preserve Taiwan FM frequencies");
        foreach (var code in new[] { "zh", "en", "fr", "es", "ja", "ko" })
            Check((station with { Languages = [code] }).ListLabel == code.ToUpperInvariant(), "ISO label " + code);
        Check((station with { Country = "UK", Name = "BBC Radio 1", Frequency = "97.1", Languages = ["en"] }).ListLabel == "EN", "Foreign FM frequency is not shown in list");
        foreach (var codes in new IReadOnlyList<string>?[] { null, [], ["unknown"], ["xx"], ["en-US"], ["", "es"] })
            Check((station with { Languages = codes }).ListLabel == "WEB", "Unknown primary language retains WEB without guessing");
        Check((station with { Frequency = "99.1", Languages = null }).ListLabel == "99.1" && station.FrequencyLabel == "WEB", "Unknown language retains original FM fallback; display-only label");
        Check((station with { Country = null }).ListLabel == "WEB" && (station with { Country = "", Frequency = "99.1" }).ListLabel == "99.1", "Unknown country preserves existing fallback too");
        var now = DateTimeOffset.Parse("2026-11-02T09:00:00Z");
        var result = EsRadioProgram.Parse(Fixture(), now);
        Check(result is { Kind: MetadataKind.Program, Title: "Es la Mañana de Federico", Person: "Federico Jiménez Losantos", NextProgram: "Kilómetro Cero" } && result.ExpiresAt == now.AddSeconds(90), "Official current program/DJ/next only, never audios as songs");
        Check(EsRadioProgram.Parse(Fixture(presenter: null), now)?.Person is null, "Missing host stays empty; no historical hard-coded mapping");
        Check(EsRadioProgram.Parse(Fixture(), DateTimeOffset.Parse("2026-11-02T11:00:00Z")) is null, "End boundary rejects old official selection");
        Check(EsRadioProgram.Parse(Fixture(), DateTimeOffset.Parse("2026-11-02T10:59:40Z"))?.ExpiresAt == DateTimeOffset.Parse("2026-11-02T11:00:00Z"), "TTL never passes slot end");
        Check(EsRadioProgram.Parse(Fixture("09:00", "10:00"), DateTimeOffset.Parse("2026-07-06T07:30:00Z")) is not null &&
            EsRadioProgram.Parse(Fixture("09:00", "10:00"), DateTimeOffset.Parse("2026-11-02T07:30:00Z")) is null, "Madrid summer/winter offsets are not fixed");
        foreach (var date in new[] { "2026-11-06T23:30:00Z", "2026-11-07T00:30:00Z", "2026-11-08T00:30:00Z" })
            Check(EsRadioProgram.Parse(Fixture("23:00", "02:00"), DateTimeOffset.Parse(date)) is not null, "Official selection across Friday/weekend midnight: " + date);
        var weekend = Fixture("08:00", "11:00", "Manuel Llamas", "La Trinchera de Llamas");
        Check(EsRadioProgram.Parse(weekend, DateTimeOffset.Parse("2026-11-07T08:30:00Z")) is { Title: "La Trinchera de Llamas", Person: "Manuel Llamas" } &&
            EsRadioProgram.Parse(weekend, DateTimeOffset.Parse("2026-11-08T08:30:00Z"))?.Person == "Manuel Llamas", "Weekend program/presenter follow current API, not weekday name mapping");
        foreach (var date in new[] { "2026-03-29T00:30:00Z", "2026-03-29T01:30:00Z", "2026-10-25T00:30:00Z", "2026-10-25T01:30:00Z" })
            Check(EsRadioProgram.Parse(Fixture("23:00", "04:00"), DateTimeOffset.Parse(date)) is not null, "DST transition live slot: " + date);
        Check(EsRadioProgram.Parse(Fixture("06:00", "25:00"), now) is null && EsRadioProgram.Parse("{}", now) is null &&
            EsRadioProgram.Parse(Fixture().Replace("esradio.libertaddigital.com", "other.example"), now) is null, "Changed/invalid data and foreign program URLs fail closed");
        using var provider = new OfficialMetadataProvider(new Handler());
        Check(provider.Supports("esradio") && await provider.GetAsync("esradio", default) is null, "Metadata refusal uses existing unavailable path");
    }

    // Explicit online diagnostic. Decode/playback is separately exercised by --esradio in Windows tests.
    public static async Task RunLiveAsync()
    {
        using var provider = new OfficialMetadataProvider();
        Console.WriteLine("Official metadata: " + JsonSerializer.Serialize(await provider.GetAsync("esradio", default)));
        foreach (var suffix in new[] { "high.mp3", "low.mp3", "high.aac" })
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://libertaddigital-radio-live1.flumotion.com/libertaddigital/ld-live1-" + suffix);
            request.Headers.Add("Icy-MetaData", "1");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var intervalText = response.Headers.TryGetValues("icy-metaint", out var values) ? values.Single() : null;
            Console.WriteLine($"{suffix}: HTTP {(int)response.StatusCode}, {response.Content.Headers.ContentType}, icy-metaint={intervalText ?? "absent"}");
            if (!int.TryParse(intervalText, out var interval) || interval is <= 0 or > 1024 * 1024) continue;
            using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var audio = new byte[interval]; var size = new byte[1];
            var titles = new HashSet<string>(); var blocks = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(20))
            {
                await body.ReadExactlyAsync(audio, timeout.Token);
                await body.ReadExactlyAsync(size, timeout.Token);
                if (size[0] > 0)
                {
                    var metadata = new byte[size[0] * 16];
                    await body.ReadExactlyAsync(metadata, timeout.Token);
                    titles.Add(Encoding.UTF8.GetString(metadata).TrimEnd('\0'));
                }
                blocks++;
            }
            Console.WriteLine($"{suffix}: {blocks} ICY blocks over {clock.Elapsed.TotalSeconds:F1}s; metadata={JsonSerializer.Serialize(titles)}");
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.RequestUri!.AbsoluteUri == "https://s1.staticld.com/rt/programa-actual-radio.json", "Provider uses national official JSON, not a regional grid");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
