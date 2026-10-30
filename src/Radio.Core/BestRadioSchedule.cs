using System.Net;
using System.Text.RegularExpressions;

namespace Radio.Core;

// Time-based schedule, explicitly not real-time on-air confirmation. Never infer DJs from links.
internal static class BestRadioSchedule
{
    private const RegexOptions Options = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static Match Match(string text, string pattern) => Regex.Match(text, pattern, Options, TimeSpan.FromMilliseconds(100));
    private static string Replace(string text, string pattern, string replacement) => Regex.Replace(text, pattern, replacement, Options, TimeSpan.FromMilliseconds(100));
    private static string? Clean(string text)
    {
        text = WebUtility.HtmlDecode(Replace(text, "<[^>]*>", " "));
        text = Replace(text, @"\s+", " ").Trim();
        return text.Length is 0 or > 150 ? null : text;
    }
    public static StationMetadata? Parse(string html, DateTimeOffset now)
    {
        if (html.Length > 256 * 1024) return null;
        html = Replace(html, "<!--.*?-->", "");
        var table = Match(html, "<table\\b[^>]*class=[\"']board-all[\"'][^>]*>(.*?)</table>");
        if (!table.Success) return null;
        var local = now.ToOffset(TimeSpan.FromHours(8));
        var column = local.DayOfWeek switch { DayOfWeek.Saturday => "2", DayOfWeek.Sunday => "3", _ => "" };
        var minute = local.TimeOfDay.TotalMinutes;
        StationMetadata? result = null;
        var cells = Regex.Matches(table.Groups[1].Value, @"<td\b([^>]*)>(.*?)</td>", Options, TimeSpan.FromMilliseconds(100));
        if (cells.Count > 100) return null;
        foreach (Match cell in cells)
        {
            var attributes = cell.Groups[1].Value;
            var cellColumn = Match(attributes, "\\bclass=[\"']board-line(2|3)?[\"']");
            if (!cellColumn.Success) continue;
            var sharedWeekend = cellColumn.Groups[1].Value == "2" && Match(attributes, "\\bcolspan=[\"']2[\"']").Success;
            if (cellColumn.Groups[1].Value != column && !(column == "3" && sharedWeekend)) continue;
            var content = cell.Groups[2].Value;
            var time = Match(content, "<span\\b[^>]*class=[\"']DJ-time[\"'][^>]*>(\\d{2}):(\\d{2})\\s*~\\s*(\\d{2}):(\\d{2})</span>");
            if (!time.Success) continue;
            var startHour = int.Parse(time.Groups[1].Value); var startMinute = int.Parse(time.Groups[2].Value);
            var endHour = int.Parse(time.Groups[3].Value); var endMinute = int.Parse(time.Groups[4].Value);
            if (startHour > 23 || startMinute > 59 || endHour > 24 || endMinute > 59 || (endHour == 24 && endMinute != 0)) continue;
            var start = startHour * 60 + startMinute; var end = endHour * 60 + endMinute;
            if (end <= start || minute < start || minute >= end) continue;
            var djMatch = Match(content, "<span\\b[^>]*class=[\"']DJ[\"'][^>]*>(.*?)</span>");
            var dj = djMatch.Success ? Clean(Regex.Split(djMatch.Groups[1].Value, @"<br\b[^>]*>", Options, TimeSpan.FromMilliseconds(100))[0]) : null;
            content = content.Replace(time.Value, "");
            if (djMatch.Success) content = content.Replace(djMatch.Value, "");
            var program = Match(content, "<span\\b[^>]*class=[\"']Program[\"'][^>]*>(.*?)</span>");
            var title = program.Success ? Clean(program.Groups[1].Value) : null;
            if (title is null) continue;
            // Changed/ambiguous layouts must not silently select an arbitrary program.
            if (result is not null) return null;
            var slotEnd = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset).AddMinutes(end);
            result = new(MetadataKind.ScheduledProgram, title, dj, slotEnd < now.AddSeconds(90) ? slotEnd : now.AddSeconds(90))
            { TimeSlot = $"{startHour:00}:{startMinute:00}–{endHour:00}:{endMinute:00}（台灣）" };
        }
        return result;
    }
}
