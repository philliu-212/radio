using System.Text.Json;
using System.Text.Json.Serialization;

namespace Radio.Core;

public enum ResolverKind { Html, Form, BccJson, Direct }

public sealed record Station(
    string Id,
    string Name,
    string Frequency,
    Uri PageUrl,
    ResolverKind Resolver,
    string StreamHost,
    string MediaType,
    string? Match = null,
    Uri? EndpointUrl = null)
{
    public string DisplayName => $"FM {Frequency}　{Name}";
}

public static class StationCatalog
{
    public static IReadOnlyList<Station> Load()
    {
        using var stream = typeof(StationCatalog).Assembly
            .GetManifestResourceStream("Radio.Core.stations.json")!;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var stations = JsonSerializer.Deserialize<Station[]>(stream, options)
            ?? throw new InvalidDataException("電台清單無法載入。");
        return stations.OrderBy(station => decimal.Parse(station.Frequency, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }
}

// URLs may contain short-lived credentials. Do not log or persist them.
public sealed class ResolvedStream(Uri uri, Uri referrer, string mediaType)
{
    public Uri Uri { get; } = uri;
    public Uri Referrer { get; } = referrer;
    public string MediaType { get; } = mediaType;
    public bool IsHls => MediaType == "application/vnd.apple.mpegurl";
    public override string ToString() => $"{Uri.Host} ({MediaType})";
}
