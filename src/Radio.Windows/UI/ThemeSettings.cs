using System.Text.Json;

namespace Radio.Windows.UI;

internal sealed class ThemeSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "dark";
    public int? Volume { get; set; }
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "radio.settings.json");

    internal static ThemeSettings Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return new();
            var settings = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(path));
            if (settings?.SchemaVersion != 1) return new();
            if (settings.Volume is < 0 or > 100) settings.Volume = null;
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static bool LoadDarkMode() => Load().Theme != "light";

    public static void Save(bool dark, int? volume = null, string? path = null)
    {
        path ??= FilePath;
        var temporary = path + ".tmp";
        try
        {
            var settings = Load(path);
            settings.Theme = dark ? "dark" : "light";
            if (volume is { } value) settings.Volume = Math.Clamp(value, 0, 100);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A read-only portable folder still supports a session-only theme. Never fall back to AppData/registry.
        }
    }
}
