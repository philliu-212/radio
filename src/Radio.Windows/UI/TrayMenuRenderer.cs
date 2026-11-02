namespace Radio.Windows.UI;

internal sealed class TrayMenuRenderer(PlayerTheme theme) : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(theme.Surface);
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(theme.Track);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.Item.Enabled && e.Item.Selected ? theme.Selection : theme.Surface);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle,
            e.Item.Enabled ? theme.Text : theme.Muted, e.TextFormat);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var size = (int)Math.Round(16 * (e.ToolStrip?.DeviceDpi ?? 96) / 96d);
        var center = new Point(e.ImageRectangle.Left + e.ImageRectangle.Width / 2, e.Item.Height / 2);
        GlyphPainter.Draw(e.Graphics, PlayerGlyph.Check, new Rectangle(center.X - size / 2, center.Y - size / 2, size, size),
            e.Item.Enabled ? theme.Accent : theme.Muted);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(theme.Track);
        var inset = (int)Math.Round(6 * (e.ToolStrip?.DeviceDpi ?? 96) / 96d);
        e.Graphics.DrawLine(pen, inset, e.Item.Height / 2, e.Item.Width - inset, e.Item.Height / 2);
    }
}
