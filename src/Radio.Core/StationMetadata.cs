using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Core;

public enum MetadataKind { Song, UpcomingSong, Program, Presenter }

public sealed record StationMetadata(MetadataKind Kind, string? Title, string? Person,
    DateTimeOffset ExpiresAt)
{
    public string? FirstLine => Title is null ? null : Kind switch
    {
        MetadataKind.UpcomingSong => $"即將播放：{Title}",
        MetadataKind.Program => $"節目：{Title}",
        _ => Title
    };
    public string? SecondLine => Person is null ? null : Kind switch
    {
        MetadataKind.Program or MetadataKind.Presenter => $"DJ：{Person}",
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
    public OfficialMetadataProvider(HttpMessageHandler? handler = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = TimeSpan.FromSeconds(10);
        _client.MaxResponseContentBufferSize = 256 * 1024;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LightweightRadio/0.3");
    }
    public bool Supports(string stationId) => stationId is "bcc-ilike" or "hit-north" or "kiss-999";
    public async Task<StationMetadata?> GetAsync(string stationId, CancellationToken cancellationToken)
    {
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
