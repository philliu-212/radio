using Radio.Core;
using System.Runtime.InteropServices;

namespace Radio.Windows.UI;

internal sealed class StationManagerDialog : Form
{
    private readonly StationLibrary _library;
    private readonly PlayerTheme _theme;
    private readonly StationListBox _available = new() { Name = "AvailableStations", AccessibleName = "可加入電台", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly StationListBox _mine = new() { Name = "MyStations", AccessibleName = "我的電台", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly StationSearchBox _leftSearch = new() { Name = "AvailableSearch", Outlined = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly StationSearchBox _rightSearch = new() { Name = "MySearch", Outlined = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly ToolTip _tips = new();
    private Font _frequencyFont = new("Segoe UI", 8.5f);
    private readonly PlayerButton _add;
    private readonly PlayerButton _remove;
    private readonly PlayerButton _addAll;
    private readonly PlayerButton _removeAll;
    private readonly TableLayoutPanel _arrows;
    public event Action? MembershipChanged;

    public StationManagerDialog(StationLibrary library, PlayerTheme theme, Font font)
    {
        _library = library; _theme = theme;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Name = "StationManager"; AccessibleName = "電台管理";
        Font = font; BackColor = theme.Canvas; ForeColor = theme.Text;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
        // Let the form alone own the entire rounded perimeter, with no outline.
        // Total content inset stays 16 DIP; only its ownership changes.
        ClientSize = new Size(690, 293); Padding = new Padding(14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(2), ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.Controls.Add(BuildSide(_available, _leftSearch, PlayerGlyph.ListPlus, "可加入電台"), 0, 0);
        layout.Controls.Add(BuildSide(_mine, _rightSearch, PlayerGlyph.BookmarkCheck, "我的電台"), 2, 0);
        var arrows = _arrows = new TableLayoutPanel { Anchor = AnchorStyles.None, Size = new Size(30, 154), ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
        arrows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 30, 8, 30, 18, 30, 8, 30 }) arrows.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        _add = Button("AddStation", PlayerGlyph.ChevronRight, "加入選取電台", framed: true);
        _remove = Button("RemoveStation", PlayerGlyph.ChevronLeft, "移除選取電台", framed: true);
        _addAll = Button("AddAllStations", PlayerGlyph.DoubleChevronRight, "加入全部搜尋結果", framed: true);
        _removeAll = Button("RemoveAllStations", PlayerGlyph.DoubleChevronLeft, "移除全部搜尋結果", framed: true);
        arrows.Controls.Add(_add, 0, 0); arrows.Controls.Add(_remove, 0, 2);
        arrows.Controls.Add(_addAll, 0, 4); arrows.Controls.Add(_removeAll, 0, 6);
        var transferColumn = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        transferColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transferColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); transferColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transferColumn.Controls.Add(arrows, 0, 1); layout.Controls.Add(transferColumn, 1, 0);
        var done = Button("DoneButton", PlayerGlyph.Check, "完成");
        done.Dock = DockStyle.None; done.Size = new Size(26, 26); done.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        done.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        layout.Controls.Add(done, 2, 1); Controls.Add(layout);
        _add.Click += (_, _) => Transfer(_available, true);
        _remove.Click += (_, _) => Transfer(_mine, false);
        _addAll.Click += (_, _) => Transfer(_available, true, all: true);
        _removeAll.Click += (_, _) => Transfer(_mine, false, all: true);
        _leftSearch.QueryChanged += RefreshLists; _rightSearch.QueryChanged += RefreshLists;
        _available.SelectedIndexChanged += (_, _) => UpdateButtons(); _mine.SelectedIndexChanged += (_, _) => UpdateButtons();
        EnableBackgroundDragging(this);
        RefreshLists();
    }

    // Only neutral surfaces initiate a move. Inputs, lists and buttons retain all
    // native mouse behavior, including text selection and scrollbar dragging.
    private static bool IsDragSurface(Control control) => control is Form or TableLayoutPanel || control.GetType() == typeof(Panel);
    private void EnableBackgroundDragging(Control control)
    {
        if (!IsDragSurface(control)) return;
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        };
        foreach (Control child in control.Controls) EnableBackgroundDragging(child);
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x0084 || m.Result != new IntPtr(1)) return;
        var point = new Point(unchecked((short)m.LParam.ToInt64()), unchecked((short)(m.LParam.ToInt64() >> 16)));
        Control surface = this;
        while (surface.GetChildAtPoint(surface.PointToClient(point)) is { } child) surface = child;
        if (IsDragSurface(surface)) m.Result = new IntPtr(2); // HTCAPTION: native window movement.
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private PlayerButton Button(string name, PlayerGlyph glyph, string tip, bool framed = false)
    {
        var button = new PlayerButton { Name = name, Glyph = glyph, Accent = true, Framed = framed, AccessibleName = tip, Dock = DockStyle.Fill, Margin = Padding.Empty };
        button.SetTheme(_theme); _tips.SetToolTip(button, tip); return button;
    }

    private Control BuildSide(StationListBox list, StationSearchBox search, PlayerGlyph glyph, string tip)
    {
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, RowCount = 3, ColumnCount = 1 };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 7)); side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Reserve the same trailing 12 DIP as the list's overlay scrollbar gutter.
        // The category icon aligns with the row's left edge; the pill ends at its right edge.
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 12, 0) };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 9)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = tip };
        icon.Paint += (_, e) => { var size = (int)Math.Round(16 * DeviceDpi / 96d); GlyphPainter.Draw(e.Graphics, glyph, new Rectangle(0, (icon.Height - size) / 2, size, size), _theme.Accent); };
        _tips.SetToolTip(icon, tip); row.Controls.Add(icon, 0, 0); row.Controls.Add(search, 2, 0);
        search.SetTheme(_theme);
        list.BackColor = _theme.Canvas; list.ForeColor = _theme.Text; list.ScrollbarColor = _theme.Track;
        list.SelectionMode = SelectionMode.MultiSimple;
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                list.BeginUpdate();
                for (var i = 0; i < list.Items.Count; i++) list.SetSelected(i, true);
                list.EndUpdate(); list.Invalidate(); e.SuppressKeyPress = true;
            }
        };
        list.DisplayMember = nameof(Station.DisplayName); list.DrawItem += DrawStation;
        side.Controls.Add(row, 0, 0); side.Controls.Add(list, 0, 2); return side;
    }

    private void Transfer(StationListBox source, bool add, bool all = false)
    {
        var selected = (all ? source.Items.Cast<Station>() : source.SelectedItems.Cast<Station>()).ToArray();
        if (selected.Length == 0) return;
        foreach (var station in selected)
            if (add) _library.Add(station.Id); else _library.Remove(station.Id);
        RefreshLists(); MembershipChanged?.Invoke();
    }
    private void RefreshLists()
    {
        Fill(_available, _library.Available.Where(s => StationLibrary.Matches(s, _leftSearch.Query)));
        Fill(_mine, _library.Mine.Where(s => StationLibrary.Matches(s, _rightSearch.Query)));
        UpdateButtons();
    }
    private static void Fill(StationListBox list, IEnumerable<Station> stations)
    {
        var selectedIds = list.SelectedItems.Cast<Station>().Select(station => station.Id).ToHashSet();
        var top = list.TopIndex;
        list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(stations.Cast<object>().ToArray());
        for (var i = 0; i < list.Items.Count; i++)
            if (selectedIds.Contains(((Station)list.Items[i]).Id)) list.SetSelected(i, true);
        if (list.Items.Count > 0) list.TopIndex = Math.Min(top, Math.Max(0, list.Items.Count - list.VisibleRows));
        list.EndUpdate(); list.Invalidate();
    }
    private void UpdateButtons()
    {
        _add.Enabled = _available.SelectedItems.Count > 0; _remove.Enabled = _mine.SelectedItems.Count > 0;
        _addAll.Enabled = _available.Items.Count > 0; _removeAll.Enabled = _mine.Items.Count > 0;
    }
    private void DrawStation(object? sender, DrawItemEventArgs e)
    {
        var list = (StationListBox)sender!;
        if (e.Index < 0 || list.Items[e.Index] is not Station station) return;
        var scale = DeviceDpi / 96d;
        int Dip(int value) => (int)Math.Round(value * scale);
        using var canvas = new SolidBrush(_theme.Canvas); e.Graphics.FillRectangle(canvas, e.Bounds);
        var bounds = new Rectangle(e.Bounds.X, e.Bounds.Y + Dip(3), list.ItemContentWidth, e.Bounds.Height - Dip(6));
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var path = RoundedShape.Create(bounds, Dip(5));
        using var brush = new SolidBrush(selected ? _theme.Selection : e.Index == list.HoverIndex ? _theme.Hover : _theme.Row);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(e.Graphics, station.Id == "bcc-ilike" ? "中廣流行網" : station.Name, Font,
            new Rectangle(bounds.X + Dip(7), bounds.Y, Math.Max(1, bounds.Width - Dip(48)), bounds.Height), _theme.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, station.FrequencyLabel, _frequencyFont,
            new Rectangle(bounds.Right - Dip(40), bounds.Y, Dip(34), bounds.Height), _theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateDpiPresentation();
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e); UpdateWindowShape();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e); UpdateDpiPresentation();
    }
    private void UpdateDpiPresentation()
    {
        int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96d);
        var rows = new[] { Dip(30), Dip(8), Dip(30), Dip(18), Dip(30), Dip(8), Dip(30) };
        // Round each row once and size the stack to that exact sum. Scaling the
        // total separately can clip the fourth button at fractional display DPI.
        for (var i = 0; i < rows.Length; i++) _arrows.RowStyles[i].Height = rows[i];
        _arrows.Size = new Size(Dip(30), rows.Sum());
        Padding = new Padding((int)Math.Round(14 * DeviceDpi / 96f));
        var previous = _frequencyFont;
        _frequencyFont = new Font("Segoe UI", 8.5f * DeviceDpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        previous.Dispose();
        UpdateWindowShape(); PerformLayout(); Invalidate(true);
    }
    private void UpdateWindowShape()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var path = RoundedShape.Create(ClientRectangle, (int)(12 * DeviceDpi / 96f));
        var previous = Region; Region = new Region(path); previous?.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tips.Dispose(); _frequencyFont.Dispose(); }
        base.Dispose(disposing);
    }
}
