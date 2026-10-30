using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Core;

public enum MetadataKind { Song, UpcomingSong, Program, Presenter, ScheduledProgram, CurrentSong }

public sealed record StationMetadata(MetadataKind Kind, string? Title, string? Person,
    DateTimeOffset ExpiresAt)
{
    public string? ProgramTitle { get; init; }
    public string? Presenter { get; init; }
    public string? TimeSlot { get; init; }
    public string? NextProgram { get; init; }
    public string? FirstLine => Title is null ? null : Kind switch
    {
        MetadataKind.CurrentSong => $"現在播放：{Title}",
        MetadataKind.UpcomingSong => $"即將播放：{Title}",
        MetadataKind.Program => $"節目：{Title}",
        MetadataKind.ScheduledProgram => $"節目表：{Title}",
        _ => Title
    };
    public string? SecondLine => string.Join(" ｜ ", new[] { PersonLine,
        Presenter is null ? null : $"DJ：{Presenter}",
        ProgramTitle is null ? null : $"節目：{ProgramTitle}",
        TimeSlot is null ? null : $"時段：{TimeSlot}",
        NextProgram is null ? null : $"接下來：{NextProgram}" }.Where(text => text is not null)) is { Length: > 0 } line ? line : null;
    private string? PersonLine => Person is null ? null : Kind switch
    {
        MetadataKind.Program or MetadataKind.Presenter or MetadataKind.ScheduledProgram => $"DJ：{Person}",
        _ => Person
    };
}

public interface IStationMetadataProvider : IDisposable
{
    bool Supports(string stationId);
    Task<StationMetadata?> GetAsync(string stationId, CancellationToken cancellationToken);
}

// Only verified official text endpoints. No second audio connection, HTML browser, or song inference.
public sealed class OfficialMetadataProvider : IStationMetadataProvider
{
    private readonly HttpClient _client;
    private string? _bestSchedule;
    private DateTimeOffset _bestScheduleFetched;
    public OfficialMetadataProvider(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = TimeSpan.FromSeconds(10);
        // RFI includes inline styles; still bounded, and never loads linked resources.
        _client.MaxResponseContentBufferSize = 1024 * 1024;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LightweightRadio/0.3");
    }
    public bool Supports(string stationId) => stationId is "best-989" or "bcc-ilike" or "hit-north" or "kiss-999" or "pop-917" or "ufo-921" or "rfi-monde";
    public async Task<StationMetadata?> GetAsync(string stationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stationId is "pop-917" or "ufo-921" or "rfi-monde")
        {
            var pageUrl = stationId switch
            {
                "pop-917" => "https://www.pop917.com/",
                "ufo-921" => "https://www.uforadio.com.tw/",
                _ => "https://www.rfi.fr/fr/grille-des-programmes/monde"
            };
            using var page = await _client.GetAsync(pageUrl, cancellationToken);
            if (!page.IsSuccessStatusCode) return null;
            var html = await page.Content.ReadAsStringAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return OfficialProgramPages.Parse(stationId, html, DateTimeOffset.UtcNow);
        }
        if (stationId == "best-989")
        {
            var now = DateTimeOffset.UtcNow;
            if (_bestSchedule is null || now - _bestScheduleFetched >= TimeSpan.FromMinutes(15))
            {
                // Official schedule currently exposes HTTP, like the station's public homepage.
                // Treat it strictly as untrusted text, never execute HTML/JS or infer a song.
                using var schedule = await _client.GetAsync("http://www.bestradio.com.tw/schedule989.htm", cancellationToken);
                if (!schedule.IsSuccessStatusCode) return null;
                var html = await schedule.Content.ReadAsStringAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _bestSchedule = html; _bestScheduleFetched = DateTimeOffset.UtcNow;
            }
            return BestRadioSchedule.Parse(_bestSchedule, DateTimeOffset.UtcNow);
        }
        var url = stationId switch
        {
            "bcc-ilike" => "https://www.bcc.com.tw/webapi/BCCRadioWebAPI/ChannelInfoBat",
            "hit-north" => "https://www.hitoradio.com/newweb/onair_n_ajax_cname.php",
            "kiss-999" => "https://www.kiss.com.tw/templates/header_songlist_json.php?area=0",
            _ => null
        };
        if (url is null) return null;
        using var response = await _client.GetAsync(url, cancellationToken);
        // Public endpoint refusal is not a reason to bypass access controls or fail playback.
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var metadata = Parse(stationId, json, DateTimeOffset.UtcNow);
        if (stationId != "kiss-999") return metadata;
        try
        {
            // Fetch text only: never load the homepage's scripts, images, iframes or audio.
            using var page = await _client.GetAsync("https://www.kiss.com.tw/", cancellationToken);
            if (!page.IsSuccessStatusCode) return metadata;
            var html = await page.Content.ReadAsStringAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var program = KissRadioProgram.Parse(html, DateTimeOffset.UtcNow);
            if (program is null) return metadata;
            return metadata is null ? program : metadata with
            {
                ProgramTitle = program.Title, Presenter = program.Person,
                ExpiresAt = metadata.ExpiresAt < program.ExpiresAt ? metadata.ExpiresAt : program.ExpiresAt
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or RegexMatchTimeoutException ||
            ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return metadata;
        }
    }

    internal static StationMetadata? Parse(string stationId, string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var expiry = now.AddSeconds(90);
        if (stationId == "bcc-ilike" && root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (Text(item, "name") != "中廣流行網") continue;
                if (item.TryGetProperty("SecondsToExpire", out var seconds) && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetDouble(out var ttl))
                {
                    if (ttl <= 0 || !double.IsFinite(ttl)) return null;
                    expiry = now.AddSeconds(Math.Min(90, ttl));
                }
                var title = Text(item, "program"); var dj = Text(item, "dj");
                var time = Text(item, "time");
                if (time is not null)
                {
                    if (!OfficialProgramPages.TrySlot(time, now, out var end, out var label)) return null;
                    if (end < expiry) expiry = end;
                    time = label;
                }
                return title is null && dj is null ? null : new(MetadataKind.Program, title, dj, expiry) { TimeSlot = time };
            }
        }
        if (stationId == "hit-north")
        {
            var dj = Text(root, "value2");
            if (dj is null) return null;
            // The endpoint returns a combined official label, not just a person's name.
            var parts = Regex.Match(dj, @"^(.+?DJ)\s*[-－]\s*(.+)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return parts.Success ? new(MetadataKind.Program, parts.Groups[1].Value.Trim(), parts.Groups[2].Value.Trim(), expiry)
                : new(MetadataKind.Presenter, null, dj, expiry);
        }
        if (stationId == "kiss-999" && Text(root, "now_playing") is { } value)
        {
            // Preserve the official state; upcoming is never promoted to current.
            var match = Regex.Match(value, @"^(\d{2}):(\d{2})\s+(即將播放|現在播放)[：:]\s*(.+?)\s+-\s+(.+)$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var hour) || hour > 23 ||
                !int.TryParse(match.Groups[2].Value, out var minute) || minute > 59) return null;
            var taiwan = now.ToOffset(TimeSpan.FromHours(8));
            var distance = Math.Abs(taiwan.TimeOfDay.TotalMinutes - hour * 60 - minute);
            if (Math.Min(distance, 1440 - distance) > 5) return null;
            return new(match.Groups[3].Value == "現在播放" ? MetadataKind.CurrentSong : MetadataKind.UpcomingSong,
                match.Groups[5].Value, match.Groups[4].Value, expiry);
        }
        return null;
    }

    private static string? Text(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = WebUtility.HtmlDecode(Regex.Replace(value.GetString() ?? "", "<[^>]*>", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))).Trim();
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return text.Length is 0 or > 300 ? null : text;
    }
    public void Dispose() => _client.Dispose();
}
