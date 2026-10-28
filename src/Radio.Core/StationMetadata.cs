using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Core;

public enum MetadataKind { Song, UpcomingSong, Program, Presenter, ScheduledProgram }

public sealed record StationMetadata(MetadataKind Kind, string? Title, string? Person,
    DateTimeOffset ExpiresAt)
{
    public string? FirstLine => Title is null ? null : Kind switch
    {
        MetadataKind.UpcomingSong => $"即將播放：{Title}",
        MetadataKind.Program => $"節目：{Title}",
        MetadataKind.ScheduledProgram => $"節目表：{Title}",
        _ => Title
    };
    public string? SecondLine => Person is null ? null : Kind switch
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
        _client.MaxResponseContentBufferSize = 256 * 1024;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LightweightRadio/0.3");
    }
    public bool Supports(string stationId) => stationId is "best-989" or "bcc-ilike" or "hit-north" or "kiss-999";
    public async Task<StationMetadata?> GetAsync(string stationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        return Parse(stationId, json, DateTimeOffset.UtcNow);
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
                return title is null && dj is null ? null : new(MetadataKind.Program, title, dj, expiry);
            }
        }
        if (stationId == "hit-north")
        {
            var dj = Text(root, "value2");
            return dj is null ? null : new(MetadataKind.Presenter, null, dj, expiry);
        }
        if (stationId == "kiss-999" && Text(root, "now_playing") is { } value)
        {
            // The observed contract is explicitly upcoming, never silently promote it to current song.
            var match = Regex.Match(value, @"^(\d{2}):(\d{2})\s+即將播放[：:]\s*(.+?)\s+-\s+(.+)$",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var hour) || hour > 23 ||
                !int.TryParse(match.Groups[2].Value, out var minute) || minute > 59) return null;
            var taiwan = now.ToOffset(TimeSpan.FromHours(8));
            var distance = Math.Abs(taiwan.TimeOfDay.TotalMinutes - hour * 60 - minute);
            if (Math.Min(distance, 1440 - distance) > 5) return null;
            return new(MetadataKind.UpcomingSong, match.Groups[4].Value, match.Groups[3].Value, expiry);
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
