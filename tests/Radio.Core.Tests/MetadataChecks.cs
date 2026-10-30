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
        var current = OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"17:35 現在播放：歌手 - 歌名"}""", now);
        Check(current is { Kind: MetadataKind.CurrentSong, FirstLine: "現在播放：歌名", SecondLine: "歌手" }, "KISS official current song is recognized without relabeling upcoming");
        Check(OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"12:35 現在播放：歌手 - 舊歌"}""", now) is null, "Stale current song is rejected too");
        Check(OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"24:35 現在播放：歌手 - 歌名"}""", now) is null, "Invalid KISS clock is rejected");
        Check(OfficialMetadataProvider.Parse("kiss-999", """{"now_playing":"23:59 現在播放：歌手 - 歌名"}""", new DateTimeOffset(2026, 10, 27, 16, 1, 0, TimeSpan.Zero)) is not null, "KISS song freshness crosses midnight");
        CheckKissProgram(now);
        using var kiss = new OfficialMetadataProvider(new KissHandler());
        var combined = await kiss.GetAsync("kiss-999", default);
        Check(combined?.FirstLine == "現在播放：測試歌名" && combined.SecondLine == "測試歌手 ｜ DJ：測試主持人 ｜ 節目：KISS Day & Night", "KISS provider combines official current song, DJ and program in existing two lines");
        using var noPage = new OfficialMetadataProvider(new KissHandler(denyPage: true));
        Check((await noPage.GetAsync("kiss-999", default)) is { Kind: MetadataKind.CurrentSong, ProgramTitle: null }, "Homepage refusal retains valid KISS song");
        using var failedPage = new OfficialMetadataProvider(new KissHandler(failPage: true));
        Check((await failedPage.GetAsync("kiss-999", default))?.Kind == MetadataKind.CurrentSong, "Homepage network error retains valid KISS song");
        using var staleSong = new OfficialMetadataProvider(new KissHandler(staleSong: true));
        Check((await staleSong.GetAsync("kiss-999", default)) is { Kind: MetadataKind.Program, Person: "測試主持人" }, "Missing song falls back to official program and DJ");
        var bcc = OfficialMetadataProvider.Parse("bcc-ilike", """[{"name":"中廣新聞網","program":"wrong"},{"name":"中廣流行網","program":"節目測試","dj":" 主持人 ","SecondsToExpire":30}]""", now);
        Check(bcc?.Kind == MetadataKind.Program && bcc.Title == "節目測試" && bcc.Person == "主持人" && bcc.ExpiresAt == now.AddSeconds(30), "BCC program/DJ by channel identity and expiry");
        Check(OfficialMetadataProvider.Parse("bcc-ilike", """[{"name":"中廣流行網","program":"old","SecondsToExpire":0}]""", now) is null, "Expired program is not displayed");
        var dj = OfficialMetadataProvider.Parse("hit-north", """{"value1":"<img src='ignore'>","value2":"<b>DJ 名稱</b>"}""", now);
        Check(dj?.Kind == MetadataKind.Presenter && dj.Title is null && dj.Person == "DJ 名稱", "Hit FM DJ remains presenter-only; markup is not rendered");
        Check(OfficialMetadataProvider.Parse("hit-north", """{"value2":null}""", now) is null, "Missing metadata stays empty");
        using var denied = new OfficialMetadataProvider(new Handler(HttpStatusCode.Forbidden));
        Check(await denied.GetAsync("kiss-999", default) is null, "Metadata HTTP refusal does not affect playback");
        Check(!denied.Supports("pop-917") && await denied.GetAsync("pop-917", default) is null, "Unsupported stations do not request metadata");
        CheckBestSchedule();
        var scheduleHandler = new ScheduleHandler();
        using var scheduleProvider = new OfficialMetadataProvider(scheduleHandler);
        Check(await scheduleProvider.GetAsync("best-989", default) is not null && await scheduleProvider.GetAsync("best-989", default) is not null && scheduleHandler.Calls == 1,
            "Official Best schedule is cached, not downloaded on every visible refresh");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await scheduleProvider.GetAsync("best-989", cancelled.Token); throw new Exception("Cancelled schedule request succeeded."); }
        catch (OperationCanceledException) { Check(scheduleHandler.Calls == 1, "Cancellation applies even when schedule is cached"); }
    }
    private const string KissPage = """
        <!-- <div class="header_text">大眾廣播 FM99.9<div class="show">舊節目 00:00 - 24:00</div><div class="song"> -->
        <div class="header_text"><div class="station_list">大眾廣播 FM99.9(南部地區)</div>
        <div class="dj_name_head"><img id="dj_img" src="ignore"><a><img id="dj_name_img" src="ignore" alt="測試主持人"></a></div>
        <div class="show"><a>KISS Day &amp; Night 00:00 - 24:00</a> &nbsp;</div><div class="song">ignore</div></div>
        """;
    private static void CheckKissProgram(DateTimeOffset now)
    {
        var page = KissPage.Replace("00:00 - 24:00", "16:00 - 18:00");
        var program = KissRadioProgram.Parse(page, now);
        Check(program is { FirstLine: "節目：KISS Day & Night", SecondLine: "DJ：測試主持人" }, "KISS uses FM99.9 on-air header, decodes entities and ignores comments");
        Check(KissRadioProgram.Parse(page, now.AddMinutes(25)) is null, "KISS program disappears at end of official slot");
        Check(KissRadioProgram.Parse(page, now.AddHours(-2)) is null, "KISS out-of-slot header fails closed");
        Check(KissRadioProgram.Parse(page, now.AddMinutes(24).AddSeconds(59))?.ExpiresAt == now.AddMinutes(25), "KISS program expires at slot boundary");
        Check(KissRadioProgram.Parse(page.Replace(" alt=\"測試主持人\"", ""), now) is { Person: null, Title: "KISS Day & Night" }, "KISS never invents a missing presenter");
        Check(KissRadioProgram.Parse(page.Replace("大眾廣播 FM99.9", "其他電台"), now) is null &&
            KissRadioProgram.Parse("<html>changed</html>", now) is null, "KISS rejects other stations and changed layout");
        Check(KissRadioProgram.Parse(page.Replace("16:00 - 18:00", "23:00 - 02:00"), now.AddHours(7)) is not null, "KISS overnight program uses Taiwan time");
        Check(KissRadioProgram.Parse(page.Replace("16:00 - 18:00", "16:00 - 25:00"), now) is null, "KISS invalid slot rejected");
    }
    private sealed class KissHandler(bool denyPage = false, bool failPage = false, bool staleSong = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.AbsolutePath == "/")
            {
                if (failPage) throw new HttpRequestException("Offline fixture");
                return Task.FromResult(new HttpResponseMessage(denyPage ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent(KissPage) });
            }
            var clock = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).AddHours(staleSong ? -2 : 0).ToString("HH:mm");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"now_playing":"{{clock}} 現在播放：測試歌手 - 測試歌名"}""") });
        }
    }
    private static void CheckBestSchedule()
    {
        const string page = """
        <table class="board-all">
        <tr><td class="board-line"><span class="DJ-time">09:00~12:00</span><span class="Program"><br>989陽光列車</span><span class="DJ"><a href="#"><img src="ignore"></a>亭君<br>宣傳不是主持人</span><!-- <span class="DJ">舊主持人</span> --></td>
        <td class="board-line2"><span class="Program"><span class="DJ-time">08:00~11:00</span><br>週六音樂<span class="DJ">Aiine</span></span></td>
        <td class="board-line3"><span class="DJ-time">08:00~11:00</span><span class="Program">週日音樂</span><span class="DJ">小嗨</span></td></tr>
        <tr><td class="board-line"><span class="DJ-time">12:00~14:00</span><span class="Program">午餐節目</span></td></tr>
        <tr><td colspan="2" class="board-line2"><span class="DJ-time">00:00~08:00</span><span class="Program">週末共同節目</span></td></tr>
        <tr><td class="board-line"><span class="DJ-time">22:00~24:00</span><span class="Program">夜間節目</span></td></tr>
        </table>
        """;
        var weekday = new DateTimeOffset(2026, 10, 28, 2, 0, 0, TimeSpan.Zero);
        var program = BestRadioSchedule.Parse(page, weekday);
        Check(program?.Kind == MetadataKind.ScheduledProgram && program.Title == "989陽光列車" && program.Person == "亭君" &&
            program.FirstLine == "節目表：989陽光列車", "Best 989 uses Taiwan time and explicitly labels scheduled program, not song");
        Check(BestRadioSchedule.Parse(page, weekday.AddDays(3))?.Person == "Aiine" &&
            BestRadioSchedule.Parse(page, weekday.AddDays(4))?.Person == "小嗨", "Best 989 distinguishes weekday/Saturday/Sunday schedule columns");
        Check(BestRadioSchedule.Parse(page, new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero))?.Title == "週末共同節目",
            "Best 989 handles shared weekend colspan without weekday leakage");
        Check(BestRadioSchedule.Parse(page, weekday.AddHours(2)) is { Title: "午餐節目", Person: null }, "Best 989 boundary chooses next program and never invents missing DJ");
        var justBeforeEnd = weekday.AddHours(1).AddMinutes(59).AddSeconds(59);
        Check(BestRadioSchedule.Parse(page, justBeforeEnd)?.ExpiresAt == weekday.AddHours(2), "Scheduled metadata expires exactly at slot boundary");
        Check(BestRadioSchedule.Parse(page, weekday.AddHours(13))?.Title == "夜間節目", "Best 989 accepts 24:00 as next midnight end");
        Check(BestRadioSchedule.Parse("<html>layout changed</html>", weekday) is null, "Changed schedule markup fails closed");
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
    private sealed class ScheduleHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            const string cell = "<span class='DJ-time'>00:00~24:00</span><span class='Program'>全天測試</span>";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"<table class='board-all'><td class='board-line'>{cell}</td><td colspan='2' class='board-line2'>{cell}</td></table>") });
        }
    }
}
