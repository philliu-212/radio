using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Radio.Core;

// Official HTML is untrusted text only. No scripts, images, audio, or arbitrary StreamTitle.
internal static class OfficialProgramPages
{
    private const RegexOptions Options = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, Options, TimeSpan.FromMilliseconds(100));
    private static string Replace(string text, string pattern, string replacement) => Regex.Replace(text, pattern, replacement, Options, TimeSpan.FromMilliseconds(100));
    private static string? Clean(string text)
    {
        text = Replace(WebUtility.HtmlDecode(Replace(text, "<[^>]*>", " ")), @"\s+", " ").Trim();
        return text.Length is 0 or > 150 ? null : text;
    }
    private static string? Field(string text, string tag, string className)
    {
        var match = Match(text, $"<{tag}\\b[^>]*class=[\"']{className}[\"'][^>]*>(.*?)</{tag}>");
        return match.Success ? Clean(match.Groups[1].Value) : null;
    }

    internal static bool TrySlot(string text, DateTimeOffset now, out DateTimeOffset end, out string? label)
    {
        end = default; label = null;
        var time = Match(text, @"^(\d{2}):(\d{2})\s*[~～\-–]\s*(\d{2}):(\d{2})$");
        if (!time.Success) return false;
        var sh = int.Parse(time.Groups[1].Value); var sm = int.Parse(time.Groups[2].Value);
        var eh = int.Parse(time.Groups[3].Value); var em = int.Parse(time.Groups[4].Value);
        if (sh > 23 || sm > 59 || eh > 24 || em > 59 || eh == 24 && em != 0) return false;
        var start = sh * 60 + sm; var finish = eh * 60 + em;
        if (start == finish) return false;
        var local = now.ToOffset(TimeSpan.FromHours(8));
        var midnight = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        var minute = local.TimeOfDay.TotalMinutes;
        if (finish > start)
        {
            if (minute < start || minute >= finish) return false;
            end = midnight.AddMinutes(finish);
        }
        else
        {
            if (minute >= finish && minute < start) return false;
            end = midnight.AddDays(minute >= start ? 1 : 0).AddMinutes(finish);
        }
        label = $"{sh:00}:{sm:00}–{eh:00}:{em:00}（台灣）";
        return true;
    }

    public static StationMetadata? Parse(string stationId, string html, DateTimeOffset now)
    {
        if (html.Length > (stationId == "rfi-monde" ? 1024 * 1024 : 256 * 1024)) return null;
        html = Replace(Replace(html, "<!--.*?-->", ""), @"<(script|style)\b[^>]*>.*?</\1>", "");
        return stationId switch
        {
            "pop-917" => Pop(html, now),
            "ufo-921" => Ufo(html, now),
            "rfi-monde" => Rfi(html, now),
            _ => null
        };
    }

    private static StationMetadata? Timed(string? title, string? person, string? time, DateTimeOffset now, string? next = null)
    {
        if (title is null || time is null || !TrySlot(time, now, out var end, out var label)) return null;
        return new(MetadataKind.Program, title, person, end < now.AddSeconds(90) ? end : now.AddSeconds(90))
        { TimeSlot = label, NextProgram = next };
    }

    private static StationMetadata? Pop(string html, DateTimeOffset now)
    {
        var blocks = Regex.Matches(html, "<div\\b[^>]*class=[\"']liveBox2[\"'][^>]*>(.*?)(?=<div\\b[^>]*class=[\"']liveBox2[\"']|<script\\b|$)", Options, TimeSpan.FromMilliseconds(100));
        StationMetadata? result = null;
        foreach (Match block in blocks)
        {
            var content = block.Groups[1].Value;
            if (Field(content, "span", "station") != "北北基FM91.7") continue;
            var time = Match(content, "</span>\\s*(\\d{2}:\\d{2}\\s*[~～-]\\s*\\d{2}:\\d{2})</span>");
            var title = Match(content, "<span\\b[^>]*class=[\"']program[\"'][^>]*>(.*?)<br\\s*/?>");
            var item = Timed(title.Success ? Clean(title.Groups[1].Value) : null, Field(content, "span", "host"),
                time.Success ? time.Groups[1].Value : null, now, Field(content, "span", "nprogram"));
            if (item is null) return null;
            // The carousel repeats FM91.7; identical clones are allowed, conflicts are not.
            if (result is not null && result != item) return null;
            result = item;
        }
        return result;
    }

    private static StationMetadata? Ufo(string html, DateTimeOffset now)
    {
        var player = Match(html, "<div\\b[^>]*id=[\"']stream[\"'][^>]*>(.*?)(?=<footer\\b|$)");
        if (!player.Success || !player.Groups[1].Value.Contains("https://stream.rcs.revma.com/em90w4aeewzuv", StringComparison.Ordinal)) return null;
        var content = player.Groups[1].Value;
        return Timed(Field(content, "div", "sm-program"), Field(content, "div", "sm-host"), Field(content, "div", "sm-time"), now);
    }

    private static StationMetadata? Rfi(string html, DateTimeOffset now)
    {
        // Reject redirects/fallbacks to Afrique or a replay grid even if their markup is identical.
        if (!Match(html, "<link\\b(?=[^>]*\\brel=[\"']canonical[\"'])(?=[^>]*\\bhref=[\"']https://www\\.rfi\\.fr/fr/grille-des-programmes/monde[\"'])[^>]*>").Success) return null;
        var date = Field(html, "span", "m-calendar-date__date");
        if (!DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
            day != DateOnly.FromDateTime(now.UtcDateTime)) return null;
        var blocks = Regex.Matches(html, "<div\\b[^>]*class=[\"']m-item-program-grid[\"'][^>]*>(.*?)(?=<div\\b[^>]*class=[\"']m-item-program-grid[\"']|$)", Options, TimeSpan.FromMilliseconds(100));
        StationMetadata? result = null;
        foreach (Match block in blocks)
        {
            var content = block.Groups[1].Value;
            // Never extend a magazine through an unlisted news bulletin or guess from the next start.
            if (Field(content, "div", "m-item-program-grid__timeline__live") != "En Direct") continue;
            if (result is not null) return null;
            var title = Field(content, "p", "m-item-program-grid__infos__program-title");
            var author = Field(content, "p", "m-item-program-grid__infos__edition-authors");
            var time = Field(content, "time", "m-item-program-grid__timeline__time");
            var clock = time is null ? System.Text.RegularExpressions.Match.Empty : Match(time, @"^(\d{2}):(\d{2}) TU$");
            if (title is null || !clock.Success || int.Parse(clock.Groups[1].Value) > 23 || int.Parse(clock.Groups[2].Value) > 59) return null;
            // Visible TU and datetime offsets disagree on the official site. Preserve the stated
            // start in UTC, use only its explicit live marker, and label this as schedule information.
            var start = new DateTimeOffset(day.Year, day.Month, day.Day, int.Parse(clock.Groups[1].Value), int.Parse(clock.Groups[2].Value), 0, TimeSpan.Zero);
            var expiry = now.AddSeconds(90);
            var midnight = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (start > now || now - start >= TimeSpan.FromHours(1)) return null;
            if (start.AddHours(1) < expiry) expiry = start.AddHours(1);
            if (midnight < expiry) expiry = midnight;
            result = new(MetadataKind.ScheduledProgram, title, author is null ? null : Clean(Replace(author, @"^Par\s*:\s*", "")), expiry)
            { TimeSlot = $"{clock.Groups[1].Value}:{clock.Groups[2].Value} UTC 起" };
        }
        return result;
    }
}
