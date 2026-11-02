namespace Radio.Windows.UI;

// A passive popup: never steals activation or intercepts tray mouse input.
internal sealed class TrayToolTipWindow : Form
{
    private PlayerTheme _theme = PlayerTheme.Dark;
    public TrayToolTipWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        AccessibleRole = AccessibleRole.ToolTip;
        ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont!;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var value = base.CreateParams;
            value.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // NOACTIVATE, TOOLWINDOW, TRANSPARENT
            return value;
        }
    }

    internal void SetTheme(PlayerTheme theme)
    {
        _theme = theme; BackColor = theme.Surface; ForeColor = theme.Text; Invalidate();
    }

    internal void ShowTip(string text, Rectangle anchor)
    {
        if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
        Text = text; AccessibleName = text;
        Location = anchor.Location;
        // Creating the handle on the target monitor gives this popup its own DPI.
        _ = Handle;
        var scale = DeviceDpi / 96f;
        var padding = (int)Math.Ceiling(6 * scale);
        var work = Screen.FromRectangle(anchor).WorkingArea;
        var measured = TextRenderer.MeasureText(text, Font,
            new Size(Math.Min((int)(420 * scale), Math.Max(1, work.Width - padding * 2)), 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        ClientSize = new Size(Math.Min(work.Width, measured.Width + padding * 2), Math.Min(work.Height, measured.Height + padding * 2));
        Location = Place(anchor, Size, work, (int)Math.Ceiling(8 * scale));
        Show(); Invalidate();
    }

    internal static Point Place(Rectangle anchor, Size size, Rectangle work, int gap)
    {
        var x = anchor.Left + (anchor.Width - size.Width) / 2;
        var y = anchor.Top - size.Height - gap;
        if (y < work.Top) y = anchor.Bottom + gap;
        return new Point(Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - size.Width)),
            Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, _theme.Track, ButtonBorderStyle.Solid);
        var padding = (int)Math.Ceiling(6 * DeviceDpi / 96f);
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -padding, -padding),
            ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
