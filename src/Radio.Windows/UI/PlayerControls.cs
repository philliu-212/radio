using System.Drawing.Drawing2D;

namespace Radio.Windows.UI;

internal static class RoundedShape
{
    public static GraphicsPath Create(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        if (d < 2) { path.AddRectangle(rectangle); return path; }
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
        path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal enum PlayerGlyph { Play, Stop, Speaker, Muted, Theme, Minimize, Close, External, Radio }

internal static class GlyphPainter
{
    // Vector glyphs avoid font-dependent emoji fallback and remain sharp at 100–200% DPI.
    public static void Draw(Graphics graphics, PlayerGlyph glyph, Rectangle bounds, Color color)
    {
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
        using var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        switch (glyph)
        {
            case PlayerGlyph.Play: graphics.FillPolygon(brush, new Point[] { new(7, 4), new(16, 10), new(7, 16) }); break;
            case PlayerGlyph.Stop:
                using (var path = RoundedShape.Create(new Rectangle(5, 5, 10, 10), 2)) graphics.DrawPath(pen, path);
                break;
            case PlayerGlyph.Speaker:
            case PlayerGlyph.Muted:
                graphics.DrawPolygon(pen, new Point[] { new(3, 7), new(6, 7), new(10, 4), new(10, 16), new(6, 13), new(3, 13) });
                if (glyph == PlayerGlyph.Speaker) { graphics.DrawArc(pen, 8, 5, 7, 10, -60, 120); graphics.DrawArc(pen, 8, 2, 11, 16, -55, 110); }
                else { graphics.DrawLine(pen, 14, 7, 18, 13); graphics.DrawLine(pen, 18, 7, 14, 13); }
                break;
            case PlayerGlyph.Theme:
                graphics.DrawArc(pen, 4, 4, 11, 11, 30, 285);
                graphics.DrawArc(pen, 9, 1, 9, 10, 90, 140);
                graphics.DrawLine(pen, 15, 2, 15, 4); graphics.DrawLine(pen, 18, 5, 16, 6);
                break;
            case PlayerGlyph.Minimize: graphics.DrawLine(pen, 6, 10, 14, 10); break;
            case PlayerGlyph.Close: graphics.DrawLine(pen, 6, 6, 14, 14); graphics.DrawLine(pen, 14, 6, 6, 14); break;
            case PlayerGlyph.External:
                graphics.DrawLines(pen, new Point[] { new(10, 5), new(5, 5), new(5, 16), new(16, 16), new(16, 11) });
                graphics.DrawLines(pen, new Point[] { new(12, 3), new(18, 3), new(18, 9) }); graphics.DrawLine(pen, 11, 10, 18, 3);
                break;
            case PlayerGlyph.Radio:
                graphics.FillEllipse(brush, 9, 9, 2, 2);
                graphics.DrawArc(pen, 5, 5, 10, 10, 135, 90); graphics.DrawArc(pen, 5, 5, 10, 10, -45, 90);
                graphics.DrawArc(pen, 1, 1, 18, 18, 140, 80); graphics.DrawArc(pen, 1, 1, 18, 18, -40, 80);
                break;
        }
        graphics.Restore(state);
    }
}

internal sealed class PlayerCard : Panel
{
    public PlayerCard() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedShape.Create(new Rectangle(0, 0, Width, Height), (int)(10 * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f));
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
    }
}

internal sealed class PlayerButton : Button
{
    private PlayerTheme _theme = PlayerTheme.Dark;
    private bool _hover;
    public bool Primary { get; init; }
    public PlayerGlyph Glyph { get; set; }
    public PlayerButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false; Cursor = Cursors.Hand; DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }
    public void SetTheme(PlayerTheme theme) { _theme = theme; Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? _theme.Canvas);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Primary || _hover)
        {
            using var path = RoundedShape.Create(new Rectangle(0, 0, Width - 1, Height - 1), (int)(10 * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f));
            using var brush = new SolidBrush(Primary ? (_hover ? ControlPaint.Light(_theme.Button, .1f) : _theme.Button) : _theme.Hover);
            e.Graphics.FillPath(brush, path);
        }
        var size = (int)(18 * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f);
        GlyphPainter.Draw(e.Graphics, Glyph, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), Primary ? _theme.ButtonText : _theme.Muted);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), _theme.Accent, BackColor);
    }
}

internal sealed class MuteButton : CheckBox
{
    public PlayerTheme Theme { get; set; } = PlayerTheme.Dark;
    public MuteButton()
    {
        Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        AccessibleName = "靜音"; Cursor = Cursors.Hand; DoubleBuffered = true;
        SetStyle(ControlStyles.UserPaint, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var size = (int)(19 * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f);
        GlyphPainter.Draw(e.Graphics, Checked ? PlayerGlyph.Muted : PlayerGlyph.Speaker,
            new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), Checked ? Theme.Accent : Theme.Muted);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2), Theme.Accent, BackColor);
    }
}

// Preserve the native trackbar's keyboard, mouse, accessibility and range semantics.
internal sealed class VolumeSlider : TrackBar
{
    public PlayerTheme Theme { get; set; } = PlayerTheme.Dark;
    public event Action? InteractionCompleted;
    public VolumeSlider()
    {
        AutoSize = false; Minimum = 0; Maximum = 100; TickStyle = TickStyle.None;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        // TBM_GETCHANNELRECT matches the native input mapping, including end padding.
        SendMessage(Handle, 0x041A, IntPtr.Zero, out var channel);
        var left = channel.Left; var width = Math.Max(1, channel.Right - left - 1);
        using var track = new Pen(Theme.Track, Math.Max(2, 3 * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var fill = new Pen(Theme.Fill, track.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var y = Height / 2;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLine(track, left, y, left + width, y);
        if (Value > 0) e.Graphics.DrawLine(fill, left, y, left + width * Value / Maximum, y);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), Theme.Accent, BackColor);
    }
    protected override void OnValueChanged(EventArgs e) { base.OnValueChanged(e); Invalidate(); }
    private bool _dragging;
    private void SetValueFromMouse(int x)
    {
        SendMessage(Handle, 0x041A, IntPtr.Zero, out var channel);
        var fraction = (x - channel.Left) / (double)Math.Max(1, channel.Right - channel.Left - 1);
        Value = Math.Clamp((int)Math.Round(Minimum + fraction * (Maximum - Minimum)), Minimum, Maximum);
    }
    protected override void WndProc(ref Message m)
    {
        // The drawn track is vertically centered, unlike the native thumb's hit rectangle.
        // Handle the entire visible control ourselves; leave native keyboard/wheel/UIA intact.
        if (m.Msg == 0x0201)
        {
            Focus(); _dragging = true; Capture = true;
            SetValueFromMouse(unchecked((short)m.LParam.ToInt64()));
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x0200 && _dragging)
        {
            SetValueFromMouse(unchecked((short)m.LParam.ToInt64()));
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x0202 && _dragging)
        {
            SetValueFromMouse(unchecked((short)m.LParam.ToInt64()));
            _dragging = false; Capture = false;
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, unchecked((short)m.LParam.ToInt64()), Height / 2, 0));
            InteractionCompleted?.Invoke();
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m);
        if (m.Msg is 0x020A or 0x0101) InteractionCompleted?.Invoke();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { _dragging = false; base.OnMouseCaptureChanged(e); }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, out NativeRect rectangle);
}

internal sealed class StationListBox : ListBox
{
    public event Action? SelectedStationClicked;
    public int HoverIndex { get; private set; } = -1;
    public Color ScrollbarColor { get; set; } = PlayerTheme.Dark.Track;
    internal int VisibleRows => Math.Max(1, ClientSize.Height / Math.Max(1, ItemHeight));
    internal int ItemContentWidth => Math.Max(0, ClientSize.Width - Dip(12));
    internal bool ScrollbarVisible => Items.Count > VisibleRows && (_pointerInside || _draggingScrollbar);
    private bool _hidingScrollbar;
    private bool _draggingScrollbar;
    private bool _pointerInside;
    private int _scrollGrabOffset;
    private int _wheelRemainder;
    public StationListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false; BorderStyle = BorderStyle.None;
        // ListBox's native owner drawing does not use WinForms' double buffer.
        // Paint rows and the overlay together so no intermediate cleared frame is visible.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        using (var background = new SolidBrush(BackColor)) e.Graphics.FillRectangle(background, e.ClipRectangle);
        for (var index = TopIndex; index < Items.Count; index++)
        {
            var bounds = GetItemRectangle(index);
            if (bounds.Top >= ClientSize.Height) break;
            if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
            var state = index == SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
            if (index == SelectedIndex && Focused && ShowFocusCues) state |= DrawItemState.Focus;
            OnDrawItem(new DrawItemEventArgs(e.Graphics, Font, bounds, index, state, ForeColor, BackColor));
        }
        DrawScrollbar(e.Graphics);
    }
    private void UpdateHover(int index, bool pointerInside)
    {
        var scrollbarWasVisible = ScrollbarVisible;
        var previous = HoverIndex;
        HoverIndex = index;
        _pointerInside = pointerInside;
        if (previous != index)
        {
            InvalidateRow(previous);
            InvalidateRow(index);
        }
        if (scrollbarWasVisible != ScrollbarVisible)
            Invalidate(new Rectangle(ItemContentWidth, 0, ClientSize.Width - ItemContentWidth, ClientSize.Height));
    }
    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var bounds = GetItemRectangle(index);
        bounds.Width = ItemContentWidth;
        if (bounds.IntersectsWith(ClientRectangle)) Invalidate(bounds);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var index = e.X < ItemContentWidth ? IndexFromPoint(e.Location) : -1;
        UpdateHover(index, true);
        base.OnMouseMove(e);
    }
    protected override void OnMouseEnter(EventArgs e) { UpdateHover(HoverIndex, true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { UpdateHover(-1, false); base.OnMouseLeave(e); }
    protected override void OnVisibleChanged(EventArgs e)
    {
        if (!Visible)
        {
            _pointerInside = false; HoverIndex = -1; _draggingScrollbar = false;
            if (Capture) Capture = false;
        }
        base.OnVisibleChanged(e);
    }
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.Style &= ~0x00200000; return parameters; }
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FitFiveRows(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitFiveRows(); }
    private void FitFiveRows()
    {
        var height = Math.Max(1, (int)Math.Round(37 * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96d));
        if (ItemHeight != height) ItemHeight = height;
    }
    private int Dip(int value) => (int)Math.Round(value * (FindForm()?.DeviceDpi ?? DeviceDpi) / 96d);
    internal Rectangle ScrollbarTrackBounds => new(ItemContentWidth + Dip(2), Dip(8), Math.Max(1, Dip(2)),
        Math.Max(0, Math.Min(ClientSize.Height, VisibleRows * ItemHeight) - Dip(16)));
    internal Rectangle ScrollThumb
    {
        get
        {
            var track = ScrollbarTrackBounds;
            var height = Math.Min(track.Height, Math.Max(Dip(18), track.Height * VisibleRows / Math.Max(1, Items.Count)));
            var range = Math.Max(1, Items.Count - VisibleRows);
            return new Rectangle(track.X, track.Y + (track.Height - height) * Math.Clamp(TopIndex, 0, range) / range,
                track.Width, height);
        }
    }
    private void DrawScrollbar(Graphics graphics)
    {
        if (!ScrollbarVisible || ScrollbarTrackBounds.Height == 0) return;
        using var brush = new SolidBrush(ScrollbarColor);
        using var path = RoundedShape.Create(ScrollThumb, Math.Max(1, ScrollThumb.Width / 2));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
    }
    private void ScrollToMouse(int y)
    {
        var thumb = ScrollThumb;
        var track = ScrollbarTrackBounds;
        var fraction = (y - track.Top - _scrollGrabOffset) / (double)Math.Max(1, track.Height - thumb.Height);
        SetScrollPosition((int)Math.Round(fraction * Math.Max(0, Items.Count - VisibleRows)));
    }
    private void SetScrollPosition(int index)
    {
        var next = Math.Clamp(index, 0, Math.Max(0, Items.Count - VisibleRows));
        if (next == TopIndex) return;
        TopIndex = next;
        Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { _draggingScrollbar = false; Invalidate(); base.OnMouseCaptureChanged(e); }
    protected override void WndProc(ref Message m)
    {
        var point = new Point(unchecked((short)m.LParam.ToInt64()), unchecked((short)(m.LParam.ToInt64() >> 16)));
        if (m.Msg == 0x020A)
        {
            _wheelRemainder += unchecked((short)(m.WParam.ToInt64() >> 16));
            var notches = _wheelRemainder / 120; _wheelRemainder %= 120;
            var lines = SystemInformation.MouseWheelScrollLines;
            if (lines < 0) lines = VisibleRows;
            SetScrollPosition(TopIndex - notches * lines);
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg is 0x0201 or 0x0203 && point.X >= ItemContentWidth)
        {
            _pointerInside = ClientRectangle.Contains(point);
            if (Items.Count > VisibleRows && point.Y >= ScrollbarTrackBounds.Top && point.Y < ScrollbarTrackBounds.Bottom)
            {
                Focus(); _draggingScrollbar = true; Capture = true;
                var thumb = ScrollThumb;
                _scrollGrabOffset = point.Y >= thumb.Top && point.Y < thumb.Bottom ? point.Y - thumb.Y : thumb.Height / 2;
                ScrollToMouse(point.Y);
            }
            m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x0200 && _draggingScrollbar)
        {
            _pointerInside = ClientRectangle.Contains(point); ScrollToMouse(point.Y); m.Result = IntPtr.Zero; return;
        }
        if (m.Msg == 0x0202 && _draggingScrollbar)
        {
            _pointerInside = ClientRectangle.Contains(point);
            ScrollToMouse(point.Y); _draggingScrollbar = false; Capture = false; Invalidate(); m.Result = IntPtr.Zero; return;
        }
        if (m.Msg is 0x0201 or 0x0203)
        {
            var index = IndexFromPoint(point);
            if (index >= 0)
            {
                Focus();
                var same = SelectedIndex == index;
                SelectedIndex = index;
                if (same) SelectedStationClicked?.Invoke();
                Invalidate();
            }
            m.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref m);
        if (!IsHandleCreated || _hidingScrollbar) return;
        _hidingScrollbar = true;
        try
        {
            // Native keyboard/wheel/LB_SETTOPINDEX still scroll, but no non-client column is reserved.
            if ((GetWindowLong(Handle, -16) & 0x00200000) != 0) ShowScrollBar(Handle, 1, false);
            if (m.Msg is 0x020A or 0x0115 or 0x0100 or 0x0197 or 0x0201) Invalidate();
        }
        finally { _hidingScrollbar = false; }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);
}
