using System.Drawing.Drawing2D;

namespace Radio.Windows.UI;

// Language names belong to the UI, never to station-name classification.
internal sealed class LanguageFilterButton : Button
{
    internal static readonly (string Code, string Label)[] Options =
        [("", "全部語言"), ("zh", "中文"), ("en", "English"), ("fr", "Français")];
    private readonly PlayerTheme _theme;
    private bool _hover;
    public string Language { get; set; } = "";
    public string LanguageLabel => Options.FirstOrDefault(option => option.Code == Language).Label ?? Language;

    public LanguageFilterButton(PlayerTheme theme)
    {
        _theme = theme;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
        DoubleBuffered = true;
        Dock = DockStyle.Fill; Margin = Padding.Empty;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _theme.Canvas);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        if (_hover)
        {
            using var path = RoundedShape.Create(ClientRectangle, (int)(6 * scale));
            using var hover = new SolidBrush(_theme.Playing); g.FillPath(hover, path);
        }
        // 14-DIP globe plus its 8-DIP chevron in a 26 x 16 DIP box, centred in the
        // 34-DIP-wide button. The globe is one step above the 16-DIP category
        // glyphs but no longer reads as the largest element in the row.
        g.TranslateTransform((Width - 26 * scale) / 2, (Height - 16 * scale) / 2);
        g.ScaleTransform(scale, scale);
        var color = Language.Length > 0 ? _theme.Accent : _theme.Muted;
        using var pen = new Pen(color, 1.2f) { LineJoin = LineJoin.Round };
        g.DrawEllipse(pen, 1, 1, 14, 14);
        g.DrawEllipse(pen, 4.5f, 1, 7, 14);
        g.DrawLine(pen, 1, 8, 15, 8);
        g.DrawLines(pen, new Point[] { new(17, 6), new(21, 10), new(25, 6) });
        if (Language.Length > 0)
        {
            using var dot = new SolidBrush(_theme.Accent); g.FillEllipse(dot, 11, 0, 5, 5);
        }
        g.ResetTransform();
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), _theme.Accent, BackColor);
    }
}

internal sealed class LanguageMenuRenderer(PlayerTheme theme) : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(theme.Surface);
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(theme.Track);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected || e.Item is ToolStripMenuItem { Checked: true })
        {
            using var brush = new SolidBrush(theme.Selection);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        var selected = e.Item is ToolStripMenuItem { Checked: true };
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
            new Rectangle((int)(10 * scale), 0, e.Item.Width - (int)(35 * scale), e.Item.Height),
            selected ? theme.Accent : theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (selected) GlyphPainter.Draw(e.Graphics, PlayerGlyph.Check,
            new Rectangle(e.Item.Width - (int)(24 * scale), (e.Item.Height - (int)(14 * scale)) / 2, (int)(14 * scale), (int)(14 * scale)), theme.Accent);
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
}
