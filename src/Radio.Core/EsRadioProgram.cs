using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Core;

// Same national current-program JSON used by the official site's #directoesradio.
// The server selects weekdays/weekends; do not infer a recurring schedule or read "audios".
internal static class EsRadioProgram
{
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    internal static StationMetadata? Parse(string json, DateTimeOffset now)
    {
        if (json.Length > 1024 * 1024) return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var title = Text(root, "nombre");
        var startText = Text(root, "hora_inicio");
        var endText = Text(root, "hora_final");
        if (title is null || !OfficialProgram(root) || !TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) || !TimeOnly.TryParseExact(endText, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end) || start == end) return null;
        var local = TimeZoneInfo.ConvertTime(now, Madrid);
        var clock = TimeOnly.FromDateTime(local.DateTime);
        if (start < end ? clock < start || clock >= end : clock < start && clock >= end) return null;

        // Resolve the end in Madrid, not using a fixed UTC offset (including midnight and DST).
        var endDay = local.Date.AddDays(start > end && clock >= start ? 1 : 0);
        var wallEnd = DateTime.SpecifyKind(endDay + end.ToTimeSpan(), DateTimeKind.Unspecified);
        // A nonexistent spring-forward boundary cannot safely establish a program expiry.
        if (Madrid.IsInvalidTime(wallEnd)) return null;
        var offsets = Madrid.IsAmbiguousTime(wallEnd) ? Madrid.GetAmbiguousTimeOffsets(wallEnd) : [Madrid.GetUtcOffset(wallEnd)];
        var ends = offsets.Select(offset => new DateTimeOffset(wallEnd, offset)).Where(value => value > now).Order().ToArray();
        if (ends.Length == 0) return null;
        var expiry = ends[0] < now.AddSeconds(90) ? ends[0] : now.AddSeconds(90);
        string? next = null;
        if (root.TryGetProperty("siguiente", out var following) && OfficialProgram(following))
            next = Text(following, "nombre");
        return new(MetadataKind.Program, title, Text(root, "presentadores"), expiry)
        {
            TimeSlot = $"{startText}–{endText}（Madrid）", NextProgram = next
        };
    }

    private static bool OfficialProgram(JsonElement item) =>
        Uri.TryCreate(Text(item, "url"), UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "esradio.libertaddigital.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    private static string? Text(JsonElement item, string key)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = WebUtility.HtmlDecode(Regex.Replace(value.GetString() ?? "", "<[^>]*>", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
        return text.Length is 0 or > 300 ? null : text;
    }
}
