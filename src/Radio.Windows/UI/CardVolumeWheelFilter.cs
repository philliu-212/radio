namespace Radio.Windows.UI;

// Windows routes wheel messages to the focused control or the hovered HWND,
// depending on user settings. Route by screen coordinates, not keyboard focus.
internal sealed class CardVolumeWheelFilter(Control card, VolumeSlider volume, Action commit) : IMessageFilter, IDisposable
{
    private int _remainder;
    public bool PreFilterMessage(ref Message message)
    {
        if (message.Msg != 0x020A) return false; // WM_MOUSEWHEEL
        var owner = card.FindForm();
        var target = Control.FromChildHandle(message.HWnd);
        var point = new Point(unchecked((short)message.LParam.ToInt64()), unchecked((short)(message.LParam.ToInt64() >> 16)));
        if (owner is null || !owner.Enabled || !card.Visible || !card.Enabled ||
            target?.FindForm() != owner || !card.RectangleToScreen(card.ClientRectangle).Contains(point))
        { _remainder = 0; return false; }

        _remainder += unchecked((short)(message.WParam.ToInt64() >> 16));
        var notches = _remainder / 120;
        _remainder %= 120;
        if (notches != 0)
        {
            var next = Math.Clamp(volume.Value + notches * 5, volume.Minimum, volume.Maximum);
            if (next != volume.Value) { volume.Value = next; commit(); }
        }
        return true; // Do not also scroll the list or adjust the native TrackBar.
    }
    public void Dispose() => Application.RemoveMessageFilter(this);
}
