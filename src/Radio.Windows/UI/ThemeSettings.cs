using System.Text.Json;

namespace Radio.Windows.UI;

internal sealed class ThemeSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "dark";
    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "radio.settings.json");

    public static bool LoadDarkMode()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 4096) return true;
            var settings = JsonSerializer.Deserialize<ThemeSettings>(File.ReadAllText(FilePath));
            return settings?.SchemaVersion != 1 || settings.Theme != "light";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return true; }
    }

    public static void Save(bool dark)
    {
        var temporary = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ThemeSettings { Theme = dark ? "dark" : "light" }));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A read-only portable folder still supports a session-only theme. Never fall back to AppData/registry.
        }
    }
}
