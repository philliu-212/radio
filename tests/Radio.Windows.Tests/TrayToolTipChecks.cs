using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class TrayToolTipChecks
{
    internal static async Task RunAsync()
    {
        using var icon = AppIcon.Load(16);
        using var menu = new ContextMenuStrip();
        menu.Items.Add("開啟播放器");
        menu.Items.Add(new ToolStripMenuItem("顯示節目／歌曲資訊") { Checked = true, CheckOnClick = true });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Radio Mini v測試").Enabled = false;
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
            tray.HandleNotification(0x7B);
            Check(((ToolStripMenuItem)menu.Items[1]).Checked, "Menu check remains enabled before rendering " + name);
            menu.Items[0].Select();
            using var menuCapture = new Bitmap(menu.Width, menu.Height);
            menu.DrawToBitmap(menuCapture, menu.ClientRectangle);
            menuCapture.Save($"artifacts/tray-menu-{name}.png");
            var colors = Enumerable.Range(0, menuCapture.Width)
                .SelectMany(x => Enumerable.Range(0, menuCapture.Height).Select(y => menuCapture.GetPixel(x, y).ToArgb())).ToHashSet();
            // Antialiased check strokes need not contain an exact solid accent pixel.
            bool HasColor(Color target) => colors.Select(Color.FromArgb).Any(color =>
                Math.Abs(color.R - target.R) + Math.Abs(color.G - target.G) + Math.Abs(color.B - target.B) < 35);
            var checkBounds = menu.Items[1].Bounds;
            var hasCheck = Enumerable.Range(3, 18).Any(x => Enumerable.Range(checkBounds.Top, checkBounds.Height).Any(y =>
            {
                var pixel = menuCapture.GetPixel(x, y);
                return Enumerable.Range(60, 41).Any(percent =>
                {
                    var alpha = percent / 100d;
                    return Math.Abs(pixel.R - (theme.Accent.R * alpha + theme.Surface.R * (1 - alpha))) +
                        Math.Abs(pixel.G - (theme.Accent.G * alpha + theme.Surface.G * (1 - alpha))) +
                        Math.Abs(pixel.B - (theme.Accent.B * alpha + theme.Surface.B * (1 - alpha))) < 12;
                });
            }));
            Check(menu.Visible && menu.Renderer is TrayMenuRenderer && menu.BackColor == theme.Surface && menu.ForeColor == theme.Text &&
                hasCheck && new[] { theme.Surface, theme.Selection, theme.Text, theme.Muted, theme.Track }.All(HasColor),
                name + " context menu renders themed background, hover, text, disabled version, check and separator; missing=" +
                string.Join(",", new[] { theme.Surface, theme.Selection, theme.Text, theme.Muted, theme.Track }.Where(color => !HasColor(color))) + "; check=" + hasCheck);
            menu.Items[1].PerformClick();
            Check(!((ToolStripMenuItem)menu.Items[1]).Checked, "Themed menu preserves check-on-click behavior");
            ((ToolStripMenuItem)menu.Items[1]).Checked = true;
            menu.Close();
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
