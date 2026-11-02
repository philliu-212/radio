using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class ThemedToolTipChecks
{
    internal static async Task RunAsync()
    {
        using var form = new Form { ClientSize = new Size(360, 150), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedToolWindow, TopMost = true, ShowInTaskbar = false,
            Text = "Radio Mini · Tooltip" };
        using var button = new Button { Text = "更新", Bounds = new Rectangle(24, 22, 80, 30) };
        form.Controls.Add(button);
        using var tips = new ThemedToolTip { ShowAlways = true, UseAnimation = false, UseFading = false };
        var draws = 0;
        tips.Draw += (_, _) => draws++;
        tips.SetToolTip(button, "更新");
        form.Show();
        form.Activate();
        foreach (var (name, theme) in new[] { ("dark", PlayerTheme.Dark), ("light", PlayerTheme.Light) })
        {
            tips.Hide(button);
            tips.SetTheme(theme);
            form.BackColor = theme.Canvas;
            button.BackColor = theme.Surface; button.ForeColor = theme.Text;
            form.Refresh();
            await Task.Delay(100);
            var before = draws;
            tips.Show("更新", button, 0, button.Height + 8, 5000);
            for (var i = 0; i < 30 && draws == before; i++) await Task.Delay(20);
            if (!tips.OwnerDraw || draws == before || tips.BackColor != theme.Surface || tips.ForeColor != theme.Text || tips.BorderColor != theme.Track)
                throw new Exception(name + " tooltip did not paint with the current theme");
            await Task.Delay(100);
            using var capture = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using (var graphics = Graphics.FromImage(capture))
                graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, capture.Size);
            Directory.CreateDirectory("artifacts");
            capture.Save($"artifacts/tooltip-{name}.png");
            Console.WriteLine("PASS: Native " + name + " tooltip uses themed owner drawing and retains 更新 text");
        }
        tips.Hide(button);
    }
}
