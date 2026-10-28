using System.Runtime.InteropServices;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class UiChecks
{
    public static async Task RunAsync()
    {
        Check(MediaEngineAudioPlayer.PlaybackStateForEvent(1005) is null && MediaEngineAudioPlayer.PlaybackStateForEvent(1006) is null &&
            MediaEngineAudioPlayer.PlaybackStateForEvent(12) == AudioState.Buffering && MediaEngineAudioPlayer.PlaybackStateForEvent(13) == AudioState.Playing,
            "Source prefetch events cannot falsely replace actual playback state");
        var player = new FixturePlayer();
        var metadata = new FixtureMetadata();
        var resolver = new FixtureResolver();
        using var form = new MainForm(player, false, true, resolver, metadata);
        form.Show();
        await Task.Delay(100);
        Check(form.ClientSize == new Size(550, 233) && Find<ListBox>(form, "StationList").ItemHeight == 37,
            "Five-row window is shorter (550 x 233 DIP) without stretching 37-DIP items");
        Check(player.Plays == 0 && form.LatestStatus.State == AudioState.Stopped, "Startup never auto-plays");
        Capture(form, "artifacts/direction-c-dark-idle.png");
        var list = Find<ListBox>(form, "StationList");
        var play = Find<Button>(form, "PlayButton");
        var mute = Find<CheckBox>(form, "Mute");
        var volume = Find<TrackBar>(form, "Volume");
        Check(list.Items.Count == 6, "Six catalog stations remain available");
        var listWidth = list.Width;
        Check(((StationListBox)list).VisibleRows == 5, "List shows exactly five full rows");
        list.TopIndex = 1;
        await Task.Delay(30);
        Check(list.TopIndex > 0 && list.Width == listWidth && list.ClientSize.Width == list.Width && Math.Abs(list.Font.SizeInPoints - 10) < .1,
            "Sixth station scrolls into view without reserving a scrollbar column or shrinking fonts");
        list.TopIndex = 0;
        form.SelectForProbe("bcc-ilike");
        Check(player.Plays == 0, "Probe selection is silent; initial startup remains idle");
        var opening = form.PlayForProbeAsync("bcc-ilike");
        Check(form.LatestStatus.State == AudioState.Opening && form.PlayingStationId is null, "Connecting is not falsely shown as Playing");
        player.Emit(AudioState.Buffering);
        Check(form.LatestStatus.State == AudioState.Buffering, "Buffering reflects engine event");
        player.Emit(AudioState.Playing);
        await opening;
        Check(form.PlayingStationId == "bcc-ilike" && metadata.Calls == 1, "Actual Playing event starts visible metadata fetch");
        var oldMetadata = metadata.Pending[0];
        list.SelectedIndex = 5;
        Check(player.Plays == 2 && form.LatestStatus.State == AudioState.Opening && form.PlayingStationId is null,
            "Changing station immediately releases old playback and connects the new selection");
        player.Emit(AudioState.Playing);
        await form.SelectionPlaybackTask;
        oldMetadata.SetResult(new(MetadataKind.Program, "STALE", "OLD", DateTimeOffset.UtcNow.AddSeconds(90)));
        metadata.Pending[1].SetResult(new(MetadataKind.Presenter, null, "測試主持人", DateTimeOffset.UtcNow.AddSeconds(90)));
        await Task.Delay(50);
        Check(Find<Label>(form, "MetadataTitle").Text == "" && Find<Label>(form, "MetadataPerson").Text == "DJ：測試主持人",
            "Late old-station metadata is discarded; single DJ field is supported");
        mute.Checked = true; volume.Value = 63;
        Check(player.IsMuted && Math.Abs(player.Volume - .63) < .001, "Mute is independent of remembered per-player volume");
        mute.Checked = false;
        Check(!player.IsMuted && volume.Value == 63, "Unmute preserves slider value");
        volume.Focus(); SendMessage(volume.Handle, 0x0100, new IntPtr(0x27), IntPtr.Zero);
        Check(volume.Value > 63, "Native slider retains keyboard right-arrow behavior");
        var volumeEvents = 0;
        volume.ValueChanged += (_, _) => volumeEvents++;
        SendMouse(volume, 0x0201, volume.Width * 3 / 4, volume.Height / 2);
        Check(volume.Value is > 65 and < 90 && Math.Abs(player.Volume - volume.Value / 100d) < .001 && volumeEvents > 0,
            "Clicking the drawn slider immediately seeks volume and fires ValueChanged");
        SendMouse(volume, 0x0200, volume.Width + 30, volume.Height / 2);
        SendMouse(volume, 0x0202, volume.Width + 30, volume.Height / 2);
        Check(volume.Value == 100, "Dragging beyond slider edge clamps to 100 percent");
        SendMouse(volume, 0x0201, -20, volume.Height / 2);
        SendMouse(volume, 0x0202, -20, volume.Height / 2);
        Check(volume.Value == 0, "Clicking slider left edge clamps to zero");
        volume.Value = 40;
        var committedVolume = -1;
        ((VolumeSlider)volume).InteractionCompleted += () => committedVolume = volume.Value;
        SendMessage(volume.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
        Check(volume.Value != 40 && committedVolume == volume.Value && Math.Abs(player.Volume - volume.Value / 100d) < .001,
            "Wheel changes volume and commits the final value after native input processing");
        var playsBeforeScroll = player.Plays;
        var stationList = (StationListBox)list;
        var card = Find<Label>(form, "NowPlayingTitle").Parent!.Parent!;
        Check(stationList.ItemContentWidth == 241 && card.PointToScreen(Point.Empty).X - form.Left == 258,
            "Overlay uses the existing gap; station-card width and right-panel position are unchanged");
        SendMessage(list.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
        Check(!stationList.ScrollbarVisible, "Scrollbar is hidden when pointer leaves the list");
        SendMouse(list, 0x0200, 60, list.ItemHeight / 2);
        Check(stationList.ScrollbarVisible, "Scrollbar appears when pointer enters station list");
        Check(stationList.ScrollbarTrackBounds.Width == 2 && stationList.ScrollbarTrackBounds.Left - stationList.ItemContentWidth == 3,
            "Scrollbar is thinner (2 DIP) with a 3-DIP gap from rounded station items");
        list.TopIndex = 0;
        var track = stationList.ScrollbarTrackBounds;
        Check(stationList.ScrollThumb.Top == track.Top && track.Top == 8, "Top thumb is inset inside first item's rounded ends");
        SendMouse(list, 0x0201, track.Left, track.Bottom - 1);
        SendMouse(list, 0x0202, track.Left, track.Bottom - 1);
        Check(list.TopIndex == 1 && player.Plays == playsBeforeScroll && list.SelectedIndex == 5,
            "Thin overlay scrollbar is clickable without selecting or retuning stations");
        Check(stationList.ScrollThumb.Bottom == track.Bottom && track.Bottom == 5 * list.ItemHeight - 8,
            "Bottom thumb stays inside last item's rounded ends");
        Capture(form, "artifacts/scrollbar-bottom.png");
        SendMouse(list, 0x0201, track.Left, stationList.ScrollThumb.Top + 10);
        SendMessage(list.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
        Check(stationList.ScrollbarVisible, "Pointer leaving during a drag keeps scrollbar visible");
        SendMouse(list, 0x0200, -4, -30);
        SendMouse(list, 0x0202, -4, -30);
        Check(list.TopIndex == 0 && player.Plays == playsBeforeScroll, "Dragging overlay thumb scrolls without retuning");
        Check(!stationList.ScrollbarVisible, "Releasing drag outside hides the scrollbar");
        SendMouse(list, 0x0201, track.Left, stationList.ScrollThumb.Top + 10);
        SendMessage(list.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
        list.Capture = false;
        Check(!stationList.ScrollbarVisible, "Lost mouse capture does not leave a stuck scrollbar");
        list.TopIndex = 1;
        SendMessage(list.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
        Check(list.TopIndex == 0, "List mouse wheel still scrolls with native scrollbar hidden");
        Capture(form, "artifacts/scrollbar-hidden.png");
        SendMouse(list, 0x0200, 60, list.ItemHeight / 2);
        Capture(form, "artifacts/scrollbar-hover.png");
        var count = form.PresentationUpdates;
        form.WindowState = FormWindowState.Minimized;
        await Task.Delay(50);
        count = form.PresentationUpdates;
        var calls = metadata.Calls;
        player.Emit(AudioState.Buffering); player.Emit(AudioState.Playing);
        await Task.Delay(100);
        Check(!form.Visible && form.IsBackgroundMode && !form.MetadataRunning && metadata.Calls == calls && form.PresentationUpdates == count,
            "Tray mode stops metadata and UI presentation updates without stopping audio");
        Check(form.PlayingStationId == "hit-north" && player.Stops == 2, "Minimize does not disconnect playback");
        form.ShowPlayer(); await Task.Delay(50);
        Check(form.Visible && metadata.Calls == calls + 1, "Tray restore fetches fresh metadata immediately");
        metadata.Pending[^1].SetResult(null); await Task.Delay(30);
        var transportBounds = play.Bounds;
        Check(!Find<Label>(form, "MetadataTitle").Visible && !Find<Label>(form, "MetadataPerson").Visible, "Missing metadata hides both fields");
        form.ToggleTheme();
        Check(!form.IsDarkMode && play.Bounds == transportBounds, "Light theme does not rearrange controls");
        Capture(form, "artifacts/direction-c-light-playing.png");
        form.ToggleTheme();
        Capture(form, "artifacts/direction-c-dark-playing.png");
        play.PerformClick();
        Check(form.LatestStatus.State == AudioState.Stopped && form.PlayingStationId is null && !form.MetadataRunning, "Stop disconnects and clears metadata");
        var retry = form.PlayForProbeAsync("best-989");
        player.Emit(AudioState.Failed);
        await retry;
        Check(form.LatestStatus.State == AudioState.Failed && play.AccessibleName == "播放", "Engine errors keep a usable retry button");
        var recovered = form.PlayForProbeAsync("best-989"); player.Emit(AudioState.Playing); await recovered;
        Check(form.LatestStatus.State == AudioState.Playing, "Playback can recover after error");
        resolver.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = form.PlayForProbeAsync("kiss-999");
        var playsBeforeCancel = player.Plays;
        play.PerformClick();
        resolver.Pending.SetResult(new(new Uri("https://example.invalid/late"), new Uri("https://example.invalid/"), "audio/aac"));
        await slow;
        Check(player.Plays == playsBeforeCancel && form.LatestStatus.State == AudioState.Stopped,
            "Stop during source resolution cannot restart audio from a late result");
        resolver.Pending = null; resolver.Fail = true;
        await form.PlayForProbeAsync("best-989");
        Check(form.LatestStatus.State == AudioState.Failed && play.Enabled, "Source errors leave UI responsive and retryable");
        resolver.Fail = false;
        recovered = form.PlayForProbeAsync("best-989"); player.Emit(AudioState.Playing); await recovered;

        // Deliver the same DPI-change message Windows sends. This validates layout calculations;
        // it does not replace an acceptance check on real monitors at each display scale.
        foreach (var dpi in new[] { 120, 144, 192, 96 })
        {
            SimulateDpi(form, dpi);
            await Task.Delay(50);
            Check(form.DeviceDpi == dpi && ((StationListBox)list).VisibleRows == 5 && list.ItemHeight == (int)Math.Round(37 * dpi / 96d),
                $"DPI {dpi}: five visible rows keep original 37-DIP item height");
            Check(Math.Abs(Find<Label>(form, "NowPlayingTitle").Font.Size - 11 * dpi / 72f) < .1, $"DPI {dpi}: explicit label fonts scale with list typography");
            Check(list.ClientSize.Width == list.Width, $"DPI {dpi}: overlay does not reserve a non-client scrollbar column");
            var scaledTrack = stationList.ScrollbarTrackBounds;
            list.TopIndex = 0;
            Check(stationList.ScrollThumb.Top == scaledTrack.Top && scaledTrack.Left > stationList.ItemContentWidth &&
                scaledTrack.Width == (int)Math.Round(2 * dpi / 96d), $"DPI {dpi}: thin scrollbar keeps its gap and top inset");
            list.TopIndex = 1;
            Check(stationList.ScrollThumb.Bottom == scaledTrack.Bottom && scaledTrack.Bottom <= 5 * list.ItemHeight - (int)Math.Round(8 * dpi / 96d),
                $"DPI {dpi}: bottom thumb never overhangs rounded items");
            list.TopIndex = 0;
            SendMouse(volume, 0x0201, volume.Width * 3 / 4, volume.Height / 2);
            SendMouse(volume, 0x0202, volume.Width * 3 / 4, volume.Height / 2);
            Check(volume.Value is > 65 and < 90, $"DPI {dpi}: visible slider track hit testing stays aligned");
            CheckLayout(form);
            Capture(form, $"artifacts/direction-c-dpi-{dpi}.png");
        }
        form.Close();
        Check(player.Disposed && metadata.Disposed, "Window close releases playback, metadata and tray ownership");
        CheckPreferences();
        await CheckStationInputAsync();
        Console.WriteLine("All offline Direction C UI checks passed.");
    }

    private static void CheckLayout(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.Visible)
            {
                if (child.Left < 0 || child.Top < 0 || child.Right > parent.ClientSize.Width + 1 || child.Bottom > parent.ClientSize.Height + 1)
                    throw new Exception($"Layout bounds: {child.Name} {child.Bounds} within {parent.ClientSize}");
                CheckLayout(child);
            }
        }
    }
    private static T Find<T>(Control root, string name) where T : Control => (T)root.Controls.Find(name, true).Single();
    private static void SendMouse(Control control, int message, int x, int y) =>
        SendMessage(control.Handle, message, message == 0x0202 ? IntPtr.Zero : new IntPtr(1), new IntPtr((x & 0xffff) | (y << 16)));
    private static void CheckPreferences()
    {
        var folder = Path.GetFullPath($"artifacts/settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "radio.settings.json");
        File.WriteAllText(path, """{"SchemaVersion":1,"Theme":"light"}""");
        Check(ThemeSettings.Load(path).Volume is null, "Legacy theme-only preferences keep default volume");
        ThemeSettings.Save(false, 73, path);
        Check(ThemeSettings.Load(path) is { Volume: 73, Theme: "light" }, "Volume persists with theme intact");
        ThemeSettings.Save(true, path: path);
        Check(ThemeSettings.Load(path) is { Volume: 73, Theme: "dark" }, "Changing theme does not reset persisted volume");
        File.WriteAllText(path, """{"SchemaVersion":1,"Theme":"dark","Volume":200}""");
        Check(ThemeSettings.Load(path).Volume is null, "Invalid stored volume is ignored");
    }
    private static async Task CheckStationInputAsync()
    {
        var player = new FixturePlayer();
        using var form = new MainForm(player, false, true, new FixtureResolver(), new FixtureMetadata());
        form.Show();
        await Task.Delay(30);
        var list = Find<ListBox>(form, "StationList");
        Check(player.Plays == 0, "Second window startup remains idle before clicking");
        SendMouse(list, 0x0201, 60, list.ItemHeight / 2);
        SendMouse(list, 0x0202, 60, list.ItemHeight / 2);
        Check(player.Plays == 1, $"Clicking the initially selected station starts playback exactly once (plays={player.Plays}, selected={list.SelectedIndex}, top={list.TopIndex}, row={list.ItemHeight}, hit={list.IndexFromPoint(60, list.ItemHeight / 2)})");
        player.Emit(AudioState.Playing); await form.SelectionPlaybackTask;
        SendMouse(list, 0x0201, 60, list.ItemHeight * 3 / 2);
        SendMouse(list, 0x0202, 60, list.ItemHeight * 3 / 2);
        Check(player.Plays == 2 && form.SelectedStationId == "ufo-921", "Mouse station change immediately tunes exactly once");
        player.Emit(AudioState.Playing); await form.SelectionPlaybackTask;
        SendMessage(list.Handle, 0x0100, new IntPtr(0x23), IntPtr.Zero);
        Check(list.SelectedIndex == 5 && list.TopIndex == 1 && player.Plays == 3, "Keyboard End scrolls to sixth station and tunes it");
        player.Emit(AudioState.Playing); await form.SelectionPlaybackTask;
        SendMessage(list.Handle, 0x0100, new IntPtr(0x24), IntPtr.Zero);
        Check(list.SelectedIndex == 0 && list.TopIndex == 0 && player.Plays == 4, "Keyboard Home scrolls back without changing layout");
        player.Emit(AudioState.Playing); await form.SelectionPlaybackTask;
        form.Close();
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine($"PASS: {name}");
    }
    private static void Capture(Form form, string path)
    {
        form.Refresh();
        Directory.CreateDirectory("artifacts");
        using var bitmap = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try { if (!PrintWindow(form.Handle, dc, 2)) throw new Exception("PrintWindow failed."); }
        finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    private static void SimulateDpi(Form form, int dpi)
    {
        var rect = new NativeRect { Left = form.Left, Top = form.Top, Right = form.Left + 550 * dpi / 96, Bottom = form.Top + 233 * dpi / 96 };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rect, ptr, false); SendMessage(form.Handle, 0x02E0, new IntPtr(dpi | dpi << 16), ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    private sealed class FixtureResolver : IStreamResolver
    {
        public TaskCompletionSource<ResolvedStream>? Pending { get; set; }
        public bool Fail { get; set; }
        public Task<ResolvedStream> ResolveAsync(Station station, CancellationToken token) =>
            Fail ? throw new SourceResolutionException("Fixture source unavailable") : Pending?.Task ??
            Task.FromResult(new ResolvedStream(new Uri("https://example.invalid/audio"), station.PageUrl, "audio/aac"));
    }
    private sealed class FixturePlayer : IAudioPlayer
    {
        public event Action<PlaybackStatus>? StatusChanged;
        public double Volume { get; set; } = .4;
        public bool IsMuted { get; set; }
        public TimeSpan Position => TimeSpan.Zero;
        public int Plays { get; private set; }
        public int Stops { get; private set; }
        public bool Disposed { get; private set; }
        public Task PlayAsync(ResolvedStream stream, CancellationToken token) { Plays++; Emit(AudioState.Opening); return Task.CompletedTask; }
        public void Stop() { Stops++; Emit(AudioState.Stopped); }
        public void Emit(AudioState state) => StatusChanged?.Invoke(new(state, "Fixture status"));
        public void Dispose() { Disposed = true; Stop(); }
    }
    private sealed class FixtureMetadata : IStationMetadataProvider
    {
        public List<TaskCompletionSource<StationMetadata?>> Pending { get; } = [];
        public int Calls => Pending.Count;
        public bool Disposed { get; private set; }
        public bool Supports(string id) => id is "bcc-ilike" or "hit-north";
        public Task<StationMetadata?> GetAsync(string id, CancellationToken token)
        {
            var completion = new TaskCompletionSource<StationMetadata?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(completion);
            // Intentionally ignore cancellation to verify that late responses cannot leak across stations.
            return completion.Task;
        }
        public void Dispose() { Disposed = true; foreach (var pending in Pending) pending.TrySetCanceled(); }
    }
}
