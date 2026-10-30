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
    // Reduced official 2026-10-30 09:33 UTC response: the visible "TU" disagrees with
    // datetime's explicit +01:00. Keep the real shape/time to catch the original regression.
    private const string Rfi = """
        <link rel="canonical" href="https://www.rfi.fr/fr/grille-des-programmes/monde">
        <span class="m-calendar-date__date">30/10/2026</span>
        <div class="m-item-program-grid"><div class="m-item-program-grid__wrapper">
        <div class="m-item-program-grid__timeline m-item-program-grid__timeline--is-live">
        <time datetime="2026-10-30T10:10:30+01:00" class="m-item-program-grid__timeline__time">10:10 TU</time>
        <div class="m-item-program-grid__timeline__live-wrapper"><div class="m-item-program-grid__timeline__live">En Direct</div></div></div>
        <div class="m-item-program-grid__infos"><div class="m-item-program-grid__infos__titles"><a href="/fr/podcasts/8-milliards-voisins/">
        <p class="m-item-program-grid__infos__program-title">8 milliards de voisins</p><p class="m-item-program-grid__infos__edition-authors">Par : Emmanuelle Bastide</p>
        </a></div></div></div></div>
        <div class="m-item-program-grid"><time datetime="2026-10-30T13:30:30+01:00" class="m-item-program-grid__timeline__time">13:30 TU</time><p class="m-item-program-grid__infos__program-title">Littérature sans frontières</p></div>
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
        var rfiNow = new DateTimeOffset(2026, 10, 30, 9, 33, 15, TimeSpan.Zero);
        var rfi = OfficialProgramPages.Parse("rfi-monde", Rfi, rfiNow);
        Check(rfi is { Kind: MetadataKind.ScheduledProgram, Title: "8 milliards de voisins", Person: "Emmanuelle Bastide", TimeSlot: "09:10 UTC 起" }, "RFI real live response uses explicit datetime offset, not mislabeled visible TU");
        Check(rfi?.ExpiresAt == rfiNow.AddSeconds(90), "RFI metadata retains bounded refresh expiry");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("30/10/2026", "29/10/2026"), rfiNow) is null, "RFI calendar date must match the timestamp date");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi, rfiNow.AddDays(1)) is null, "RFI yesterday's live marker rejected");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("/monde", "/afrique"), rfiNow) is null, "RFI rejects Afrique page with otherwise identical markup");
        var nextLocalDay = Rfi.Replace("30/10/2026", "31/10/2026").Replace("2026-10-30T10:10:30+01:00", "2026-10-31T00:10:30+01:00").Replace("10:10 TU", "00:10 TU");
        Check(OfficialProgramPages.Parse("rfi-monde", nextLocalDay, new DateTimeOffset(2026, 10, 30, 23, 30, 0, TimeSpan.Zero))?.TimeSlot == "23:10 UTC 起", "RFI local date can be tomorrow while UTC is still today");
        Check(OfficialProgramPages.Parse("rfi-monde", nextLocalDay, new DateTimeOffset(2026, 10, 30, 23, 59, 59, TimeSpan.Zero)) is not null, "RFI remains valid across UTC midnight within its official local date");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi, new DateTimeOffset(2026, 10, 30, 22, 59, 59, TimeSpan.Zero))?.ExpiresAt ==
            new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero), "RFI clears dated page at official offset midnight");
        Check(OfficialProgramPages.Parse("rfi-monde", "<style>" + new string('x', 300 * 1024) + "</style>" + Rfi, rfiNow)?.Title == rfi?.Title,
            "RFI bounded large inline styles do not block text parsing");
        Check(OfficialProgramPages.Parse("rfi-monde", new string('x', 1024 * 1024 + 1), Now) is null &&
            OfficialProgramPages.Parse("pop-917", new string('x', 256 * 1024 + 1), Now) is null, "Oversized metadata pages rejected");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("En Direct", ""), rfiNow) is null, "RFI never infers current program from gaps in schedule");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi, new DateTimeOffset(2026, 10, 30, 9, 10, 29, TimeSpan.Zero)) is null, "RFI actual future start is rejected down to the second");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi, new DateTimeOffset(2026, 10, 30, 9, 10, 30, TimeSpan.Zero)) is not null, "RFI accepts exact actual start boundary");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi, rfiNow.AddHours(1))?.Title == rfi?.Title, "RFI explicit current marker is not discarded by an invented one-hour duration");
        foreach (var invalid in new[] { "2026-10-30T10:10:30", "2026-10-30T25:10:30+01:00", "2026-10-30T10:10:30+25:00", "not-a-date" })
            Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("2026-10-30T10:10:30+01:00", invalid), rfiNow) is null, "RFI missing offset or invalid ISO timestamp fails closed");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("10:10:30+01:00", "09:10:30Z"), rfiNow)?.TimeSlot == "09:10 UTC 起", "RFI supports explicit Z UTC timestamps");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("10:10:30+01:00", "10:10:30+02:00"), rfiNow)?.TimeSlot == "08:10 UTC 起", "RFI honors supplied summer offset without a hard-coded Paris or Taiwan offset");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi.Replace("class=\"m-item-program-grid__infos__edition-authors\"", "class=\"missing\""), rfiNow) is { Title: "8 milliards de voisins", Person: null }, "RFI missing author does not hide valid official program or invent a presenter");
        Check(OfficialProgramPages.Parse("rfi-monde", Rfi + Rfi, rfiNow) is null, "RFI ambiguous live markers rejected");
        foreach (var id in new[] { "pop-917", "ufo-921", "rfi-monde" })
        {
            Check(OfficialProgramPages.Parse(id, "<html>changed</html>", Now) is null &&
                OfficialProgramPages.Parse(id, "<!--" + (id == "pop-917" ? Pop : id == "ufo-921" ? Ufo : Rfi) + "-->", Now) is null,
                $"{id} changed or commented layout stays empty");
            using var denied = new OfficialMetadataProvider(new PageHandler(id, HttpStatusCode.Forbidden));
            Check(denied.Supports(id) && await denied.GetAsync(id, default) is null, $"{id} HTTP refusal returns empty");
            using var valid = new OfficialMetadataProvider(new PageHandler(id, HttpStatusCode.OK));
            var data = await valid.GetAsync(id, default); // POP/UFO fixed fixtures may be out of slot in real time.
            if (id == "rfi-monde") Check(data is { Title: "8 milliards de voisins", Person: "Emmanuelle Bastide", Kind: MetadataKind.ScheduledProgram },
                "RFI provider accepts realistic current HTTP fixture end to end");
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
            var page = id == "pop-917" ? Pop : id == "ufo-921" ? Ufo : Rfi;
            if (id == "rfi-monde")
            {
                var local = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(1));
                var start = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, local.Offset);
                page = page.Replace("30/10/2026", local.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("2026-10-30T10:10:30+01:00", start.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
            }
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(page) });
        }
    }
}
