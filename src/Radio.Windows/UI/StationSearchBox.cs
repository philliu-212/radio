using System.Drawing.Drawing2D;

namespace Radio.Windows.UI;

internal sealed class StationSearchBox : Panel
{
    private readonly TextBox _input = new() { BorderStyle = BorderStyle.None, Name = "SearchInput" };
    private PlayerTheme _theme = PlayerTheme.Dark;
    public bool Outlined { get; init; }
    public string Query { get => _input.Text; set => _input.Text = value; }
    public event Action? QueryChanged;

    public StationSearchBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Controls.Add(_input);
        _input.TextChanged += (_, _) => QueryChanged?.Invoke();
        _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Query = ""; e.SuppressKeyPress = true; } };
        AccessibleName = "搜尋電台名稱或頻率";
        _input.AccessibleName = AccessibleName;
        TabStop = false;
        SetTheme(_theme);
    }

    public void SetTheme(PlayerTheme theme)
    {
        _theme = theme; _input.BackColor = theme.Surface; _input.ForeColor = theme.Text;
        Invalidate();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var scale = (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f;
        _input.SetBounds((int)(31 * scale), Math.Max(0, (Height - _input.PreferredHeight) / 2),
            Math.Max(1, Width - (int)(43 * scale)), _input.PreferredHeight);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? _theme.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedShape.Create(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using var brush = new SolidBrush(_theme.Surface);
        e.Graphics.FillPath(brush, path);
        if (Outlined)
        {
            using var border = new Pen(_theme.Track, Math.Max(1, (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f));
            e.Graphics.DrawPath(border, path);
        }
        var scale = (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f;
        var size = (int)(14 * scale);
        GlyphPainter.Draw(e.Graphics, PlayerGlyph.Search, new Rectangle((int)(10 * scale), (Height - size) / 2, size, size), _theme.Muted);
    }
}
