using System.Diagnostics;
using System.Runtime.InteropServices;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows;

internal sealed class MainForm : Form
{
    private readonly IStreamResolver _resolver;
    private readonly IAudioPlayer _player;
    private readonly IStationMetadataProvider _metadataProvider;
    private readonly StationListBox _stations = new() { Name = "StationList", AccessibleName = "電台清單", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly PlayerButton _transport = new() { Name = "PlayButton", Primary = true, Glyph = PlayerGlyph.Play, AccessibleName = "播放", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly PlayerButton _themeButton = new() { Name = "ThemeToggle", Glyph = PlayerGlyph.Theme, AccessibleName = "切換深淺色模式", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly PlayerButton _minimize = new() { Name = "MinimizeButton", Glyph = PlayerGlyph.Minimize, AccessibleName = "最小化", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly PlayerButton _close = new() { Name = "CloseButton", Glyph = PlayerGlyph.Close, AccessibleName = "關閉", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly PlayerButton _official = new() { Name = "OfficialLink", Glyph = PlayerGlyph.External, AccessibleName = "電台官網", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _status = TextLabel("PlaybackStatus");
    private readonly Label _title = TextLabel("NowPlayingTitle");
    private readonly Label _frequency = TextLabel("Frequency");
    private readonly Label _fm = TextLabel("FrequencyUnit");
    private readonly Label _metadataTitle = TextLabel("MetadataTitle");
    private readonly Label _metadataPerson = TextLabel("MetadataPerson");
    private readonly MuteButton _mute = new() { Name = "Mute", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly VolumeSlider _volume = new() { Name = "Volume", AccessibleName = "音量", Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly PlayerCard _card = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly ToolTip _tips = new() { ShowAlways = true };
    private readonly List<Font> _fonts = [];
    private readonly Dictionary<(float Size, FontStyle Style, string Family, int Dpi), Font> _fontCache = [];
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayTransport;
    private readonly bool _persistPreferences;
    private Font _frequencyFont;
    private CancellationTokenSource? _playRequest;
    private CancellationTokenSource? _metadataRequest;
    private TaskCompletionSource? _started;
    private Station? _targetStation;
    private Station? _playingStation;
    private StationMetadata? _metadata;
    private PlaybackStatus _latestStatus = new(AudioState.Stopped, "選擇電台後按播放，開始收聽。");
    private PlayerTheme _theme;
    private bool _darkMode;
    private bool _active;
    private bool _hasStarted;
    private bool _closing;
    private bool _resourcesDisposed;
    private bool _uiSuspended;
    private bool _uiDirty;
    private bool _metadataEnabled = true;
    private bool _suppressSelectionPlayback;
    private string? _lastSelectedStationId;
    internal Task SelectionPlaybackTask { get; private set; } = Task.CompletedTask;

    internal int PresentationUpdates { get; private set; }
    internal bool IsBackgroundMode => _uiSuspended;
    internal PlaybackStatus LatestStatus => _latestStatus;
    internal bool IsDarkMode => _darkMode;
    internal string? PlayingStationId => _playingStation?.Id;
    internal string? SelectedStationId => (_stations.SelectedItem as Station)?.Id;
    internal string DisplayedStation => _title.Text;
    internal bool MetadataRunning => _metadataRequest is not null;

    public MainForm(IAudioPlayer player, bool persistPreferences = true, bool? darkOverride = null,
        IStreamResolver? resolver = null, IStationMetadataProvider? metadataProvider = null)
    {
        _player = player;
        _resolver = resolver ?? new OfficialStreamResolver();
        _metadataProvider = metadataProvider ?? new OfficialMetadataProvider();
        _persistPreferences = persistPreferences;
        _darkMode = darkOverride ?? ThemeSettings.LoadDarkMode();
        _theme = _darkMode ? PlayerTheme.Dark : PlayerTheme.Light;
        SuspendLayout();
        Text = "Radio Mini";
        Font = OwnFont(10);
        _frequencyFont = OwnFont(8.5f, family: "Segoe UI");
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(554, 233);
        MinimumSize = new Size(554, 233);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(9, 5, 9, 9), RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildHeader(), 0, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _stations.DataSource = StationCatalog.Load();
        _stations.DisplayMember = nameof(Station.DisplayName);
        _lastSelectedStationId = (_stations.SelectedItem as Station)?.Id;
        body.Controls.Add(_stations, 0, 0);
        // Only widen the gap: item -> 2 DIP -> 2-DIP scrollbar -> 8 DIP -> status card.
        body.SetColumnSpan(_stations, 2);
        body.Controls.Add(BuildPlayerCard(), 2, 0);
        layout.Controls.Add(body, 0, 1);
        Controls.Add(layout);

        var menu = new ContextMenuStrip();
        menu.Items.Add("開啟播放器", null, (_, _) => ShowPlayer());
        _trayTransport = new ToolStripMenuItem("播放", null, async (_, _) => { if (_active) StopPlayback(); else await PlaySelectedAsync(); });
        menu.Items.Add(_trayTransport);
        var metadataOption = new ToolStripMenuItem("顯示節目／歌曲資訊") { Checked = true, CheckOnClick = true };
        metadataOption.CheckedChanged += (_, _) =>
        {
            _metadataEnabled = metadataOption.Checked;
            CancelMetadata();
            RefreshPresentation();
            StartMetadata();
        };
        menu.Items.Add(metadataOption);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Close());
        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Radio Mini", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowPlayer();
        _transport.Click += async (_, _) => await TogglePlaybackAsync();
        _stations.SelectedIndexChanged += (_, _) =>
        {
            var changed = SelectedStationId != _lastSelectedStationId;
            _lastSelectedStationId = SelectedStationId;
            RefreshPresentation();
            if (changed && !_suppressSelectionPlayback && Visible && !_closing && _stations.SelectedItem is Station)
                SelectionPlaybackTask = PlaySelectedAsync();
        };
        _stations.SelectedStationClicked += () =>
        {
            if (!_active && !_closing) SelectionPlaybackTask = PlaySelectedAsync();
        };
        _stations.DrawItem += DrawStation;
        _stations.KeyDown += async (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space) { e.SuppressKeyPress = true; await PlaySelectedAsync(); }
        };
        if (_persistPreferences && ThemeSettings.Load().Volume is { } savedVolume) _player.Volume = savedVolume / 100d;
        _volume.Value = (int)Math.Round(_player.Volume * 100);
        _mute.Checked = _player.IsMuted;
        _volume.ValueChanged += (_, _) => { _player.Volume = _volume.Value / 100d; _tips.SetToolTip(_volume, $"音量 {_volume.Value}%"); };
        _volume.InteractionCompleted += SavePreferences;
        _mute.CheckedChanged += (_, _) => { _player.IsMuted = _mute.Checked; _mute.Invalidate(); };
        _themeButton.Click += (_, _) => ToggleTheme();
        _minimize.Click += (_, _) => { WindowState = FormWindowState.Minimized; Hide(); };
        _close.Click += (_, _) => Close();
        _official.Click += (_, _) =>
        {
            if (DisplayedSource is not { } station) return;
            try { Process.Start(new ProcessStartInfo(OfficialPage(station).AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            { _tips.Show("無法開啟瀏覽器，請稍後再試。", _official, 3000); }
        };
        _player.StatusChanged += UpdateStatus;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) SuspendPresentation();
            else if (Visible) ResumePresentation();
        };
        VisibleChanged += (_, _) =>
        {
            if (Visible && WindowState != FormWindowState.Minimized) ResumePresentation();
            else if (!Visible && !_closing) { _uiSuspended = true; CancelMetadata(); }
        };
        _tips.SetToolTip(_themeButton, "切換深色／淺色模式");
        _tips.SetToolTip(_minimize, "縮到系統匣，繼續播放");
        _tips.SetToolTip(_close, "退出 Radio Mini");
        _tips.SetToolTip(_official, "開啟電台官方網頁");
        _tips.SetToolTip(_mute, "靜音（保留音量）");
        ApplyTheme();
        ResumeLayout(true);
    }

    private static Label TextLabel(string name) => new() { Name = name, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, UseMnemonic = false };
    private Font OwnFont(float size, FontStyle style = FontStyle.Regular, string family = "Microsoft JhengHei UI")
    {
        var key = (size, style, family, DeviceDpi);
        if (_fontCache.TryGetValue(key, out var cached)) return cached;
        var font = new Font(family, size * DeviceDpi / 72, style, GraphicsUnit.Pixel);
        _fonts.Add(font); _fontCache.Add(key, font); return font;
    }
    private void ApplyDpiTypography()
    {
        // Explicit label fonts and owner-drawn text share the same DIP baseline. Do not rely on
        // inherited Font scaling for some labels while leaving explicit fonts at the old DPI.
        SuspendLayout();
        Font = OwnFont(10);
        _frequencyFont = OwnFont(8.5f, family: "Segoe UI");
        Controls.Find("AppTitle", true).Single().Font = OwnFont(11, family: "Segoe UI");
        _status.Font = OwnFont(9); _title.Font = OwnFont(11, FontStyle.Bold);
        _frequency.Font = OwnFont(16, FontStyle.Bold, "Segoe UI"); _fm.Font = OwnFont(9, family: "Segoe UI");
        _metadataTitle.Font = OwnFont(10.5f); _metadataPerson.Font = OwnFont(9.5f);
        ResumeLayout(true);
    }
    private Station? DisplayedSource => _playingStation ?? _targetStation ?? _stations.SelectedItem as Station;
    private static string StationName(Station station) => station.Id == "bcc-ilike" ? "中廣流行網" : station.Name;
    private static Uri OfficialPage(Station station) => station.Id == "kiss-999"
        ? new Uri("https://www.kiss.com.tw/") : station.PageUrl;

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 5, RowCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 17));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 25));
        var logo = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        logo.Paint += (_, e) => GlyphPainter.Draw(e.Graphics, PlayerGlyph.Radio,
            new Rectangle(0, (logo.Height - ScaleDip(13)) / 2, ScaleDip(13), ScaleDip(13)), _theme.Accent);
        var brand = TextLabel("AppTitle"); brand.Text = "Radio Mini"; brand.Font = OwnFont(11, family: "Segoe UI");
        header.Controls.Add(logo, 0, 0); header.Controls.Add(brand, 1, 0);
        header.Controls.Add(_themeButton, 2, 0); header.Controls.Add(_minimize, 3, 0); header.Controls.Add(_close, 4, 0);
        header.MouseDown += DragWindow; logo.MouseDown += DragWindow; brand.MouseDown += DragWindow;
        return header;
    }

    private Control BuildPlayerCard()
    {
        _card.Padding = new Padding(9, 8, 9, 8);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, RowCount = 7, ColumnCount = 1 };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        statusRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 23));
        _status.Font = OwnFont(9);
        statusRow.Controls.Add(_status, 0, 0); statusRow.Controls.Add(_official, 1, 0);
        table.Controls.Add(statusRow, 0, 0);
        _title.Font = OwnFont(11, FontStyle.Bold); table.Controls.Add(_title, 0, 1);
        var frequencyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        frequencyRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frequencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); frequencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _frequency.AutoSize = true; _frequency.Font = OwnFont(16, FontStyle.Bold, "Segoe UI");
        _fm.Text = " FM"; _fm.Font = OwnFont(9, family: "Segoe UI");
        frequencyRow.Controls.Add(_frequency, 0, 0); frequencyRow.Controls.Add(_fm, 1, 0);
        table.Controls.Add(frequencyRow, 0, 2);
        _metadataTitle.Font = OwnFont(10.5f); _metadataPerson.Font = OwnFont(9.5f);
        table.Controls.Add(_metadataTitle, 0, 4); table.Controls.Add(_metadataPerson, 0, 5);
        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty };
        transport.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 5));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 29));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.Controls.Add(_transport, 0, 0); transport.Controls.Add(_mute, 2, 0); transport.Controls.Add(_volume, 3, 0);
        table.Controls.Add(transport, 0, 6);
        _card.Controls.Add(table);
        return _card;
    }

    internal void ToggleTheme()
    {
        _darkMode = !_darkMode; _theme = _darkMode ? PlayerTheme.Dark : PlayerTheme.Light;
        ApplyTheme();
        SavePreferences();
    }
    private void SavePreferences()
    {
        if (_persistPreferences) ThemeSettings.Save(_darkMode, _volume.Value);
    }
    private void ApplyTheme()
    {
        BackColor = _theme.Canvas; ForeColor = _theme.Text;
        _card.BackColor = _theme.Surface;
        _stations.BackColor = _theme.Canvas; _stations.ForeColor = _theme.Text;
        _stations.ScrollbarColor = _theme.Track;
        _title.ForeColor = _theme.Text; _frequency.ForeColor = _theme.Text; _fm.ForeColor = _theme.Muted;
        _metadataTitle.ForeColor = _theme.Text; _metadataPerson.ForeColor = _theme.Muted;
        _mute.Theme = _theme; _mute.BackColor = _theme.Surface;
        _volume.Theme = _theme; _volume.BackColor = _theme.Surface;
        foreach (var button in new[] { _transport, _themeButton, _minimize, _close, _official }) button.SetTheme(_theme);
        RefreshPresentation(); Invalidate(true);
    }

    private void DrawStation(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _stations.Items[e.Index] is not Station station) return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var playing = _playingStation?.Id == station.Id && _active;
        using (var canvas = new SolidBrush(_theme.Canvas)) e.Graphics.FillRectangle(canvas, e.Bounds);
        var bounds = new Rectangle(e.Bounds.X, e.Bounds.Y + ScaleDip(3), _stations.ItemContentWidth, e.Bounds.Height - ScaleDip(6));
        var background = selected ? _theme.Selection : playing ? _theme.Playing : e.Index == _stations.HoverIndex ? _theme.Hover : _theme.Row;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = RoundedShape.Create(bounds, ScaleDip(5));
        using var brush = new SolidBrush(background);
        e.Graphics.FillPath(brush, path);
        if (playing) GlyphPainter.Draw(e.Graphics, PlayerGlyph.Speaker, new Rectangle(bounds.X + ScaleDip(6), bounds.Y + (bounds.Height - ScaleDip(13)) / 2, ScaleDip(13), ScaleDip(13)), _theme.Accent);
        var nameBounds = new Rectangle(bounds.X + ScaleDip(23), bounds.Y, bounds.Width - ScaleDip(61), bounds.Height);
        TextRenderer.DrawText(e.Graphics, StationName(station), Font, nameBounds, _theme.Text,
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, station.Frequency, _frequencyFont,
            new Rectangle(bounds.Right - ScaleDip(40), bounds.Y, ScaleDip(34), bounds.Height), _theme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (selected && _stations.Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -2, -2), _theme.Accent, background);
    }

    private Task TogglePlaybackAsync() => _active && _targetStation?.Id == SelectedStationId
        ? StopFromButtonAsync() : PlaySelectedAsync();
    private Task StopFromButtonAsync() { StopPlayback(); return Task.CompletedTask; }

    private async Task PlaySelectedAsync()
    {
        if (_stations.SelectedItem is not Station station || _closing) return;
        StopPlayback();
        _hasStarted = true; _active = true; _targetStation = station;
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _started = started; _playRequest = request;
        UpdateStatus(new PlaybackStatus(AudioState.Opening, "正在取得官方直播來源…"));
        try
        {
            var stream = await _resolver.ResolveAsync(station, request.Token);
            if (_playRequest != request || _closing) return;
            await _player.PlayAsync(stream, request.Token);
            await started.Task.WaitAsync(request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (_playRequest == request && !_closing) FailPlayback("連線逾時，請按播放重試。");
        }
        catch (Exception error)
        {
            if (_playRequest != request || _closing) return;
            FailPlayback(error is SourceResolutionException ? error.Message : "無法播放此直播，請重試或開啟電台官網。");
        }
        finally
        {
            if (_playRequest == request) { _playRequest = null; _started = null; }
            request.Dispose();
        }
    }

    private void FailPlayback(string message)
    {
        _active = false; _playingStation = null; CancelMetadata();
        _player.Stop();
        UpdateStatus(new PlaybackStatus(AudioState.Failed, message));
    }
    private void StopPlayback()
    {
        var request = _playRequest; _playRequest = null; _started = null;
        request?.Cancel();
        _active = false; _playingStation = null; _targetStation = null;
        CancelMetadata();
        _player.Stop();
        UpdateStatus(new PlaybackStatus(AudioState.Stopped, "已停止；再次播放將連接當下直播。"));
    }

    private void UpdateStatus(PlaybackStatus status)
    {
        if (_closing) return;
        if (IsHandleCreated && InvokeRequired)
        {
            try { BeginInvoke(() => UpdateStatus(status)); } catch (InvalidOperationException) { }
            return;
        }
        // Adapters emit Stopped while replacing their internal source; that is not user Stop.
        if (status.State == AudioState.Stopped && _playRequest is not null && _active) return;
        if (status.State is AudioState.Playing or AudioState.Buffering && !_active) return;
        _latestStatus = status;
        if (status.State == AudioState.Playing)
        {
            _playingStation = _targetStation;
            _started?.TrySetResult();
            StartMetadata();
        }
        else if (status.State is AudioState.Failed or AudioState.Stopped)
        {
            _active = false; _playingStation = null; CancelMetadata(); _started?.TrySetResult();
        }
        _trayTransport.Text = _active ? "停止" : "播放";
        _tray.Text = _active && _targetStation is { } station ? $"Radio Mini · {StationName(station)}" : "Radio Mini";
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (_closing || _uiSuspended) { _uiDirty = true; return; }
        PresentationUpdates++; _uiDirty = false;
        var station = DisplayedSource;
        _title.Text = station is null ? "選擇電台" : StationName(station);
        _frequency.Text = station?.Frequency ?? "—";
        _tips.SetToolTip(_title, _title.Text);
        _status.Text = _latestStatus.State switch
        {
            AudioState.Playing => "● 播放中", AudioState.Opening => "● 連線中", AudioState.Buffering => "● 緩衝中",
            AudioState.Failed => "● 播放失敗，請重試", _ => _hasStarted ? "● 已停止" : "● 待機"
        };
        _status.ForeColor = _latestStatus.State == AudioState.Playing ? _theme.Accent
            : _latestStatus.State == AudioState.Failed ? _theme.Warning : _theme.Muted;
        _tips.SetToolTip(_status, _latestStatus.Message);
        var stop = _active && _targetStation?.Id == SelectedStationId;
        _transport.Glyph = stop ? PlayerGlyph.Stop : PlayerGlyph.Play;
        _transport.AccessibleName = stop ? "停止" : "播放";
        _tips.SetToolTip(_transport, stop ? "停止直播" : "播放選取的電台");
        _transport.Invalidate();
        var data = _metadata?.ExpiresAt > DateTimeOffset.UtcNow ? _metadata : null;
        SetMetadataLabel(_metadataTitle, data?.FirstLine);
        SetMetadataLabel(_metadataPerson, data?.SecondLine);
        _stations.Invalidate();
    }
    private void SetMetadataLabel(Label label, string? value)
    {
        label.Text = value ?? ""; label.Visible = value is not null; _tips.SetToolTip(label, value);
    }

    private void StartMetadata()
    {
        if (_closing || _uiSuspended || !Visible || !_metadataEnabled || !_active || _playingStation is not { } station ||
            !_metadataProvider.Supports(station.Id) || _metadataRequest is not null) return;
        var request = new CancellationTokenSource();
        _metadataRequest = request;
        _ = PollMetadataAsync(station.Id, request);
    }
    private async Task PollMetadataAsync(string stationId, CancellationTokenSource request)
    {
        try
        {
            while (!request.IsCancellationRequested)
            {
                StationMetadata? data = null;
                try { data = await _metadataProvider.GetAsync(stationId, request.Token); }
                catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException or System.Text.RegularExpressions.RegexMatchTimeoutException) { }
                if (request.IsCancellationRequested || _metadataRequest != request || _playingStation?.Id != stationId || _closing || _uiSuspended) return;
                _metadata = data?.ExpiresAt > DateTimeOffset.UtcNow ? data : null;
                RefreshPresentation();
                // Wake at expiry (not just poll time) to avoid showing stale data between refreshes.
                var delay = _metadata is null ? TimeSpan.FromSeconds(60) : _metadata.ExpiresAt - DateTimeOffset.UtcNow;
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60)), request.Token);
                _metadata = null; RefreshPresentation();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (_metadataRequest == request) _metadataRequest = null;
            request.Dispose();
        }
    }
    private void CancelMetadata()
    {
        var request = _metadataRequest; _metadataRequest = null; _metadata = null;
        request?.Cancel();
    }
    private void SuspendPresentation()
    {
        // Native taskbar minimization must retain the taskbar button for restoration.
        // Only the explicit "minimize to tray" button hides the window.
        _uiSuspended = true; CancelMetadata();
    }
    private void ResumePresentation()
    {
        var refresh = _uiSuspended || _uiDirty;
        _uiSuspended = false;
        if (refresh) RefreshPresentation();
        StartMetadata();
    }
    internal void ShowPlayer()
    {
        Show(); WindowState = FormWindowState.Normal; ResumePresentation(); Activate();
    }
    internal void SelectForProbe(string id)
    {
        _suppressSelectionPlayback = true;
        try
        {
            for (var i = 0; i < _stations.Items.Count; i++)
                if (_stations.Items[i] is Station station && station.Id == id) { _stations.SelectedIndex = i; return; }
            throw new ArgumentException("Unknown station.", nameof(id));
        }
        finally { _suppressSelectionPlayback = false; }
    }
    internal Task PlayForProbeAsync(string id) { SelectForProbe(id); return PlaySelectedAsync(); }

    private int ScaleDip(int value) => (int)Math.Round(value * DeviceDpi / 96d);
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep native minimize/system-menu semantics even with a custom, captionless frame.
            parameters.Style |= 0x00020000 | 0x00080000;
            return parameters;
        }
    }
    private void UpdateWindowShape()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0 || WindowState == FormWindowState.Minimized) return;
        using var path = RoundedShape.Create(ClientRectangle, ScaleDip(13));
        var previous = Region; Region = new Region(path); previous?.Dispose();
    }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateWindowShape(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e); ApplyDpiTypography(); UpdateWindowShape();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); ApplyDpiTypography(); UpdateWindowShape(); RefreshPresentation();
    }
    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture(); SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ReleaseResources(); base.OnFormClosed(e);
    }
    private void ReleaseResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true; _closing = true;
        SavePreferences();
        _playRequest?.Cancel(); _playRequest = null;
        CancelMetadata(); _player.StatusChanged -= UpdateStatus;
        _player.Dispose(); _metadataProvider.Dispose();
        _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _tips.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) ReleaseResources();
        base.Dispose(disposing);
        if (disposing) foreach (var font in _fonts) font.Dispose();
    }
}
