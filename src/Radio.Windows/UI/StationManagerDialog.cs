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
    private readonly LanguageFilterButton _leftLanguage;
    private readonly LanguageFilterButton _rightLanguage;
    private readonly ContextMenuStrip _languageMenu;
    internal string AvailableStationsLanguageFilter => _leftLanguage.Language;
    internal string MyStationsLanguageFilter => _rightLanguage.Language;
    internal ContextMenuStrip LanguageMenu => _languageMenu;
    private Font _frequencyFont = new("Segoe UI", 8.5f);
    private readonly PlayerButton _add;
    private readonly PlayerButton _remove;
    private readonly PlayerButton _addAll;
    private readonly PlayerButton _removeAll;
    private readonly Dictionary<StationListBox, StationHoverAnimation> _hoverAnimations = new();
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 15 };
    public event Action? MembershipChanged;

    public StationManagerDialog(StationLibrary library, PlayerTheme theme, Font font)
    {
        // Keep the 96-DPI baseline pending until the complete control tree exists.
        // Otherwise ClientSize/Padding can consume initial DPI scaling before the
        // absolute search rows/columns have been attached to the form.
        SuspendLayout();
        _library = library; _theme = theme;
        _leftLanguage = new LanguageFilterButton(theme) { Name = "AvailableLanguageFilter" };
        _rightLanguage = new LanguageFilterButton(theme) { Name = "MyLanguageFilter" };
        _languageMenu = new ContextMenuStrip { Name = "LanguageMenu", Font = font, ShowImageMargin = false, ShowCheckMargin = false, Renderer = new LanguageMenuRenderer(theme) };
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Name = "StationManager"; AccessibleName = "電台管理";
        Font = font; BackColor = theme.Canvas; ForeColor = theme.Text;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
        // Let the form alone own the entire rounded perimeter, with no outline.
        // Keep the 16-DIP top/side inset, with a smaller 10-DIP bottom inset.
        ClientSize = new Size(580, 289); Padding = new Padding(14, 14, 14, 8);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(2), ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        _add = Button("AddStation", PlayerGlyph.ChevronRight, "加入選取電台");
        _remove = Button("RemoveStation", PlayerGlyph.ChevronLeft, "移除選取電台");
        _addAll = Button("AddAllStations", PlayerGlyph.DoubleChevronRight, "加入全部搜尋結果");
        _removeAll = Button("RemoveAllStations", PlayerGlyph.DoubleChevronLeft, "移除全部搜尋結果");
        layout.Controls.Add(BuildSide(_available, _leftSearch, _leftLanguage, PlayerGlyph.ListPlus, "可加入電台", _add, _addAll, true), 0, 0);
        layout.Controls.Add(BuildSide(_mine, _rightSearch, _rightLanguage, PlayerGlyph.BookmarkCheck, "我的電台", _remove, _removeAll, false), 2, 0);
        var done = Button("DoneButton", PlayerGlyph.Check, "完成");
        done.Dock = DockStyle.None; done.Size = new Size(26, 26); done.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        done.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        layout.Controls.Add(done, 2, 1); Controls.Add(layout);
        _add.Click += (_, _) => Transfer(_available, true);
        _remove.Click += (_, _) => Transfer(_mine, false);
        _addAll.Click += (_, _) => Transfer(_available, true, all: true);
        _removeAll.Click += (_, _) => Transfer(_mine, false, all: true);
        _leftSearch.QueryChanged += RefreshLists; _rightSearch.QueryChanged += RefreshLists;
        _leftLanguage.Click += (_, _) => ShowLanguageMenu(_leftLanguage);
        _rightLanguage.Click += (_, _) => ShowLanguageMenu(_rightLanguage);
        UpdateLanguageButton(_leftLanguage); UpdateLanguageButton(_rightLanguage);
        _library.OrderChanged += RefreshLists;
        _available.SelectedIndexChanged += (_, _) => UpdateButtons(); _mine.SelectedIndexChanged += (_, _) => UpdateButtons();
        _hoverTimer.Tick += (_, _) =>
        {
            _available.Invalidate(); _mine.Invalidate();
            if (!_hoverAnimations.Values.Any(animation => animation.IsAnimating(Environment.TickCount64))) _hoverTimer.Stop();
        };
        EnableBackgroundDragging(this);
        RefreshLists();
        ResumeLayout(true);
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

    private PlayerButton Button(string name, PlayerGlyph glyph, string tip)
    {
        var button = new PlayerButton { Name = name, Glyph = glyph, Accent = true, AccessibleName = tip, Dock = DockStyle.Fill, Margin = Padding.Empty };
        button.SetTheme(_theme);
        if (glyph == PlayerGlyph.Check) _tips.SetToolTip(button, tip);
        return button;
    }

    private Control BuildSide(StationListBox list, StationSearchBox search, LanguageFilterButton language, PlayerGlyph glyph, string tip,
        PlayerButton single, PlayerButton batch, bool add)
    {
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, RowCount = 5, ColumnCount = 1 };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 7)); side.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Reserve the same trailing 12 DIP as the list's overlay scrollbar gutter.
        // Both icons are outside the complete search pill; globe ends at the row's right edge.
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0, 0, 12, 0) };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 9)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 6)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        var icon = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = tip };
        icon.Paint += (_, e) => { var size = (int)Math.Round(16 * DeviceDpi / 96d); GlyphPainter.Draw(e.Graphics, glyph, new Rectangle(0, (icon.Height - size) / 2, size, size), _theme.Accent); };
        _tips.SetToolTip(icon, tip); row.Controls.Add(icon, 0, 0); row.Controls.Add(search, 2, 0);
        row.Controls.Add(language, 4, 0);
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
        var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        single.Dock = batch.Dock = DockStyle.None;
        single.Visible = false;
        list.Controls.Add(single); footer.Controls.Add(batch);
        var animation = new StationHoverAnimation();
        _hoverAnimations.Add(list, animation);
        string? arrowStationId = null;
        single.PaintSurface = graphics =>
        {
            // Native child buttons are opaque. Repaint the row beneath this hit area
            // so an almost-transparent arrow cannot erase text that is still moving.
            var index = list.IndexFromPoint(0, single.Top + single.Height / 2);
            if (index < 0) return;
            var state = graphics.Save();
            graphics.SetClip(single.ClientRectangle, System.Drawing.Drawing2D.CombineMode.Intersect);
            graphics.TranslateTransform(-single.Left, -single.Top);
            DrawStation(list, new DrawItemEventArgs(graphics, Font, list.GetItemRectangle(index), index,
                list.GetSelected(index) ? DrawItemState.Selected : DrawItemState.None));
            graphics.Restore(state);
        };
        void PositionArrows()
        {
            int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96d);
            // Keep the footer just below the last visible row even for short results.
            var listHeight = Math.Min(5, list.Items.Count) * Dip(37);
            if (side.RowStyles[2].Height != listHeight) side.RowStyles[2].Height = listHeight;
            single.Size = new Size(Dip(12), Dip(15)); batch.Size = new Size(Dip(17), Dip(15));
            // For rightward movement the rear chevron is the left one; for leftward
            // movement it is the right one. Its viewport aligns with the row arrow.
            var x = add ? list.ItemContentWidth - Dip(19) : Dip(7);
            batch.Location = new Point(add ? x : x - Dip(5), 0);
            var point = list.PointToClient(Cursor.Position);
            var index = list.ClientRectangle.Contains(point) && point.X < list.ItemContentWidth ? list.IndexFromPoint(point) : -1;
            if (index >= 0 && list.GetItemRectangle(index).Bottom > list.ClientSize.Height) index = -1;
            var hoveredId = index >= 0 ? ((Station)list.Items[index]).Id : null;
            var now = Environment.TickCount64;
            if (animation.SetHovered(hoveredId, now))
            {
                _hoverTimer.Start();
                // Hover owns the active row; moving the pointer must not transfer it.
                list.ClearSelected();
                if (index >= 0) list.SetSelected(index, true);
            }
            if (hoveredId is not null) arrowStationId = hoveredId;
            var arrowIndex = list.Items.Cast<Station>().Select((station, i) => (station, i))
                .Where(item => item.station.Id == arrowStationId).Select(item => item.i).DefaultIfEmpty(-1).First();
            var progress = arrowStationId is null ? 0 : animation.Progress(arrowStationId, now);
            // Keep the outgoing arrow at its fixed row coordinates until its fade finishes.
            if (arrowIndex < 0 || (hoveredId is null && progress == 0)) { single.Visible = false; return; }
            var bounds = list.GetItemRectangle(arrowIndex);
            if (bounds.Top < 0 || bounds.Bottom > list.ClientSize.Height) { single.Visible = false; return; }
            single.Location = new Point(x, bounds.Top + (bounds.Height - single.Height) / 2);
            single.BackColor = list.GetSelected(arrowIndex) ? _theme.Selection : StationHoverAnimation.Blend(_theme.Row, _theme.Hover, progress);
            single.Invalidate();
            single.Visible = true; single.BringToFront();
        }
        list.MouseMove += (_, _) => PositionArrows();
        single.Click += (_, _) =>
        {
            var index = list.IndexFromPoint(single.Left + single.Width / 2, single.Top + single.Height / 2);
            if (index < 0) return;
            list.ClearSelected(); list.SetSelected(index, true);
        };
        list.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.X >= list.ItemContentWidth) return;
            var index = list.IndexFromPoint(e.Location);
            if (index < 0 || !list.GetItemRectangle(index).Contains(e.Location)) return;
            // Native MultiSimple may toggle the hovered row off on mouse-down.
            // A row click always moves that one row, never another selection.
            list.ClearSelected(); list.SetSelected(index, true);
            Transfer(list, add);
        };
        list.MouseLeave += (_, _) => PositionArrows();
        list.Invalidated += (_, _) => PositionArrows();
        single.MouseLeave += (_, _) => { list.RefreshPointerHover(); PositionArrows(); list.Invalidate(); };
        footer.SizeChanged += (_, _) => PositionArrows();
        side.Controls.Add(row, 0, 0); side.Controls.Add(list, 0, 2); side.Controls.Add(footer, 0, 3); return side;
    }

    private void Transfer(StationListBox source, bool add, bool all = false)
    {
        var selected = (all ? source.Items.Cast<Station>() : source.SelectedItems.Cast<Station>()).ToArray();
        if (selected.Length == 0) return;
        foreach (var station in selected)
            if (add) _library.Add(station.Id); else _library.Remove(station.Id);
        // Clear only destination conditions that would hide at least one moved station.
        var search = add ? _rightSearch : _leftSearch;
        var language = add ? _rightLanguage : _leftLanguage;
        if (selected.Any(station => !StationLibrary.MatchesLanguage(station, language.Language)))
        { language.Language = ""; UpdateLanguageButton(language); }
        if (selected.Any(station => !StationLibrary.Matches(station, search.Query))) search.Query = "";
        RefreshLists(); MembershipChanged?.Invoke();
    }
    private void RefreshLists()
    {
        Fill(_available, _library.Available.Where(s => StationLibrary.Matches(s, _leftSearch.Query) && StationLibrary.MatchesLanguage(s, AvailableStationsLanguageFilter)));
        Fill(_mine, _library.Mine.Where(s => StationLibrary.Matches(s, _rightSearch.Query) && StationLibrary.MatchesLanguage(s, MyStationsLanguageFilter)));
        UpdateButtons();
    }
    private void UpdateLanguageButton(LanguageFilterButton button)
    {
        button.AccessibleName = $"語言：{button.LanguageLabel}";
        _tips.SetToolTip(button, button.AccessibleName); button.Invalidate();
    }
    private void ShowLanguageMenu(LanguageFilterButton button)
    {
        _languageMenu.Close();
        foreach (var item in _languageMenu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        _languageMenu.Items.Clear();
        var scale = DeviceDpi / 96f;
        _languageMenu.MinimumSize = new Size((int)(150 * scale), 0);
        _languageMenu.MaximumSize = new Size((int)(150 * scale), 0);
        foreach (var (code, label) in LanguageFilterButton.Options)
        {
            var item = new ToolStripMenuItem(label) { Name = $"Language_{code}", Checked = button.Language == code,
                AutoSize = false, Size = new Size((int)(146 * scale), (int)(28 * scale)) };
            item.Click += (_, _) =>
            {
                button.Language = code; UpdateLanguageButton(button);
                RefreshLists(); _languageMenu.Close();
            };
            _languageMenu.Items.Add(item);
        }
        _languageMenu.Show(button, new Point(0, button.Height));
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
        var progress = _hoverAnimations[list].Progress(station.Id, Environment.TickCount64);
        using var brush = new SolidBrush(selected && !_hoverAnimations[list].IsHovered(station.Id)
            ? _theme.Selection : StationHoverAnimation.Blend(_theme.Row, _theme.Hover, progress));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.FillPath(brush, path);
        var extra = 18 * progress;
        var leading = list == _mine ? 6 + extra : 7;
        var trailing = list == _available ? 6 + extra : 6;
        int Offset(double value) => (int)Math.Round(value * scale);
        TextRenderer.DrawText(e.Graphics, station.Id == "bcc-ilike" ? "中廣流行網" : station.Name, Font,
            new Rectangle(bounds.X + Offset(leading), bounds.Y, Math.Max(1, bounds.Width - Offset(leading + trailing + 35)), bounds.Height), _theme.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
            TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
        TextRenderer.DrawText(e.Graphics, station.FrequencyLabel, _frequencyFont,
            new Rectangle(bounds.Right - Offset(trailing + 34), bounds.Y, Dip(34), bounds.Height), _theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
        if (progress > 0)
        {
            var add = list == _available;
            var arrow = new Rectangle(add ? bounds.Right - Dip(19) : bounds.Left + Dip(7),
                e.Bounds.Top + (e.Bounds.Height - Dip(15)) / 2, Dip(12), Dip(15));
            GlyphPainter.Draw(e.Graphics, add ? PlayerGlyph.ChevronRight : PlayerGlyph.ChevronLeft,
                arrow, Color.FromArgb((int)Math.Round(255 * progress), _theme.Accent));
        }
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
        var inset = (int)Math.Round(14 * DeviceDpi / 96f);
        Padding = new Padding(inset, inset, inset, (int)Math.Round(8 * DeviceDpi / 96f));
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
        if (disposing) { _hoverTimer.Dispose(); _library.OrderChanged -= RefreshLists; _languageMenu.Dispose(); _tips.Dispose(); _frequencyFont.Dispose(); }
        base.Dispose(disposing);
    }
}

// Presentation only: each row can finish fading out while another starts fading in.
internal sealed class StationHoverAnimation
{
    internal const double DurationMilliseconds = 120;
    private readonly Dictionary<string, (double From, double To, double Started)> _rows = new();
    private string? _hoveredId;

    internal bool SetHovered(string? id, double now)
    {
        if (id == _hoveredId) return false;
        if (_hoveredId is not null) _rows[_hoveredId] = (Progress(_hoveredId, now), 0, now);
        if (id is not null) _rows[id] = (Progress(id, now), 1, now);
        _hoveredId = id;
        return true;
    }

    internal double Progress(string id, double now)
    {
        if (!_rows.TryGetValue(id, out var row)) return 0;
        var t = Math.Clamp((now - row.Started) / DurationMilliseconds, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3); // Cubic ease-out, continuous on reversal.
        return row.From + (row.To - row.From) * eased;
    }

    internal bool IsHovered(string id) => id == _hoveredId;

    internal bool IsAnimating(double now) => _rows.Values.Any(row => row.From != row.To && now - row.Started < DurationMilliseconds);

    internal static Color Blend(Color from, Color to, double progress) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * progress),
        (int)Math.Round(from.G + (to.G - from.G) * progress),
        (int)Math.Round(from.B + (to.B - from.B) * progress));
}
