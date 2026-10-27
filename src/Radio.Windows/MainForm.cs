using System.Diagnostics;
using Radio.Core;

namespace Radio.Windows;

internal sealed class MainForm : Form
{
    private readonly IStreamResolver _resolver = new OfficialStreamResolver();
    private readonly IAudioPlayer _player;
    private readonly ListBox _stations = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button _play = new() { Text = "播放", AutoSize = true };
    private readonly Button _stop = new() { Text = "停止", AutoSize = true };
    private readonly NotifyIcon _tray;
    private CancellationTokenSource? _playRequest;
    private bool _closing;
    private string? _playingName;

    public MainForm(IAudioPlayer player)
    {
        _player = player;
        Text = $"Radio — 輕量驗證版 ({(player is MpvAudioPlayer ? "mpv 純音訊" : "Windows 原生")})";
        Font = new Font("Microsoft JhengHei UI", 10);
        ClientSize = new Size(530, 410);
        MinimumSize = new Size(460, 370);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "選擇電台，再按播放；不會自動出聲。", AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        _stations.DataSource = StationCatalog.Load();
        _stations.DisplayMember = nameof(Station.DisplayName);
        layout.Controls.Add(_stations);
        var controls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 4) };
        var volume = new TrackBar { Minimum = 0, Maximum = 100, Value = 40, Width = 140, TickStyle = TickStyle.None };
        var mute = new CheckBox { Text = "靜音", AutoSize = true };
        controls.Controls.AddRange([_play, _stop, new Label { Text = "音量", AutoSize = true, Margin = new Padding(8) }, volume, mute]);
        layout.Controls.Add(controls);
        layout.Controls.Add(_status);
        var official = new LinkLabel { Text = "開啟所選電台官方收聽頁", AutoSize = true };
        layout.Controls.Add(official);
        Controls.Add(layout);
        _status.Text = "已停止。縮小視窗會收進系統匣；關閉視窗會退出。";

        var menu = new ContextMenuStrip();
        menu.Items.Add("顯示播放器", null, (_, _) => ShowPlayer());
        menu.Items.Add("停止", null, (_, _) => StopPlayback());
        menu.Items.Add("退出", null, (_, _) => Close());
        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Radio", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowPlayer();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        _play.Click += async (_, _) => await PlaySelectedAsync();
        _stations.DoubleClick += async (_, _) => await PlaySelectedAsync();
        _stop.Click += (_, _) => StopPlayback();
        volume.ValueChanged += (_, _) => _player.Volume = volume.Value / 100d;
        mute.CheckedChanged += (_, _) => _player.IsMuted = mute.Checked;
        official.LinkClicked += (_, _) =>
        {
            if (_stations.SelectedItem is Station station)
                Process.Start(new ProcessStartInfo(station.PageUrl.AbsoluteUri) { UseShellExecute = true });
        };
        _player.StatusChanged += UpdateStatus;
    }

    private async Task PlaySelectedAsync()
    {
        if (_stations.SelectedItem is not Station station) return;
        StopPlayback();
        var request = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        _playRequest = request;
        _playingName = station.Name;
        _status.Text = $"{station.Name}：取得官方直播來源中…";
        try
        {
            var stream = await _resolver.ResolveAsync(station, request.Token);
            if (_playRequest != request) return;
            await _player.PlayAsync(stream, request.Token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (_playRequest == request)
            {
                _player.Stop();
                _status.Text = "連接逾時，請稍後重試。";
            }
        }
        catch (Exception error)
        {
            if (_playRequest != request || _closing) return;
            _player.Stop();
            _status.Text = error is SourceResolutionException ? error.Message
                : $"播放引擎無法開啟此串流（0x{error.HResult:X8}）。可先用官方收聽頁。";
        }
        finally
        {
            if (_playRequest == request) _playRequest = null;
            request.Dispose();
        }
    }

    private void StopPlayback()
    {
        _playRequest?.Cancel();
        _playRequest = null;
        _playingName = null;
        _player.Stop();
    }

    private void UpdateStatus(PlaybackStatus status)
    {
        if (_closing || !IsHandleCreated) return;
        if (!InvokeRequired)
        {
            _status.Text = _playingName is { } name ? $"{name}：{status.Message}" : status.Message;
            return;
        }
        try
        {
            BeginInvoke(() => UpdateStatus(status));
        }
        catch (InvalidOperationException) { /* Window is closing. */ }
    }

    private void ShowPlayer()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing = true;
        StopPlayback();
        _player.StatusChanged -= UpdateStatus;
        _player.Dispose();
        _tray.Dispose();
        base.OnFormClosed(e);
    }
}
