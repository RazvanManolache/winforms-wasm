// Pure managed window longs and class longs for canvas-hosted WinForms.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32;

internal static partial class PInvokeCore
{
    private static readonly ConcurrentDictionary<(nint Hwnd, WINDOW_LONG_PTR_INDEX Index), nint> s_windowLongs = new();
    private static readonly ConcurrentDictionary<(nint Hwnd, GET_CLASS_LONG_INDEX Index), nint> s_classLongs = new();

    public static IntPtr GetClassLong(HWND hWnd, GET_CLASS_LONG_INDEX nIndex)
    {
        s_classLongs.TryGetValue(((nint)hWnd.Value, nIndex), out var val);
        return val;
    }

    public static nint SetClassLong(HWND hWnd, GET_CLASS_LONG_INDEX nIndex, nint dwNewLong)
    {
        var key = ((nint)hWnd.Value, nIndex);
        s_classLongs.TryGetValue(key, out var old);
        s_classLongs[key] = dwNewLong;
        return old;
    }

    public static nint GetWindowLong<T>(T hWnd, WINDOW_LONG_PTR_INDEX nIndex)
        where T : IHandle<HWND>
    {
        if (s_windowLongs.TryGetValue(((nint)hWnd.Handle.Value, nIndex), out var val))
        {
            if (nIndex == WINDOW_LONG_PTR_INDEX.GWL_WNDPROC && val == 0)
            {
                return 1;
            }
            return val;
        }
        if (nIndex == WINDOW_LONG_PTR_INDEX.GWL_WNDPROC)
        {
            return 1;
        }
        return 0;
    }

    public static nint SetWindowLong<T>(T hWnd, WINDOW_LONG_PTR_INDEX nIndex, nint newValue)
        where T : IHandle<HWND>
    {
        if (nIndex == WINDOW_LONG_PTR_INDEX.GWL_WNDPROC && newValue == 0)
        {
            newValue = 1;
        }
        var key = ((nint)hWnd.Handle.Value, nIndex);
        s_windowLongs.TryGetValue(key, out var old);
        s_windowLongs[key] = newValue;
        return old;
    }

    public static nint SetWindowLong<THwnd, TNewValue>(THwnd hWnd, WINDOW_LONG_PTR_INDEX nIndex, TNewValue newValue)
        where THwnd : IHandle<HWND>
        where TNewValue : IHandle<HWND>
    {
        return SetWindowLong(hWnd, nIndex, (nint)newValue.Handle.Value);
    }

    private static int s_procCounter = 100;

    public static nint SetWindowLong<T>(T hWnd, WINDOW_LONG_PTR_INDEX nIndex, WNDPROC dwNewLong)
        where T : IHandle<HWND>
    {
        nint newHandle = (nint)Interlocked.Increment(ref s_procCounter);
        return SetWindowLong(hWnd, nIndex, newHandle);
    }

    internal static unsafe HRESULT PrintDlgEx(PRINTDLGEXW* pPD) => HRESULT.S_OK;
}
