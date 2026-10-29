namespace Radio.Windows.UI;

internal static class AppIcon
{
    internal const string ResourceName = "Radio.Windows.Assets.RadioMini.ico";

    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("應用程式圖示資源遺失。");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone(); // Own the icon independently of the resource stream.
    }
}
