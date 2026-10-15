// Managed extension for NativeWindow window creation hook.

using Windows.Win32.Foundation;
using Windows.Win32;

namespace System.Windows.Forms;

public unsafe partial class NativeWindow
{
    internal static void DispatchCreateWindow(HWND hwnd, char* lpClassName)
    {
        string? className = (nuint)lpClassName > 0xFFFF ? new string(lpClassName) : null;
        for (var wc = WindowClass.s_cache; wc != null; wc = wc._next)
        {
            if (wc._targetWindow is not null && wc._targetWindow.HWND.IsNull)
            {
                if (className == null || string.Equals(wc._windowClassName, className, StringComparison.Ordinal))
                {
                    wc.Callback(hwnd, PInvokeCore.WM_NCCREATE, (WPARAM)0, (LPARAM)0);
                    break;
                }
            }
        }
    }
}
