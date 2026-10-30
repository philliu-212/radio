using System.Net;
using System.Text.RegularExpressions;

namespace Radio.Core;

// Only the official FM99.9 on-air header, not DJ biographies or the weekly schedule.
internal static class KissRadioProgram
{
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern,
        RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static StationMetadata? Parse(string html, DateTimeOffset now)
    {
        html = Regex.Replace(html, @"<!--.*?-->|<script\b[^>]*>.*?</script>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var header = Match(html, "<div\\b[^>]*class=[\"']header_text[\"'][^>]*>(.*?)<div\\b[^>]*class=[\"']song[\"']");
        if (!header.Success || !header.Groups[1].Value.Contains("大眾廣播 FM99.9", StringComparison.Ordinal)) return null;
        var content = header.Groups[1].Value;
        var show = Match(content, "<div\\b[^>]*class=[\"']show[\"'][^>]*>(.*?)</div>");
        var text = Clean(show.Groups[1].Value);
        if (text is null) return null;
        var slot = Match(text, @"^(.+?)\s+(\d{2}):(\d{2})\s*-\s*(\d{2}):(\d{2})$");
        if (!slot.Success) return null;
        var start = Minutes(slot.Groups[2].Value, slot.Groups[3].Value, false);
        var end = Minutes(slot.Groups[4].Value, slot.Groups[5].Value, true);
        if (start is null || end is null || start == end) return null;
        var taiwan = now.ToOffset(TimeSpan.FromHours(8));
        var minute = taiwan.TimeOfDay.TotalMinutes;
        var duration = end.Value > start.Value ? end.Value - start.Value : end.Value - start.Value + 1440;
        var elapsed = (minute - start.Value + 1440) % 1440;
        if (elapsed >= duration) return null;
        var djBlock = Match(content, "<div\\b[^>]*class=[\"']dj_name_head[\"'][^>]*>(.*?)</div>");
        var dj = Match(djBlock.Groups[1].Value, "<img\\b(?=[^>]*\\bid=[\"']dj_name_img[\"'])[^>]*\\balt=[\"']([^\"']*)[\"']");
        var startDate = taiwan.Date.AddMinutes(start.Value);
        if (startDate > taiwan.DateTime) startDate = startDate.AddDays(-1);
        var expiry = new DateTimeOffset(startDate, taiwan.Offset).AddMinutes(duration);
        return new(MetadataKind.Program, slot.Groups[1].Value, Clean(dj.Groups[1].Value),
            expiry < now.AddSeconds(90) ? expiry : now.AddSeconds(90));
    }

    private static int? Minutes(string hourText, string minuteText, bool allowMidnight)
    {
        var hour = int.Parse(hourText); var minute = int.Parse(minuteText);
        return minute < 60 && (hour < 24 || allowMidnight && hour == 24 && minute == 0) ? hour * 60 + minute : null;
    }

    private static string? Clean(string value)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]*>", "", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)));
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
        return text.Length is > 0 and <= 300 ? text : null;
    }
}
