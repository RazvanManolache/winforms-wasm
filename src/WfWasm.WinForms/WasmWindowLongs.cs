// Host-facing probe over the fork's emulated window longs (the
// PInvokeCore store is internal). The canvas host reads WS_CAPTION to
// decide whether a form gets the painted 30px caption: borderless and
// custom-chrome forms (FormBorderStyle.None, RadForm) must not get one.

namespace System.Windows.Forms;

public static class WasmWindowLongs
{
    public static int GetWindowStyle(IntPtr hwnd)
        => (int)global::Windows.Win32.PInvokeCore.GetWindowLong(
            (global::Windows.Win32.Foundation.HWND)hwnd,
            global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_LONG_PTR_INDEX.GWL_STYLE);
}
