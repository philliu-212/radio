using System.Net;
using Radio.Core;

internal static class MetadataChecks
{
    public static async Task RunAsync()
    {
        var now = new DateTimeOffset(2026, 10, 27, 9, 35, 0, TimeSpan.Zero);
        var upcoming = OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"17:35 即將播放：郭家瑋 - 我也我也"}""", now);
        Check(upcoming?.Kind == MetadataKind.UpcomingSong && upcoming.FirstLine == "即將播放：我也我也" && upcoming.SecondLine == "郭家瑋", "KISS upcoming label is preserved");
        Check(OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"12:35 即將播放：歌手 - 舊歌"}""", now) is null, "Reject stale KISS clock");
        Check(OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"17:35 arbitrary stream title"}""", now) is null, "Never infer a song from arbitrary stream title");
        var bcc = OfficialMetadataProvider.Parse("bcc-ilike", """[{"name":"中廣新聞網","program":"wrong"},{"name":"中廣流行網","program":"節目測試","dj":" 主持人 ","SecondsToExpire":30}]""", now);
        Check(bcc?.Kind == MetadataKind.Program && bcc.Title == "節目測試" && bcc.Person == "主持人" && bcc.ExpiresAt == now.AddSeconds(30), "BCC program/DJ by channel identity and expiry");
        Check(OfficialMetadataProvider.Parse("bcc-ilike", """[{"name":"中廣流行網","program":"old","SecondsToExpire":0}]""", now) is null, "Expired program is not displayed");
        var dj = OfficialMetadataProvider.Parse("hit-north", """{"value1":"<img src='ignore'>","value2":"<b>DJ 名稱</b>"}""", now);
        Check(dj?.Kind == MetadataKind.Presenter && dj.Title is null && dj.Person == "DJ 名稱", "Hit FM DJ remains presenter-only; markup is not rendered");
        Check(OfficialMetadataProvider.Parse("hit-north", """{"value2":null}""", now) is null, "Missing metadata stays empty");
        using var denied = new OfficialMetadataProvider(new Handler(HttpStatusCode.Forbidden));
        Check(await denied.GetAsync("kiss-999", default) is null, "Metadata HTTP refusal does not affect playback");
        Check(!denied.Supports("best-989") && await denied.GetAsync("best-989", default) is null, "Unsupported stations do not request metadata");
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine($"PASS: {message}");
    }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
