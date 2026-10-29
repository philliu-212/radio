using Radio.Core;
using System.Globalization;

namespace Radio.Windows.UI;

// Catalog and membership are separate: more catalog sources can be merged by stable ID
// without changing the player, membership settings or stream resolvers.
internal sealed class StationLibrary
{
    private readonly ThemeSettings _settings;
    public event Action? OrderChanged;
    public IReadOnlyList<Station> Catalog { get; }
    public IReadOnlyList<Station> Mine => _settings.MyStationIds
        .Select(id => Catalog.FirstOrDefault(station => station.Id == id)).OfType<Station>().ToArray();
    public IReadOnlyList<Station> Available => Catalog.Where(station => !_settings.MyStationIds.Contains(station.Id)).ToArray();

    public StationLibrary(ThemeSettings settings, IReadOnlyList<Station>? catalog = null)
    {
        _settings = settings;
        Catalog = catalog ?? StationCatalog.Load();
        _settings.MyStationIds = _settings.MyStationIds.Distinct().ToList();
        Sort();
    }

    public void Add(string id)
    {
        if (Catalog.Any(station => station.Id == id) && !_settings.MyStationIds.Contains(id))
            _settings.MyStationIds.Add(id);
        _settings.PlayCounts.TryAdd(id, 0);
        Sort();
    }

    public void Remove(string id) => _settings.MyStationIds.Remove(id);
    public long Count(string id) => _settings.PlayCounts.GetValueOrDefault(id);
    public void RecordSuccess(string id)
    {
        _settings.PlayCounts[id] = Math.Min(long.MaxValue - 1, Count(id)) + 1;
        Sort(); OrderChanged?.Invoke();
    }
    // LINQ's stable sort preserves membership order for ties.
    public void Sort() => _settings.MyStationIds = _settings.MyStationIds.OrderByDescending(Count).ToList();
    public static bool MatchesLanguage(Station station, string? language) =>
        string.IsNullOrEmpty(language) || station.Languages?.Any(code =>
            string.Equals(code, language, StringComparison.OrdinalIgnoreCase)) == true;

    public static bool Matches(Station station, string query)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return new[] { station.Name, station.FrequencyLabel, station.DisplayName }
            .Any(value => compare.IndexOf(value, query.Trim(), options) >= 0);
    }
}
