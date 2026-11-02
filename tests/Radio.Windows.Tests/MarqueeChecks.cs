using Radio.Windows.UI;

namespace Radio.Windows.Tests;

internal static class MarqueeChecks
{
    internal static void Run()
    {
        using var form = new Form { ClientSize = new Size(220, 60) };
        using var label = new MarqueeLabel { Text = "短文字" };
        form.Controls.Add(label); form.Show();
        Check(!label.AnimationRunning && label.ScrollOffset == 0, "Short metadata stays still");
        const string text = "測試歌曲／歌手／DJ／節目：這是只用於離線驗證的超長文字 ABC & DEF，確認尾端能完整顯示";
        label.Text = text;
        Check(label.AnimationRunning && label.OverflowWidth > 0, "Overflow starts metadata animation");
        label.AdvanceAnimation(1200);
        Check(label.ScrollOffset == 0, "Marquee pauses at the beginning");
        label.AdvanceAnimation(1000);
        Check(Math.Abs(label.ScrollOffset - 24 * label.DeviceDpi / 96d) < .01, "Marquee moves slowly at DPI-scaled speed");
        var offset = label.ScrollOffset;
        label.Text = text;
        Check(label.ScrollOffset == offset, "Unchanged metadata does not restart the animation");
        label.AdvanceAnimation(100000);
        Check(label.ScrollOffset == label.OverflowWidth, "Marquee reaches the full text tail");
        using (var bitmap = new Bitmap(label.Width, label.Height)) label.DrawToBitmap(bitmap, label.ClientRectangle);
        label.AdvanceAnimation(1799);
        Check(label.ScrollOffset == label.OverflowWidth, "Marquee pauses at the tail");
        label.AdvanceAnimation(2);
        Check(label.ScrollOffset == 0, "Marquee loops back to the beginning");
        label.AnimationEnabled = false;
        label.AdvanceAnimation(5000);
        Check(!label.AnimationRunning && label.ScrollOffset == 0, "Suspended presentation stops the timer");
        label.AnimationEnabled = true;
        Check(label.AnimationRunning, "Restored presentation restarts overflow animation");
        form.Hide();
        Check(!label.AnimationRunning, "Hidden parent stops animation");
        form.Show();
        Check(label.AnimationRunning && label.ScrollOffset == 0, "Showing parent resets animation");
        form.WindowState = FormWindowState.Minimized;
        Check(!label.AnimationRunning, "Native minimization stops animation");
        form.WindowState = FormWindowState.Normal;
        Check(label.AnimationRunning && label.ScrollOffset == 0, "Restoring from minimization resets animation");
        label.AdvanceAnimation(2200);
        form.ClientSize = new Size(240, 60);
        Check(label.ScrollOffset == 0, "Resizing resets the viewport");
        using var font = new Font(label.Font.FontFamily, 12);
        label.AdvanceAnimation(2200); label.Font = font;
        Check(label.ScrollOffset == 0 && label.AnimationRunning, "Font changes remeasure and restart");
        label.Text = "短文字";
        Check(!label.AnimationRunning && label.ScrollOffset == 0, "New short metadata clears scrolling");
        label.Text = text; form.ClientSize = new Size(2000, 60);
        Check(!label.AnimationRunning, "Widening to fit stops animation");
        label.Text = "";
        Check(!label.AnimationRunning, "Empty metadata never animates");
        label.Dispose();
        Check(!label.AnimationRunning, "Disposal releases the animation timer");
        form.Close();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
