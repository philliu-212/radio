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
        Check(form.ClientSize == new Size(550, 270), "Direction C: 550 x 270 DIP at 96 DPI");
        Check(player.Plays == 0 && form.LatestStatus.State == AudioState.Stopped, "Startup never auto-plays");
        Capture(form, "artifacts/direction-c-dark-idle.png");
        var list = Find<ListBox>(form, "StationList");
        var play = Find<Button>(form, "PlayButton");
        var mute = Find<CheckBox>(form, "Mute");
        var volume = Find<TrackBar>(form, "Volume");
        Check(list.Items.Count == 6, "Six catalog stations remain available");
        var listWidth = list.Width;
        list.DataSource = StationCatalog.Load().Concat(StationCatalog.Load()).ToList();
        list.SelectedIndex = 11;
        await Task.Delay(30);
        Check(list.TopIndex > 0 && list.Width == listWidth && Math.Abs(list.Font.SizeInPoints - 10) < .1, "Long list scrolls without changing column width or font");
        list.DataSource = StationCatalog.Load();
        form.SelectForProbe("bcc-ilike");
        Check(player.Plays == 0, "Selection alone does not open a source");
        var opening = form.PlayForProbeAsync("bcc-ilike");
        Check(form.LatestStatus.State == AudioState.Opening && form.PlayingStationId is null, "Connecting is not falsely shown as Playing");
        player.Emit(AudioState.Buffering);
        Check(form.LatestStatus.State == AudioState.Buffering, "Buffering reflects engine event");
        player.Emit(AudioState.Playing);
        await opening;
        Check(form.PlayingStationId == "bcc-ilike" && metadata.Calls == 1, "Actual Playing event starts visible metadata fetch");
        form.SelectForProbe("hit-north");
        Check(form.PlayingStationId == "bcc-ilike" && form.DisplayedStation == "中廣流行網" && player.Plays == 1,
            "Selected and playing stations are separate; right card stays on actual source");
        var oldMetadata = metadata.Pending[0];
        var next = form.PlayForProbeAsync("hit-north");
        player.Emit(AudioState.Playing);
        await next;
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
            Check(form.DeviceDpi == dpi && list.ItemHeight == (int)Math.Round(37 * dpi / 96d), $"DPI {dpi}: list rows scale without smaller fonts");
            Check(Math.Abs(Find<Label>(form, "NowPlayingTitle").Font.Size - 11 * dpi / 72f) < .1, $"DPI {dpi}: explicit label fonts scale with list typography");
            CheckLayout(form);
            Capture(form, $"artifacts/direction-c-dpi-{dpi}.png");
        }
        form.Close();
        Check(player.Disposed && metadata.Disposed, "Window close releases playback, metadata and tray ownership");
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
        var rect = new NativeRect { Left = form.Left, Top = form.Top, Right = form.Left + 550 * dpi / 96, Bottom = form.Top + 270 * dpi / 96 };
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
