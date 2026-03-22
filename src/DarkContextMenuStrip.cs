using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PcUsageTimer;

public sealed class DarkContextMenuStrip : ContextMenuStrip
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        int preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
