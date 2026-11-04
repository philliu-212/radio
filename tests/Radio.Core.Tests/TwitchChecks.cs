using System.Net;
using Radio.Core;

internal static class TwitchChecks
{
    private static readonly Uri Page = new("https://www.twitch.tv/livekiss");
    private const string Token = """{"data":{"streamPlaybackAccessToken":{"signature":"fixture-signature","value":"{\"fixture\":true}"}}}""";
    private const string Playlist = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=700000,VIDEO=\"chunked\"\nhttps://video.example/video.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=160000,CODECS=\"mp4a.40.2\",VIDEO=\"audio_only\"\nhttps://fixture.hls.ttvnw.net/audio/index.m3u8?token=fixture\n";

    internal static async Task RunAsync()
    {
        var kiss = StationCatalog.Load().Single(station => station.Id == "kiss-999");
        var calls = 0;
        var resolver = new OfficialStreamResolver(() => new Handler(request =>
        {
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                Check(request.RequestUri!.Host == "gql.twitch.tv" && request.Headers.Contains("Client-ID"), "Public Twitch token endpoint");
                Check(request.Content!.ReadAsStringAsync().Result.Contains("livekiss"), "Fixed livekiss channel");
                return Response(Token);
            }
            Check(request.RequestUri!.Host == "usher.ttvnw.net" && request.RequestUri.Query.Contains("allow_audio_only=true"), "Twitch audio manifest request");
            return Response(Playlist);
        }));
        var stream = await resolver.ResolveFallbackAsync(kiss, default);
        Check(calls == 2 && stream!.Uri.AbsolutePath == "/audio/index.m3u8" && stream.IsHls &&
            stream.Referrer.AbsoluteUri == "https://www.twitch.tv/livekiss" && !stream.ToString().Contains("token="), "Audio-only backup without leaking token");
        await resolver.ResolveFallbackAsync(kiss, default);
        Check(calls == 4, "Each backup attempt resolves fresh credentials");
        Check(await resolver.ResolveFallbackAsync(StationCatalog.Load().Single(station => station.Id == "best-989"), default) is null && calls == 4, "Unconfigured stations never use livekiss");

        foreach (var body in new[] { "{}", "null", "{", """{"errors":[{"message":"denied"}]}""", """{"data":{"streamPlaybackAccessToken":null}}""" })
            await Fails(async () => await new OfficialStreamResolver(() => new Handler(_ => Response(body))).ResolveFallbackAsync(kiss, default));
        foreach (var code in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests })
            await Fails(async () => await new OfficialStreamResolver(() => new Handler(_ => new HttpResponseMessage(code))).ResolveFallbackAsync(kiss, default));
        foreach (var url in new[] { "https://evil.example/audio.m3u8", "http://fixture.ttvnw.net/audio.m3u8", "https://ttvnw.net.evil.example/audio.m3u8", "https://user@fixture.ttvnw.net/audio.m3u8", "https://fixture.ttvnw.net:444/audio.m3u8", "https://fixture.ttvnw.net/error.html" })
            await Fails(() => Task.FromResult<ResolvedStream?>(TwitchStreamResolver.ParseAudioPlaylist(Playlist.Replace("https://fixture.hls.ttvnw.net/audio/index.m3u8?token=fixture", url), Page)));
        foreach (var body in new[] { "<html>error</html>", "#EXTM3U\n", Playlist.Replace("VIDEO=\"audio_only\"", "VIDEO=\"chunked\"") })
            await Fails(() => Task.FromResult<ResolvedStream?>(TwitchStreamResolver.ParseAudioPlaylist(body, Page)));
        Check(TwitchStreamResolver.ParseAudioPlaylist(Playlist.Replace("fixture.hls.ttvnw.net", "fixture.hls.live-video.net"), Page).IsHls, "Twitch alternate CDN accepted");
        foreach (var url in new[] { "http://www.twitch.tv/livekiss", "https://evil.example/livekiss", "https://www.twitch.tv/livekiss?token=bad", "https://www.twitch.tv/a/b", "https://user@www.twitch.tv/livekiss" })
            await Fails(async () => await resolver.ResolveFallbackAsync(kiss with { BackupSources = [kiss.BackupSources![0] with { PageUrl = new Uri(url) }] }, default));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await resolver.ResolveFallbackAsync(kiss, canceled.Token); throw new Exception("Ignored cancellation"); }
        catch (OperationCanceledException) { Check(calls == 4, "Canceled fallback makes no request"); }
    }

    private static async Task Fails(Func<Task<ResolvedStream?>> action)
    {
        try { await action(); }
        catch (SourceResolutionException error)
        {
            Check(!error.Message.Contains("fixture-signature") && !error.Message.Contains("token="), "Backup failure is safe and explicit");
            return;
        }
        throw new Exception("Expected source failure");
    }
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
