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
    Uri? EndpointUrl = null,
    IReadOnlyList<string>? Languages = null,
    string? Country = null,
    string? Genre = null,
    string? City = null,
    string? BroadcastFrequency = null)
{
    private static readonly HashSet<string> LanguageCodes = System.Globalization.CultureInfo
        .GetCultures(System.Globalization.CultureTypes.NeutralCultures)
        .Select(culture => culture.TwoLetterISOLanguageName)
        .Where(code => code.Length == 2 && code != "iv").ToHashSet(StringComparer.OrdinalIgnoreCase);
    public bool HasFrequency => !string.IsNullOrWhiteSpace(Frequency);
    public string FrequencyLabel => HasFrequency ? Frequency : "WEB";
    // Optional, verified local FM identification for the card only. Keep catalog sorting/search intact.
    public bool HasCardFrequency => HasFrequency || !string.IsNullOrWhiteSpace(BroadcastFrequency);
    public string CardFrequencyLabel => !string.IsNullOrWhiteSpace(BroadcastFrequency) ? BroadcastFrequency : FrequencyLabel;
    public string LocationLabel => string.IsNullOrWhiteSpace(Country) || Country == "Taiwan" ? "" :
        string.Join(" ", new[] { Country, City }.Where(value => !string.IsNullOrWhiteSpace(value)));
    // Country only distinguishes domestic display; language always comes from verified catalog data.
    // Unknown country/language retains the existing fallback. Never mutate the source frequency.
    public string ListLabel => !string.IsNullOrWhiteSpace(Country) && Country != "Taiwan" &&
        Languages?.FirstOrDefault() is { } language && LanguageCodes.Contains(language)
        ? language.ToUpperInvariant() : FrequencyLabel;
    public string DisplayName => HasFrequency ? $"FM {Frequency}　{Name}" : $"WEB　{Name}";
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
        return stations.OrderBy(station => station.HasFrequency
            ? decimal.Parse(station.Frequency, System.Globalization.CultureInfo.InvariantCulture)
            : decimal.MaxValue).ToArray();
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
