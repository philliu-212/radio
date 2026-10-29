using Radio.Core;

namespace Radio.Windows.UI;

// Catalog and membership are separate: more catalog sources can be merged by stable ID
// without changing the player, membership settings or stream resolvers.
internal sealed class StationLibrary
{
    private readonly ThemeSettings _settings;
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
    }

    public void Remove(string id) => _settings.MyStationIds.Remove(id);
    public long Count(string id) => _settings.PlayCounts.GetValueOrDefault(id);
    public void RecordSuccess(string id) => _settings.PlayCounts[id] = Math.Min(long.MaxValue - 1, Count(id)) + 1;
    // LINQ's stable sort preserves membership order for ties; never sort on a playback event.
    public void Sort() => _settings.MyStationIds = _settings.MyStationIds.OrderByDescending(Count).ToList();
    public static bool Matches(Station station, string query) =>
        station.Name.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
        station.FrequencyLabel.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
        station.DisplayName.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
