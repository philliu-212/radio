namespace Radio.Windows.UI;

internal sealed record PlayerTheme(Color Canvas, Color Surface, Color Row, Color Hover,
    Color Playing, Color Selection, Color Text, Color Muted, Color Accent, Color Button,
    Color ButtonText, Color Track, Color Fill, Color Warning)
{
    private static Color Hex(string value) => ColorTranslator.FromHtml(value);
    public static PlayerTheme Dark { get; } = new(
        Hex("#202225"), Hex("#292D31"), Hex("#2A2D31"), Hex("#32393A"),
        Hex("#293A35"), Hex("#394A49"), Hex("#F0F1F2"), Hex("#A0ACA8"),
        Hex("#9BE0C6"), Hex("#A6DFC9"), Hex("#203D33"), Hex("#4E585A"), Hex("#9BD9C1"), Hex("#EDBF83"));
    public static PlayerTheme Light { get; } = new(
        Hex("#EEF1EF"), Hex("#FFFFFF"), Hex("#E3E8E5"), Hex("#D7E2DC"),
        Hex("#D4E9DF"), Hex("#BBD9CE"), Hex("#24342E"), Hex("#586C63"),
        Hex("#22694F"), Hex("#A6DFC9"), Hex("#203D33"), Hex("#BBC8C1"), Hex("#398564"), Hex("#965A13"));
}
