namespace Radio.Windows.UI;

// Keep native tooltip placement, sizing and timing; only replace its painting.
internal sealed class ThemedToolTip : ToolTip
{
    public ThemedToolTip()
    {
        OwnerDraw = true;
        SetTheme(PlayerTheme.Dark);
        Draw += (_, e) =>
        {
            using var background = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            ControlPaint.DrawBorder(e.Graphics, e.Bounds, BorderColor, ButtonBorderStyle.Solid);
            var textBounds = Rectangle.Inflate(e.Bounds, -3, -2);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, e.Font, textBounds, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        };
    }

    internal Color BorderColor { get; private set; }

    public void SetTheme(PlayerTheme theme)
    {
        BackColor = theme.Surface;
        ForeColor = theme.Text;
        BorderColor = theme.Track;
    }
}
