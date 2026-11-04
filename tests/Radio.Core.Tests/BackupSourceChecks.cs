using Radio.Core;

internal static class BackupSourceChecks
{
    internal static async Task RunAsync()
    {
        var catalog = StationCatalog.Load();
        Check(catalog.Where(station => station.BackupCount > 0).Select(station => station.Id)
            .ToHashSet().SetEquals(["kiss-999", "bcc-ilike", "rfi-monde", "esradio"]), "Only scanned usable backups are enabled");
        var resolver = new OfficialStreamResolver(() => new NoRequests());
        foreach (var station in catalog.Where(station => station.Id != "kiss-999"))
        {
            for (var index = 0; index < station.BackupCount; index++)
            {
                var source = station.BackupSources![index];
                var stream = await resolver.ResolveFallbackAsync(station, default, index);
                Check(stream is not null && stream.Uri == source.EndpointUrl && stream.MediaType == source.MediaType && stream.Referrer == source.PageUrl,
                    station.Id + " backup order and source type " + index);
            }
            Check(await resolver.ResolveFallbackAsync(station, default, -1) is null &&
                await resolver.ResolveFallbackAsync(station, default, station.BackupCount) is null, "Out-of-range backup is absent, not wrapped/retried");
        }
        var rfi = catalog.Single(station => station.Id == "rfi-monde");
        Check(rfi.BackupSources![0].EndpointUrl!.AbsolutePath == "/rfimonde-64.mp3", "Alternate RFI host still selects Monde, never Afrique mount");
        var esradio = catalog.Single(station => station.Id == "esradio");
        Check(esradio.BackupCount == 2 && esradio.BackupSources![0].EndpointUrl!.AbsolutePath.EndsWith("-low.mp3") &&
            esradio.BackupSources[1].EndpointUrl!.AbsolutePath.EndsWith("-med.aac"), "esRadio MP3 then AAC, no video/replay");
        Check((esradio with { BackupSources = Enumerable.Repeat(esradio.BackupSources![0], 20).ToArray() }).BackupCount == 3,
            "Backup attempts are capped even with excess configuration");
        foreach (var url in new[] { "http://libertaddigital-radio-live1.flumotion.com/audio", "https://evil.example/audio", "https://user@libertaddigital-radio-live1.flumotion.com/audio", "https://libertaddigital-radio-live1.flumotion.com:444/audio" })
        {
            var bad = esradio with { BackupSources = [esradio.BackupSources![0] with { EndpointUrl = new Uri(url) }] };
            try { await resolver.ResolveFallbackAsync(bad, default); throw new Exception("Unsafe backup accepted"); }
            catch (SourceResolutionException) { Console.WriteLine("PASS: Direct backup retains HTTPS/host/port/credentials validation"); }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class NoRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new Exception("Direct backup must not fetch a page/API");
    }
}
