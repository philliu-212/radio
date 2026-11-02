using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Radio.Windows.UI;

// Shell v4 sends POPUPOPEN/POPUPCLOSE for custom tooltip UI. NIF_SHOWTIP is
// deliberately absent: Explorer keeps the accessible text, without a second tip.
internal sealed class ThemedTrayIcon : Component
{
    private const int Callback = 0x8001;
    private const uint MessageFlag = 1, IconFlag = 2, TipFlag = 4, GuidFlag = 0x20;
    private readonly Guid _guid = Guid.NewGuid();
    private readonly TrayWindow _window;
    private readonly TrayToolTipWindow _tip = new();
    private Icon? _icon;
    private string _text = "";
    private bool _visible, _registered, _disposed;
    private Rectangle _anchor;
    private ContextMenuStrip? _menu;
    private PlayerTheme _theme = PlayerTheme.Dark;
    internal bool UsesCustomToolTip { get; private set; }
    internal TrayToolTipWindow TipWindow => _tip;
    public event EventHandler? DoubleClick;

    public ThemedTrayIcon() => _window = new TrayWindow(this);
    public ContextMenuStrip? ContextMenuStrip
    {
        get => _menu;
        set
        {
            if (_menu is not null) _menu.Closed -= MenuClosed;
            _menu = value;
            if (_menu is not null) { _menu.Closed += MenuClosed; ApplyMenuTheme(); }
        }
    }
    public Icon? Icon { get => _icon; set { _icon = value; Update(); } }
    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value) return;
            _text = value; Update();
            if (_tip.Visible) _tip.ShowTip(_text, _anchor);
        }
    }
    public bool Visible { get => _visible; set { _visible = value; Update(); } }
    public void SetTheme(PlayerTheme theme)
    {
        _theme = theme;
        _tip.SetTheme(theme);
        ApplyMenuTheme();
    }
    private void ApplyMenuTheme()
    {
        if (_menu is null) return;
        _menu.BackColor = _theme.Surface;
        _menu.ForeColor = _theme.Text;
        _menu.Renderer = new TrayMenuRenderer(_theme);
        _menu.Invalidate();
    }

    private void MenuClosed(object? sender, ToolStripDropDownClosedEventArgs e)
    {
        if (_disposed || !_registered) return;
        var data = Data();
        Shell_NotifyIcon(3, ref data); // NIM_SETFOCUS: return keyboard focus to the tray.
    }

    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = _window.Handle, Id = 1,
        Flags = MessageFlag | IconFlag | TipFlag | GuidFlag,
        CallbackMessage = Callback, Icon = _icon?.Handle ?? IntPtr.Zero,
        Tip = _text.Length > 127 ? _text[..127] : _text, Guid = _guid,
        Info = "", InfoTitle = ""
    };

    private void Update()
    {
        if (_disposed) return;
        var data = Data();
        if (!_visible || _icon is null)
        {
            _tip.Hide();
            if (_registered) Shell_NotifyIcon(2, ref data);
            _registered = false; UsesCustomToolTip = false;
            return;
        }
        if (_registered) { Shell_NotifyIcon(1, ref data); return; }
        if (!Shell_NotifyIcon(0, ref data)) return; // Explorer may be restarting.
        _registered = true;
        data.Version = 4;
        UsesCustomToolTip = Shell_NotifyIcon(4, ref data);
    }

    private Rectangle IconBounds()
    {
        var id = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = _window.Handle, Id = 1, Guid = _guid };
        if (Shell_NotifyIconGetRect(ref id, out var rect) == 0)
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return new Rectangle(Cursor.Position, new Size(1, 1));
    }

    internal void HandleNotification(int notification, Rectangle? anchor = null)
    {
        if (_disposed || !_visible) return;
        if (notification == 0x406) // NIN_POPUPOPEN
        {
            if (ContextMenuStrip?.Visible == true) return;
            _anchor = anchor ?? IconBounds();
            _tip.ShowTip(_text, _anchor);
        }
        else if (notification == 0x407) _tip.Hide(); // NIN_POPUPCLOSE
        else if (notification is 0x203 or 0x401) // WM_LBUTTONDBLCLK / NIN_KEYSELECT
        {
            _tip.Hide(); DoubleClick?.Invoke(this, EventArgs.Empty);
        }
        else if (notification is 0x201 or 0x204 or 0x400) _tip.Hide();
        else if (notification == 0x7B) // WM_CONTEXTMENU (also keyboard invocation)
        {
            _tip.Hide();
            if (ContextMenuStrip is not { } menu) return;
            var bounds = IconBounds();
            SetForegroundWindow(_window.Handle);
            menu.Show(new Point(bounds.Left, bounds.Top));
            PostMessage(_window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            Visible = false; _disposed = true;
            ContextMenuStrip = null;
            _tip.Dispose(); _window.DestroyHandle();
        }
        base.Dispose(disposing);
    }

    private sealed class TrayWindow : NativeWindow
    {
        private readonly ThemedTrayIcon _owner;
        private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        internal TrayWindow(ThemedTrayIcon owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Caption = "Radio Mini Tray" });
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Callback)
            {
                // Version 4 packs the notification into LOWORD(lParam).
                _owner.HandleNotification((int)(m.LParam.ToInt64() & 0xffff));
                return;
            }
            if ((uint)m.Msg == _taskbarCreated)
            {
                _owner._registered = false; _owner.UsesCustomToolTip = false;
                _owner._tip.Hide(); _owner.Update();
            }
            base.WndProc(ref m);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id, Flags, CallbackMessage;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid Guid;
        internal IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id;
        internal Guid Guid;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyData data);
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconIdentifier id, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
