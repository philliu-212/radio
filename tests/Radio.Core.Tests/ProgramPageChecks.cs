using System.Net;
using Radio.Core;

internal static class ProgramPageChecks
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 30, 0, TimeSpan.Zero);
    private const string Pop = """
        <div class="liveBox2"><span class="time"><span class="station">桃竹苗FM90.7</span>　16:00~17:00</span><span class="program">別台<br /><span class="host">別人</span></span></div>
        <div class="liveBox2"><span class="time"><span class="station">北北基FM91.7</span>　16:00~17:00</span><span class="program">測試 &amp; 節目<br /><span class="host">測試主持</span></span><span class="next">NEXT<span class="nprogram">下一節目</span></span></div>
        """;
    private const string Ufo = """
        <div class="sm-program">宣傳節目</div>
        <div id="stream"><div class="sm-host"><a>主持甲、主持乙</a></div><div class="sm-time">16:00 - 17:00</div><div class="sm-program"><a>測試晚報</a></div>
        <source src="https://stream.rcs.revma.com/em90w4aeewzuv"></div><footer>ignore</footer>
        """;
    private const string Rfi = """
        <link rel="canonical" href="https://www.rfi.fr/fr/grille-des-programmes/monde">
        <span class="m-calendar-date__date">30/10/2026</span>
        <div class="m-item-program-grid"><time datetime="2026-10-30T08:10:30+01:00" class="m-item-program-grid__timeline__time">08:10 TU</time>
        <div class="m-item-program-grid__timeline__live">En Direct</div><p class="m-item-program-grid__infos__program-title">Émission &amp; test</p><p class="m-item-program-grid__infos__edition-authors">Par : Présentatrice</p></div>
        <div class="m-item-program-grid"><time class="m-item-program-grid__timeline__time">10:10 TU</time><p class="m-item-program-grid__infos__program-title">Future</p></div>
        """;

    public static async Task RunAsync()
    {
        var pop = OfficialProgramPages.Parse("pop-917", Pop, Now);
        Check(pop is { Title: "測試 & 節目", Person: "測試主持", NextProgram: "下一節目", TimeSlot: "16:00–17:00（台灣）" }, "POP selects FM91.7, not another channel or NEXT as current");
        Check(OfficialProgramPages.Parse("pop-917", Pop + Pop, Now) == pop, "POP accepts identical carousel clones");
        Check(OfficialProgramPages.Parse("pop-917", Pop + Pop.Replace("測試主持", "不同主持"), Now) is null, "POP conflicting carousel fails closed");
        Check(OfficialProgramPages.Parse("pop-917", Pop.Replace("class=\"host\"", "class=\"missing\""), Now)?.Person is null, "POP missing host stays empty");
        Check(OfficialProgramPages.Parse("pop-917", Pop, Now.AddMinutes(30)) is null, "POP slot boundary rejects old header");
        Check(OfficialProgramPages.Parse("pop-917", Pop.Replace("16:00~17:00", "23:00~02:00"), Now.AddHours(8))?.Title == pop?.Title, "POP overnight slot uses Taiwan previous-day start");
        var ufo = OfficialProgramPages.Parse("ufo-921", Ufo, Now);
        Check(ufo is { Title: "測試晚報", Person: "主持甲、主持乙" }, "UFO uses official player, not promotional blocks");
        Check(OfficialProgramPages.Parse("ufo-921", Ufo.Replace("em90w4aeewzuv", "wrong"), Now) is null, "UFO rejects unidentified feed");
        Check(OfficialProgramPages.Parse("ufo-921", Ufo, Now.AddMinutes(29).AddSeconds(59))?.ExpiresAt == Now.AddMinutes(30), "UFO expiry capped at slot end");
        Check(!OfficialProgramPages.TrySlot("24:00–25:00", Now, out _, out _) &&
            !OfficialProgramPages.TrySlot("16:00-16:00", Now, out _, out _), "Invalid and ambiguous time slots rejected");
        var rfi = OfficialProgramPages.Parse("rfi-monde", Rfi, Now);
        Check(rfi is { Kind: MetadataKind.ScheduledProgram, Title: "Émission & test", Person: "Présentatrice", TimeSlot: "08:10 UTC 起" }, "RFI uses dated Monde live marker and visible UTC, not datetime offset or future program");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("30/10/2026", "29/10/2026"), Now) is null, "RFI stale UTC date rejected");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("/monde", "/afrique"), Now) is null, "RFI rejects Afrique page with otherwise identical markup");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("30/10/2026", "31/10/2026"), Now.AddHours(8)) is null, "RFI UTC date is not Taiwan date");
        var rfiMidnight = Rfi.Replace("08:10 TU", "23:10 TU");
        Check(OfficialProgramPages.Parse("rfi-monde", rfiMidnight, new DateTimeOffset(2026, 10, 30, 23, 59, 59, TimeSpan.Zero))?.ExpiresAt ==
            new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), "RFI expires at UTC midnight");
        Check(OfficialProgramPages.Parse("rfi-monde", "<style>" + new string('x', 300 * 1024) + "</style>" + Rfi, Now)?.Title == rfi?.Title,
            "RFI bounded large inline styles do not block text parsing");
        Check(OfficialProgramPages.Parse("rfi-monde", new string('x', 1024 * 1024 + 1), Now) is null &&
            OfficialProgramPages.Parse("pop-917", new string('x', 256 * 1024 + 1), Now) is null, "Oversized metadata pages rejected");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("En Direct", ""), Now) is null, "RFI never infers current program from gaps in schedule");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("08:10 TU", "09:10 TU"), Now) is null, "RFI future visible UTC slot fails closed even with live marker");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi, Now.AddMinutes(40)) is null, "RFI live marker cannot extend through long unlisted gaps");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi + Rfi, Now) is null, "RFI ambiguous live markers rejected");
        foreach (var id in new[] { "pop-917", "ufo-921", "rfi-monde" })
        {
            Check(OfficialProgramPages.Parse(id, "<html>changed</html>", Now) is null &&
                OfficialProgramPages.Parse(id, "<!--" + (id == "pop-917" ? Pop : id == "ufo-921" ? Ufo : Rfi) + "-->", Now) is null,
                $"{id} changed or commented layout stays empty");
            using var denied = new OfficialMetadataProvider(new PageHandler(id, HttpStatusCode.Forbidden));
            Check(denied.Supports(id) && await denied.GetAsync(id, default) is null, $"{id} HTTP refusal returns empty");
            using var valid = new OfficialMetadataProvider(new PageHandler(id, HttpStatusCode.OK));
            await valid.GetAsync(id, default); // Fixed fixture may be out of slot in real time; request contract is independent.
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await valid.GetAsync(id, cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, $"{id} cancellation respected"); }
        }
        var hit = OfficialMetadataProvider.Parse("hit-north", """{"value2":"不累DJ - Bibi趙之璧"}""", Now);
        Check(hit is { Kind: MetadataKind.Program, Title: "不累DJ", Person: "Bibi趙之璧" }, "Hit FM separates official program label and DJ");
        var bcc = OfficialMetadataProvider.Parse("bcc-ilike", """[{"name":"中廣流行網","program":"節目","dj":"主持","time":"16:00~17:00","SecondsToExpire":500}]""", Now.AddMinutes(29).AddSeconds(59));
        Check(bcc?.TimeSlot == "16:00–17:00（台灣）" && bcc.ExpiresAt == Now.AddMinutes(30), "BCC shows official slot and expires at boundary");
        Check(OfficialMetadataProvider.Parse("bcc-ilike", """[{"name":"中廣流行網","program":"節目","time":"15:00~16:00"}]""", Now) is null, "BCC expired official time slot rejected");
        Check(pop?.SecondLine == "DJ：測試主持 ｜ 時段：16:00–17:00（台灣） ｜ 接下來：下一節目", "Useful info fits existing two-line metadata contract");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine($"PASS: {name}");
    }
    private sealed class PageHandler(string id, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var expected = id switch { "pop-917" => "https://www.pop917.com/", "ufo-921" => "https://www.uforadio.com.tw/", _ => "https://www.rfi.fr/fr/grille-des-programmes/monde" };
            Check(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == expected && request.Content is null,
                $"{id} requests official text page only");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(id == "pop-917" ? Pop : id == "ufo-921" ? Ufo : Rfi) });
        }
    }
}
