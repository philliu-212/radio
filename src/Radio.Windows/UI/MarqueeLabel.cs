using System.Diagnostics;

namespace Radio.Windows.UI;

// Keep the full Text available to accessibility and the existing metadata tooltip.
internal sealed class MarqueeLabel : Label
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Stopwatch _clock = new();
    private const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
        TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping;
    private int _textWidth;
    private double _offset;
    private bool _animationEnabled = true;
    private bool _disposed;
    private Form? _owner;
    internal double ScrollOffset => _offset;
    internal bool AnimationRunning => _timer.Enabled;
    internal int OverflowWidth => Math.Max(0, _textWidth - Math.Max(0, ClientSize.Width - Padding.Horizontal));
    internal int CycleWidth => _textWidth + (int)Math.Round(32 * DeviceDpi / 96d);

    public MarqueeLabel()
    {
        Dock = DockStyle.Fill; Margin = Padding.Empty;
        TextAlign = ContentAlignment.MiddleLeft; UseMnemonic = false;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        _timer.Tick += (_, _) =>
        {
            var elapsed = _clock.Elapsed.TotalMilliseconds;
            _clock.Restart();
            // Do not jump across text after a blocked UI thread.
            AdvanceAnimation(Math.Min(elapsed, 100));
        };
    }

    internal bool AnimationEnabled
    {
        get => _animationEnabled;
        set { if (_animationEnabled == value) return; _animationEnabled = value; ResetAnimation(); }
    }

    private void ResetAnimation()
    {
        if (_disposed) return;
        _offset = 0;
        _textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font,
            new Size(int.MaxValue, int.MaxValue), TextFlags).Width;
        UpdateTimer(); Invalidate();
    }

    private void UpdateTimer()
    {
        // Virtual callbacks can occur in the Label base constructor.
        if (_timer is null) return;
        var run = !IsDisposed && IsHandleCreated && Visible && _animationEnabled && OverflowWidth > 0 &&
            _owner is { Visible: true, WindowState: not FormWindowState.Minimized };
        _timer.Enabled = run;
        if (run) _clock.Restart(); else _clock.Reset();
    }

    internal void AdvanceAnimation(double milliseconds)
    {
        if (!AnimationRunning || milliseconds <= 0) return;
        _offset = (_offset + milliseconds * 24 * DeviceDpi / 96d / 1000) % CycleWidth;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (OverflowWidth == 0) { base.OnPaint(e); return; }
        var viewport = new Rectangle(Padding.Left, Padding.Top,
            Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
        var state = e.Graphics.Save();
        e.Graphics.SetClip(viewport, System.Drawing.Drawing2D.CombineMode.Intersect);
        var bounds = new Rectangle(viewport.Left - (int)Math.Round(_offset), viewport.Top, _textWidth, viewport.Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, TextFlags);
        // The next copy enters before the current one leaves. Wrapping the offset
        // therefore changes no visible pixels and never jumps back to the start.
        bounds.X += CycleWidth;
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, TextFlags);
        e.Graphics.Restore(state);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); ResetAnimation(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ResetAnimation(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ResetAnimation(); }
    protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); ResetAnimation(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ResetAnimation(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); ResetAnimation(); }
    private void OwnerStateChanged(object? sender, EventArgs e) => ResetAnimation();
    private void WatchOwner()
    {
        if (_owner is not null) { _owner.VisibleChanged -= OwnerStateChanged; _owner.Resize -= OwnerStateChanged; }
        _owner = _disposed ? null : FindForm();
        if (_owner is not null) { _owner.VisibleChanged += OwnerStateChanged; _owner.Resize += OwnerStateChanged; }
    }
    protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); WatchOwner(); ResetAnimation(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WatchOwner(); ResetAnimation(); }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!_disposed) { _timer.Stop(); _clock.Reset(); }
        base.OnHandleDestroyed(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; WatchOwner(); _timer.Dispose(); _clock.Stop(); }
        base.Dispose(disposing);
    }
}
