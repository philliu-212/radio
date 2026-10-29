using System.Runtime.InteropServices;
using Radio.Core;
using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class UiChecks
{
    public static async Task RunAsync()
    {
        await CaptureFourArrowPreviewAsync();
        Check(MediaEngineAudioPlayer.PlaybackStateForEvent(1005) is null && MediaEngineAudioPlayer.PlaybackStateForEvent(1006) is null &&
            MediaEngineAudioPlayer.PlaybackStateForEvent(12) == AudioState.Buffering && MediaEngineAudioPlayer.PlaybackStateForEvent(13) == AudioState.Playing,
            "Source prefetch events cannot falsely replace actual playback state");
        var player = new FixturePlayer();
        var metadata = new FixtureMetadata();
        var resolver = new FixtureResolver();
        var settings = CatalogSettings();
        using var form = new MainForm(player, false, true, resolver, metadata, settings);
        form.Show();
        await Task.Delay(100);
        var expectedVersion = typeof(MainForm).Assembly.GetName().Version!.ToString(3);
        Check(form.Text == $"Radio Mini v{expectedVersion}" && Find<Label>(form, "AppTitle").Text == form.Text,
            "Visible app title and taskbar title show the actual app assembly version");
        try { PlayerBackend.Create(["--backend", "mpv"]); throw new Exception("Removed backend was accepted."); }
        catch (ArgumentException) { Check(true, "Removed backend is rejected rather than launching an external player"); }
        Check(form.ClientSize == new Size(554, 271) && Find<ListBox>(form, "StationList").ItemHeight == 37,
            "Search adds only 38 DIP in height; original widths, fonts and 31-DIP rows remain");
        Check(player.Plays == 0 && form.LatestStatus.State == AudioState.Stopped, "Startup never auto-plays");
        Capture(form, "artifacts/direction-c-dark-idle.png");
        var list = Find<ListBox>(form, "StationList");
        var play = Find<Button>(form, "PlayButton");
        var mute = Find<CheckBox>(form, "Mute");
        var volume = Find<TrackBar>(form, "Volume");
        CheckNoStationFocusFrame((StationListBox)list, Find<StationSearchBox>(form, "StationSearch").Controls[0]);
        Check(list.Items.Count == 7, "Seven catalog stations remain available");
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
        Check(settings.PlayCounts.GetValueOrDefault("bcc-ilike") == 1, "Only successful playback counts");
        player.Emit(AudioState.Buffering); player.Emit(AudioState.Playing);
        Check(settings.PlayCounts["bcc-ilike"] == 1 && ((Station)list.Items[0]).Id == "pop-917", "Buffering/reconnect events neither count again nor reorder rows");
        var search = Find<StationSearchBox>(form, "StationSearch");
        var playsBeforeSearch = player.Plays;
        search.Query = "107.7";
        Check(list.Items.Count == 1 && form.PlayingStationId == "bcc-ilike" && player.Plays == playsBeforeSearch, "Frequency search never retunes playback");
        search.Query = "does not exist";
        Check(list.Items.Count == 0 && form.PlayingStationId == "bcc-ilike", "No results keep existing audio and information");
        search.Query = "";
        Check(list.Items.Count == 7 && player.Plays == playsBeforeSearch, "Clearing search restores full list silently");
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
        Check(stationList.ItemContentWidth == 241 && card.Width == 283 && card.PointToScreen(Point.Empty).X - form.Left == 262,
            "Only gap grows by 4 DIP; item and status-card widths stay unchanged");
        SendMessage(list.Handle, 0x02A3, IntPtr.Zero, IntPtr.Zero);
        Check(!stationList.ScrollbarVisible, "Scrollbar is hidden when pointer leaves the list");
        SendMouse(list, 0x0200, 60, list.ItemHeight / 2);
        Check(stationList.ScrollbarVisible, "Scrollbar appears when pointer enters station list");
        list.Update();
        var hoverInvalidations = new List<Rectangle>();
        void RecordInvalidation(object? sender, InvalidateEventArgs e) => hoverInvalidations.Add(e.InvalidRect);
        list.Invalidated += RecordInvalidation;
        SendMouse(list, 0x0200, 65, list.ItemHeight / 2);
        Check(hoverInvalidations.Count == 0, "Moving within the same row does not repaint the list or scrollbar");
        SendMouse(list, 0x0200, 65, list.ItemHeight * 3 / 2);
        Check(hoverInvalidations.Count == 2 && hoverInvalidations.All(bounds => bounds.Width == stationList.ItemContentWidth && bounds.Height == list.ItemHeight),
            "Crossing rows repaints only the previous and new hover rows, not the scrollbar");
        list.Invalidated -= RecordInvalidation;
        var drawnRows = 0;
        void RecordDraw(object? sender, DrawItemEventArgs e) => drawnRows++;
        list.DrawItem += RecordDraw;
        list.Refresh();
        list.DrawItem -= RecordDraw;
        var visibleRows = Enumerable.Range(list.TopIndex, list.Items.Count - list.TopIndex)
            .Count(index => list.GetItemRectangle(index).IntersectsWith(list.ClientRectangle));
        Check(drawnRows == visibleRows, "Buffered painting renders all visible owner-drawn rows, including a partial bottom row");
        Check(stationList.ScrollbarTrackBounds.Width == 2 && stationList.ScrollbarTrackBounds.Left - stationList.ItemContentWidth == 2 &&
            list.ClientSize.Width - stationList.ScrollbarTrackBounds.Right == 8,
            "Approved spacing is exactly item / 2 DIP / bar 2 DIP / 8 DIP / status card");
        list.TopIndex = 0;
        var track = stationList.ScrollbarTrackBounds;
        Check(stationList.ScrollThumb.Top == track.Top && track.Top == 8, "Top thumb is inset inside first item's rounded ends");
        SendMouse(list, 0x0201, track.Left, track.Bottom - 1);
        SendMouse(list, 0x0202, track.Left, track.Bottom - 1);
        Check(list.TopIndex == list.Items.Count - stationList.VisibleRows && player.Plays == playsBeforeScroll && list.SelectedIndex == 5,
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
        list.Update();
        var boundaryInvalidations = 0;
        void RecordBoundaryInvalidation(object? sender, InvalidateEventArgs e) => boundaryInvalidations++;
        list.Invalidated += RecordBoundaryInvalidation;
        for (var i = 0; i < 5; i++) SendMessage(list.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
        Check(list.TopIndex == 0 && boundaryInvalidations == 0, "Repeated wheel scrolling above the first row does not repaint");
        list.Invalidated -= RecordBoundaryInvalidation;
        list.TopIndex = list.Items.Count - stationList.VisibleRows;
        list.Update();
        boundaryInvalidations = 0;
        list.Invalidated += RecordBoundaryInvalidation;
        for (var i = 0; i < 5; i++) SendMessage(list.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
        Check(list.TopIndex == list.Items.Count - stationList.VisibleRows && boundaryInvalidations == 0, "Repeated wheel scrolling below the last row does not repaint");
        list.Invalidated -= RecordBoundaryInvalidation;
        list.TopIndex = 0;
        Capture(form, "artifacts/scrollbar-hidden.png");
        SendMouse(list, 0x0200, 60, list.ItemHeight / 2);
        Capture(form, "artifacts/scrollbar-hover.png");
        var count = form.PresentationUpdates;
        Find<Button>(form, "MinimizeButton").PerformClick();
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
        var playsBeforeMinimize = player.Plays;
        var stopsBeforeMinimize = player.Stops;
        SendMessage(form.Handle, 0x0112, new IntPtr(0xF020), IntPtr.Zero);
        await Task.Delay(30);
        Check(form.Visible && form.ShowInTaskbar && form.WindowState == FormWindowState.Minimized && form.IsBackgroundMode && !form.MetadataRunning,
            "Native taskbar minimize keeps the window available on the taskbar while suspending presentation");
        SendMessage(form.Handle, 0x0112, new IntPtr(0xF120), IntPtr.Zero);
        await Task.Delay(30);
        Check(form.Visible && form.WindowState == FormWindowState.Normal && !form.IsBackgroundMode && form.MetadataRunning &&
            player.Plays == playsBeforeMinimize && player.Stops == stopsBeforeMinimize && play.Bounds == transportBounds,
            "Native taskbar restore resumes presentation without hiding, resizing or restarting playback");
        metadata.Pending[^1].SetResult(null); await Task.Delay(30);
        form.ToggleTheme();
        Check(!form.IsDarkMode && play.Bounds == transportBounds, "Light theme does not rearrange controls");
        Capture(form, "artifacts/direction-c-light-playing.png");
        form.ToggleTheme();
        Capture(form, "artifacts/direction-c-dark-playing.png");
        play.PerformClick();
        Check(form.LatestStatus.State == AudioState.Stopped && form.PlayingStationId is null && !form.MetadataRunning, "Stop disconnects and clears metadata");
        var retry = form.PlayForProbeAsync("best-989");
        var countBeforeFailure = settings.PlayCounts.GetValueOrDefault("best-989");
        player.Emit(AudioState.Failed);
        await retry;
        Check(settings.PlayCounts.GetValueOrDefault("best-989") == countBeforeFailure, "Failed stream never increments count");
        Check(form.LatestStatus.State == AudioState.Failed && play.AccessibleName == "播放", "Engine errors keep a usable retry button");
        var recovered = form.PlayForProbeAsync("best-989"); player.Emit(AudioState.Playing); await recovered;
        Check(settings.PlayCounts["best-989"] == countBeforeFailure + 1, "New successful manual retry counts once");
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
            Check(scaledTrack.Left - stationList.ItemContentWidth == (int)Math.Round(2 * dpi / 96d) &&
                Math.Abs(list.ClientSize.Width - scaledTrack.Right - 8 * dpi / 96d) <= 1,
                $"DPI {dpi}: approved 2/2/8 spacing survives pixel rounding");
            list.TopIndex = list.Items.Count - stationList.VisibleRows;
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
        await CheckStationManagementAsync();
        await CheckBatchStationManagementAsync();
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
    private static int HitTest(Form form, Point screenPoint) => (int)SendMessage(form.Handle, 0x0084, IntPtr.Zero,
        new IntPtr((screenPoint.X & 0xffff) | ((screenPoint.Y & 0xffff) << 16)));
    private static void CheckNoStationFocusFrame(StationListBox list, Control other)
    {
        list.SelectedIndex = 0; list.Focus(); list.Refresh();
        using var focused = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(focused, list.ClientRectangle);
        other.Focus(); list.Refresh();
        using var unfocused = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(unfocused, list.ClientRectangle);
        for (var y = 0; y < focused.Height; y++)
            for (var x = 0; x < focused.Width; x++)
                if (focused.GetPixel(x, y) != unfocused.GetPixel(x, y))
                    throw new Exception($"Station focus changed row pixels: {list.Name} at {x},{y}");
        Check(true, $"{list.Name}: focused selection adds no dotted frame");
    }
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
        var settings = CatalogSettings(); settings.PlayCounts["pop-917"] = 12;
        ThemeSettings.Save(settings, path);
        ThemeSettings.Save(false, 62, path);
        var restored = ThemeSettings.Load(path);
        Check(restored.MyStationIds.SequenceEqual(settings.MyStationIds) && restored.PlayCounts["pop-917"] == 12 && restored.Volume == 62,
            "Station membership and real counts survive saving preferences and reloading");
        restored.MyStationIds.Clear(); ThemeSettings.Save(restored, path);
        Check(ThemeSettings.Load(path).MyStationIds.Count == 0, "Explicit empty membership persists");
        File.WriteAllText(path, """{"SchemaVersion":1,"Theme":"light","Volume":45}""");
        restored = ThemeSettings.Load(path);
        Check(restored.Theme == "light" && restored.Volume == 45 && restored.MyStationIds.Count == 0, "Legacy settings migrate without inventing membership or losing preferences");
    }
    private static async Task CaptureFourArrowPreviewAsync()
    {
        // Capture the real controls with isolated fixture membership, never user settings.
        var settings = new ThemeSettings { MyStationIds = ["best-989", "pop-917", "ufo-921"] };
        using var font = new Font("Microsoft JhengHei UI", 10);
        using var dialog = new StationManagerDialog(new StationLibrary(settings), PlayerTheme.Dark, font);
        dialog.Show(); await Task.Delay(30);
        var available = Find<ListBox>(dialog, "AvailableStations");
        var mine = Find<ListBox>(dialog, "MyStations");
        available.SetSelected(0, true); available.SetSelected(2, true); mine.SetSelected(1, true);
        Capture(dialog, "artifacts/station-manager-four-arrows.png");
        dialog.Close();
    }

    private static ThemeSettings CatalogSettings() => new() { MyStationIds = StationCatalog.Load().Select(s => s.Id).ToList() };

    private static async Task CheckStationManagementAsync()
    {
        var settings = new ThemeSettings();
        var library = new StationLibrary(settings);
        using (var dialog = new StationManagerDialog(library, PlayerTheme.Dark, SystemFonts.MessageBoxFont!))
        {
            dialog.Show(); await Task.Delay(30);
            var available = Find<ListBox>(dialog, "AvailableStations");
            var mine = Find<ListBox>(dialog, "MyStations");
            var left = Find<StationSearchBox>(dialog, "AvailableSearch");
            var right = Find<StationSearchBox>(dialog, "MySearch");
            Check(available.Items.Count == 7 && mine.Items.Count == 0 && Find<Button>(dialog, "DoneButton").Enabled,
                "Empty manager allows completion and exposes the real catalog");
            Check(left.PointToScreen(new Point(left.Width, 0)).X == available.PointToScreen(new Point(((StationListBox)available).ItemContentWidth, 0)).X &&
                right.PointToScreen(new Point(right.Width, 0)).X == mine.PointToScreen(new Point(((StationListBox)mine).ItemContentWidth, 0)).X,
                "Both search pills align with the visible station rows' right edges");
            var addButton = Find<PlayerButton>(dialog, "AddStation");
            Check(addButton.Framed && addButton.Size == new Size(30, 30) && Find<PlayerButton>(dialog, "RemoveStation").Framed,
                "Transfer arrows use compact framed 30-DIP buttons");
            Check(HitTest(dialog, dialog.PointToScreen(new Point(100, dialog.ClientSize.Height - 10))) == 2,
                "Dialog background exposes native caption hit testing for window dragging");
            Check(HitTest(dialog, left.PointToScreen(new Point(left.Width / 2, left.Height / 2))) == 1 &&
                HitTest(dialog, available.PointToScreen(new Point(50, 20))) == 1 &&
                HitTest(dialog, addButton.PointToScreen(new Point(15, 15))) == 1,
                "Search, station rows and transfer buttons are not draggable surfaces");
            CheckNoStationFocusFrame((StationListBox)available, left.Controls[0]);
            left.Query = "91.7"; right.Query = "KISS";
            Check(available.Items.Count == 1 && mine.Items.Count == 0, "Manager searches are independent");
            available.SelectedIndex = 0; Find<Button>(dialog, "AddStation").PerformClick();
            Check(available.Items.Count == 0 && mine.Items.Count == 0 && library.Mine.Count == 1, "Adding moves membership, respecting both filters");
            right.Query = "POP";
            Check(mine.Items.Count == 1 && library.Count("pop-917") == 0, "Name search finds newly added station with zero count");
            library.Add("pop-917"); Check(library.Mine.Count == 1, "Duplicate membership rejected");
            mine.SelectedIndex = 0; Find<Button>(dialog, "RemoveStation").PerformClick();
            Check(mine.Items.Count == 0 && available.Items.Count == 1, "Removing returns station to available catalog");
            left.Query = "";
            Capture(dialog, "artifacts/station-manager-empty.png");
            CheckLayout(dialog);
            foreach (var dpi in new[] { 120, 144, 192, 96 })
            {
                SimulateDpi(dialog, dpi, 293); await Task.Delay(30);
                CheckLayout(dialog);
                Check(new[] { "AddStation", "RemoveStation", "AddAllStations", "RemoveAllStations" }
                    .Select(name => Find<Button>(dialog, name)).All(button => button.Width == (int)Math.Round(30 * dpi / 96d) && button.Height == (int)Math.Round(30 * dpi / 96d)),
                    $"Manager DPI {dpi}: all four action buttons keep their compact square size");
                var content = dialog.Controls[0];
                var corner = (int)Math.Ceiling(12 * dpi / 96d);
                Check(content.Left >= corner && content.Top >= corner &&
                    content.Right <= dialog.ClientSize.Width - corner && content.Bottom <= dialog.ClientSize.Height - corner,
                    $"Manager DPI {dpi}: child windows cannot overlap any rounded perimeter edge");
                Check(dialog.Region!.IsVisible(dialog.ClientSize.Width - 20, dialog.ClientSize.Height / 2), $"Manager DPI {dpi}: rounded window region follows full dialog size");
                Check(available.ItemHeight == (int)Math.Round(37 * dpi / 96d), $"Manager DPI {dpi}: row height scales without clipping search or actions");
                Capture(dialog, $"artifacts/station-manager-dpi-{dpi}.png");
            }
            Find<Button>(dialog, "DoneButton").PerformClick();
        }
        library.Add("pop-917"); library.Add("ufo-921"); library.Add("bcc-ilike");
        library.RecordSuccess("bcc-ilike");
        Check(library.Mine[0].Id == "pop-917", "Successful play does not immediately sort");
        library.Sort();
        Check(library.Mine.Select(s => s.Id).SequenceEqual(new[] { "bcc-ilike", "pop-917", "ufo-921" }), "Sort is descending with stable ties");
        library.Remove("bcc-ilike"); library.Add("bcc-ilike");
        Check(library.Count("bcc-ilike") == 1, "Statistics survive removal and re-addition");

        var player = new FixturePlayer();
        using var form = new MainForm(player, false, true, new FixtureResolver(), new FixtureMetadata());
        var opened = 0;
        using var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            var manager = Application.OpenForms.OfType<StationManagerDialog>().FirstOrDefault();
            if (manager is null) return;
            timer.Stop(); opened++;
            Check(manager.Modal && manager.Owner == form && form.Visible, "Manager is modal and retains main window behind it");
            if (opened == 2)
            {
                var available = Find<ListBox>(manager, "AvailableStations"); available.SelectedIndex = 0;
                Find<Button>(manager, "AddStation").PerformClick();
                available.SelectedIndex = 0;
                Capture(manager, "artifacts/station-manager-added.png");
            }
            if (opened == 3)
            {
                var mine = Find<ListBox>(manager, "MyStations"); mine.SelectedIndex = 0;
                Find<Button>(manager, "RemoveStation").PerformClick();
                Check(form.PlayingStationId == "pop-917", "Managing membership does not interrupt audio before completion");
            }
            Find<Button>(manager, "DoneButton").PerformClick();
        };
        form.Show(); await Task.Delay(150);
        Check(opened == 0 && !Application.OpenForms.OfType<StationManagerDialog>().Any() && Find<Button>(form, "EmptyAddStation").Visible,
            "First empty startup shows only the central plus, never opens management automatically");
        Capture(form, "artifacts/station-first-start.png");
        timer.Start(); Find<Button>(form, "EmptyAddStation").PerformClick();
        Check(opened == 1 && Find<Button>(form, "EmptyAddStation").Visible && !Find<ListBox>(form, "StationList").Visible,
            "Central plus opens manager; zero-station completion leaves only centered plus");
        Capture(form, "artifacts/station-empty-player.png");
        timer.Start(); Find<Button>(form, "EmptyAddStation").PerformClick();
        Check(opened == 2 && Find<ListBox>(form, "StationList").Visible && !Find<Button>(form, "EmptyAddStation").Visible,
            "Central plus reopens manager and adding restores normal player");
        Capture(form, "artifacts/station-player-search.png");
        var playback = form.PlayForProbeAsync("pop-917"); player.Emit(AudioState.Playing); await playback;
        timer.Start(); Find<Button>(form, "ManageStations").PerformClick();
        Check(opened == 3 && form.PlayingStationId is null && form.LatestStatus.State == AudioState.Stopped && Find<Button>(form, "EmptyAddStation").Visible,
            "Completing removal of the playing station stops audio and returns to zero-state player");
        form.Close();
    }
    private static async Task CheckBatchStationManagementAsync()
    {
        var settings = new ThemeSettings();
        var library = new StationLibrary(settings);
        using (var dialog = new StationManagerDialog(library, PlayerTheme.Dark, SystemFonts.MessageBoxFont!))
        {
            dialog.Show(); await Task.Delay(30);
            var available = Find<StationListBox>(dialog, "AvailableStations");
            var mine = Find<StationListBox>(dialog, "MyStations");
            var left = Find<StationSearchBox>(dialog, "AvailableSearch");
            var right = Find<StationSearchBox>(dialog, "MySearch");
            var add = Find<Button>(dialog, "AddStation"); var remove = Find<Button>(dialog, "RemoveStation");
            var addAll = Find<Button>(dialog, "AddAllStations"); var removeAll = Find<Button>(dialog, "RemoveAllStations");
            void ClickRow(StationListBox list, int index)
            {
                var y = list.GetItemRectangle(index).Top + list.ItemHeight / 2;
                SendMouse(list, 0x0201, 50, y); SendMouse(list, 0x0202, 50, y);
            }
            Check(!add.Enabled && !remove.Enabled && addAll.Enabled && !removeAll.Enabled,
                "Selected and all-result actions have independent empty-state availability");
            ClickRow(available, 0); ClickRow(available, 2);
            Check(available.SelectedIndices.Cast<int>().SequenceEqual(new[] { 0, 2 }), "Plain clicks select multiple rows without Ctrl");
            var selectedDrawn = new HashSet<int>();
            void RecordSelection(object? sender, DrawItemEventArgs e) { if ((e.State & DrawItemState.Selected) != 0) selectedDrawn.Add(e.Index); }
            available.DrawItem += RecordSelection; available.Refresh(); available.DrawItem -= RecordSelection;
            Check(selectedDrawn.SetEquals(new[] { 0, 2 }), "All selected rows receive the selection background");
            ClickRow(available, 0);
            Check(available.SelectedIndices.Cast<int>().SequenceEqual(new[] { 2 }), "Clicking an already selected row toggles only that row off");
            var beforeScroll = available.SelectedItems.Cast<Station>().Select(s => s.Id).ToArray();
            SendMessage(available.Handle, 0x020A, new IntPtr(-120 << 16), IntPtr.Zero);
            Check(available.SelectedItems.Cast<Station>().Select(s => s.Id).SequenceEqual(beforeScroll), "Multi-select scrolling does not alter selections");
            available.TopIndex = 0;
            ClickRow(available, 0); ClickRow(available, 1);
            var notifications = 0; dialog.MembershipChanged += () => notifications++;
            add.PerformClick();
            Check(library.Mine.Count == 3 && library.Available.Count == 4 && notifications == 1,
                "Single chevron transfers all selected rows in one batch");
            mine.SetSelected(0, true); mine.SetSelected(1, true); remove.PerformClick();
            Check(library.Mine.Count == 1 && notifications == 2, "Left single chevron removes multiple selected rows in one batch");
            left.Query = "91.7"; addAll.PerformClick();
            Check(library.Mine.Count == 2 && available.Items.Count == 0 && !addAll.Enabled,
                "Double right chevron adds only visible search results without prior selection");
            right.Query = "96.3"; removeAll.PerformClick();
            Check(library.Mine.Count == 1 && library.Mine[0].Id == "pop-917" && !removeAll.Enabled,
                "Double left chevron removes search results but preserves hidden stations");
            left.Query = ""; addAll.PerformClick(); right.Query = "";
            Check(library.Mine.Count == 7 && !addAll.Enabled && removeAll.Enabled, "Unfiltered add-all includes the entire remaining catalog without duplicates");
            removeAll.PerformClick();
            Check(library.Mine.Count == 0 && library.Available.Count == 7 && !removeAll.Enabled && Find<Button>(dialog, "DoneButton").Enabled,
                "Unfiltered remove-all leaves valid zero-station membership and enabled completion");
            Check(settings.PlayCounts.Values.All(count => count == 0), "Batch selection and transfer never increment playback statistics");
            dialog.Close();
        }

        var seed = StationCatalog.Load()[0];
        var catalog = Enumerable.Range(0, 120).Select(index => seed with { Id = $"fixture-{index}", Name = $"Fixture {index}" }).ToArray();
        var largeSettings = new ThemeSettings { MyStationIds = catalog.Take(100).Select(s => s.Id).ToList(), PlayCounts = { ["fixture-0"] = 17 } };
        var largeLibrary = new StationLibrary(largeSettings, catalog);
        using var largeDialog = new StationManagerDialog(largeLibrary, PlayerTheme.Dark, SystemFonts.MessageBoxFont!);
        largeDialog.Show(); await Task.Delay(30);
        var largeNotifications = 0; largeDialog.MembershipChanged += () => largeNotifications++;
        Find<Button>(largeDialog, "RemoveAllStations").PerformClick();
        Check(largeLibrary.Mine.Count == 0 && largeLibrary.Available.Count == 120 && largeNotifications == 1 && largeLibrary.Count("fixture-0") == 17,
            "One double-chevron click removes 100 stations, notifies once and retains lifetime statistics");
        largeDialog.Close();
    }

    private static async Task CheckStationInputAsync()
    {
        var player = new FixturePlayer();
        using var form = new MainForm(player, false, true, new FixtureResolver(), new FixtureMetadata(), CatalogSettings());
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
        Check(list.SelectedIndex == 6 && list.TopIndex == 2 && player.Plays == 3 && form.SelectedStationId == "rfi-monde", "Keyboard End scrolls to RFI and tunes it");
        Check(Find<Label>(form, "Frequency").Text == "網路" && Find<Label>(form, "FrequencyUnit").Text == "",
            "RFI displays internet-only status without an FM unit");
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
    private static void SimulateDpi(Form form, int dpi, int height = 271)
    {
        var width = form is StationManagerDialog ? 690 : 554;
        var rect = new NativeRect { Left = form.Left, Top = form.Top, Right = form.Left + width * dpi / 96, Bottom = form.Top + height * dpi / 96 };
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
