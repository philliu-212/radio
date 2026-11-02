using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class TrayToolTipChecks
{
    internal static async Task RunAsync()
    {
        using var icon = AppIcon.Load(16);
        using var menu = new ContextMenuStrip();
        menu.Items.Add("開啟播放器");
        using var tray = new ThemedTrayIcon { Icon = icon, Text = "Radio Mini · 中廣流行網", ContextMenuStrip = menu, Visible = true };
        Check(tray.UsesCustomToolTip, "Shell registers version 4 custom tray tooltip notifications");
        var work = Screen.PrimaryScreen!.WorkingArea;
        var anchor = new Rectangle(work.Left + work.Width / 2, work.Top + work.Height / 2, 24, 24);
        var active = Form.ActiveForm;
        foreach (var (name, theme) in new[] { ("dark", PlayerTheme.Dark), ("light", PlayerTheme.Light) })
        {
            tray.Text = "Radio Mini · 中廣流行網";
            tray.SetTheme(theme);
            tray.HandleNotification(0x406, anchor);
            await Task.Delay(100);
            Check(tray.TipWindow.Visible && tray.TipWindow.Text == tray.Text &&
                tray.TipWindow.BackColor == theme.Surface && tray.TipWindow.ForeColor == theme.Text,
                name + " tray tooltip shows the real station with themed colors");
            Check(Form.ActiveForm == active && !tray.TipWindow.ShowInTaskbar,
                "Tray tooltip does not activate a window or add a taskbar entry");
            using var capture = new Bitmap(tray.TipWindow.Width, tray.TipWindow.Height);
            tray.TipWindow.DrawToBitmap(capture, new Rectangle(Point.Empty, capture.Size));
            Directory.CreateDirectory("artifacts");
            capture.Save($"artifacts/tray-tooltip-{name}.png");
            tray.Text = "Radio Mini · POP Radio";
            Check(tray.TipWindow.Text == tray.Text, "Visible tray tooltip follows station text changes");
            tray.HandleNotification(0x407);
            Check(!tray.TipWindow.Visible, "Shell popup-close notification dismisses custom tray tooltip");
        }
        var doubles = 0;
        tray.DoubleClick += (_, _) => doubles++;
        tray.HandleNotification(0x406, anchor);
        tray.HandleNotification(0x203);
        Check(doubles == 1 && !tray.TipWindow.Visible, "Tray double-click still fires once and dismisses the tooltip");
        tray.HandleNotification(0x401);
        Check(doubles == 2, "Keyboard selection can also open the player from the tray");
        tray.HandleNotification(0x406, anchor);
        tray.HandleNotification(0x7B);
        Check(menu.Visible && !tray.TipWindow.Visible, "Tray context-menu callback opens the existing menu without a tooltip overlay");
        menu.Close();
        tray.HandleNotification(0x406, anchor);
        tray.Visible = false;
        Check(!tray.TipWindow.Visible && !tray.UsesCustomToolTip, "Hiding tray icon removes registration and popup");
        tray.Visible = true;
        Check(tray.UsesCustomToolTip, "Showing tray icon restores Shell version 4 registration");
        tray.HandleNotification(0x406, anchor);
        var popup = tray.TipWindow;
        tray.Dispose();
        Check(popup.IsDisposed, "Disposing tray icon releases its tooltip window");

        foreach (var bounds in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -200, 1920, 1080) })
        foreach (var position in new[] { bounds.Location, new Point(bounds.Right - 24, bounds.Bottom - 24) })
        {
            var size = new Size(320, 40);
            var point = TrayToolTipWindow.Place(new Rectangle(position, new Size(24, 24)), size, bounds, 8);
            Check(bounds.Contains(new Rectangle(point, size)), "Tray tooltip layout stays inside work area, including negative monitor origins");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
