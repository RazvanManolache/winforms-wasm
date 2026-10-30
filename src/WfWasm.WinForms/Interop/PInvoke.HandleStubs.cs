// Concrete non-generic PInvoke methods called by generic wrappers in System.Windows.Forms.Primitives.
// These break the generic recursion loops where Foo<T>(T) calls Foo(T.Handle) and resolved back to Foo<HWND>.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.Ime;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32;

internal static partial class PInvoke
{
    public static HWND ChildWindowFromPointEx(HWND hwndParent, Point pt, CWP_FLAGS uFlags) => default;
    public static BOOL ClientToScreen(HWND hWnd, ref Point lpPoint)
    {
        var origin = WasmScreenMapping.ClientOrigin(hWnd);
        lpPoint.X += origin.X;
        lpPoint.Y += origin.Y;
        return (BOOL)true;
    }
    public static BOOL CloseHandle(HANDLE hObject) => (BOOL)true;
    public static BOOL DestroyAcceleratorTable(HACCEL hAccel) => (BOOL)true;
    public static BOOL DrawMenuBar(HWND hWnd) => (BOOL)true;
    public static BOOL EnableMenuItem(HMENU hMenu, uint uIDEnableItem, MENU_ITEM_FLAGS uEnable) => (BOOL)true;
    public static BOOL EnableWindow(HWND hWnd, BOOL bEnable) => (BOOL)true;
    public static BOOL EndDialog(HWND hDlg, nint nResult) => (BOOL)true;
    public static HWND GetAncestor(HWND hwnd, GET_ANCESTOR_FLAGS flags) => default;
    public static HWND GetDlgItem(HWND hDlg, int nIDDlgItem) => default;
    public static uint GetDpiForWindow(HWND hwnd) => 96;
    public static HMENU GetMenu(HWND hWnd) => default;
    public static int GetMenuItemCount(HMENU hMenu) => 0;
    public static HWND GetParent(HWND hwnd) => default;
    public static BOOL GetScrollInfo(HWND hwnd, SCROLLBAR_CONSTANTS nBar, ref SCROLLINFO lpsi) => (BOOL)false;
    public static HMENU GetSystemMenu(HWND hwnd, BOOL bRevert) => default;
    public static HWND GetWindow(HWND hWnd, GET_WINDOW_CMD uCmd) => default;
    public static HIMC ImmGetContext(HWND hWnd) => default;
    public static BOOL ImmReleaseContext(HWND hWnd, HIMC hIMC) => (BOOL)true;
    public static BOOL InvalidateRgn(HWND hWnd, HRGN hrgn, BOOL erase) => (BOOL)true;
    public static BOOL IsChild(HWND hWndParent, HWND hWnd)
    {
        // Desktop walks the parent chain (NULL or foreign handles
        // are not children). ContainsFocus depends on this for
        // child-hosting controls such as combo editors.
        // A query must never throw: parallel test sessions (and real apps)
        // can leave a destroyed handle focused, so a disposed control in
        // the chain means "not a child", not ObjectDisposedException.
        try
        {
            global::System.Windows.Forms.Control? c =
                global::System.Windows.Forms.Control.FromHandle((IntPtr)hWnd.Value);
            while (c is not null && !c.IsDisposed)
            {
                if (c.IsHandleCreated && c.Handle == (IntPtr)hWndParent.Value)
                    return (BOOL)true;
                c = c.Parent;
            }
        }
        catch
        {
        }
        return (BOOL)false;
    }
    public static BOOL IsWindow(HWND hWnd) => (BOOL)(!hWnd.IsNull);
    public static BOOL IsWindowEnabled(HWND hWnd) => (BOOL)true;
    public static BOOL IsWindowVisible(HWND hWnd) => (BOOL)true;
    public static void NotifyWinEvent(uint @event, HWND hwnd, int idObject, int idChild) { }
    public static BOOL ScreenToClient(HWND hWnd, ref Point lpPoint)
    {
        var origin = WasmScreenMapping.ClientOrigin(hWnd);
        lpPoint.X -= origin.X;
        lpPoint.Y -= origin.Y;
        return (BOOL)true;
    }
    public static HWND SetActiveWindow(HWND hWnd) => hWnd;
    public static IntPtr SetCapture(HWND hWnd) { return (IntPtr)global::System.Windows.Forms.WasmCaptureState.Set(hWnd.Value); }
    public static BOOL SetForegroundWindow(HWND hWnd) => (BOOL)true;
    public static BOOL SetLayeredWindowAttributes(HWND hwnd, COLORREF crKey, byte bAlpha, LAYERED_WINDOW_ATTRIBUTES_FLAGS dwFlags) => (BOOL)true;
    public static BOOL SetMenu(HWND hWnd, HMENU hMenu) => (BOOL)true;
    public static HWND SetParent(HWND hWndChild, HWND hWndNewParent) => hWndNewParent;
    public static int SetScrollPos(HWND hWnd, SCROLLBAR_CONSTANTS nBar, int nPos, BOOL bRedraw) => nPos;
    [ThreadStatic]
    private static bool s_inSetWindowPos;
    [ThreadStatic]
    private static global::System.Collections.Generic.HashSet<HWND>? s_pendingBounds;
    private static readonly global::System.Reflection.MethodInfo? s_updateBoundsMethod =
        typeof(global::System.Windows.Forms.Control).GetMethod("UpdateBounds",
            global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic,
            [typeof(int), typeof(int), typeof(int), typeof(int)]);

    public static BOOL SetWindowPos(HWND hWnd, HWND hWndInsertAfter, int X, int Y, int cx, int cy, SET_WINDOW_POS_FLAGS uFlags)
    {
        WindowBoundsStorage.Update(hWnd, X, Y, cx, cy, uFlags);
        if (s_inSetWindowPos)
        {
            // Nested request: a container's layout repositions children while the
            // container's own SetWindowPos is still in flight (e.g. DockWindow layout
            // assigning pane bounds during the window's UpdateBounds). Desktop
            // processes these reentrantly until sizes converge; dropping the nested
            // UpdateBounds (previous behavior) froze control fields at stale values
            // and skipped child layout. Record it for the outermost call to drain.
            (s_pendingBounds ??= new global::System.Collections.Generic.HashSet<HWND>()).Add(hWnd);
            return (BOOL)true;
        }
        s_inSetWindowPos = true;
        try
        {
            ApplyStoredBounds(hWnd);
            // Drain nested requests iteratively (bounded: matches desktop
            // convergence via the SetBoundsCore no-change early-out; the cap
            // guards against ping-pong layouts).
            int passes = 0;
            while (s_pendingBounds is { Count: > 0 } && passes++ < 32)
            {
                global::System.Collections.Generic.HashSet<HWND> pending = s_pendingBounds;
                s_pendingBounds = new global::System.Collections.Generic.HashSet<HWND>();
                foreach (HWND queued in pending)
                {
                    ApplyStoredBounds(queued);
                }
            }
        }
        finally
        {
            s_inSetWindowPos = false;
            s_pendingBounds = null;
        }
        return (BOOL)true;
    }

    private static void ApplyStoredBounds(HWND hWnd)
    {
        try
        {
            var c = global::System.Windows.Forms.Control.FromHandle((IntPtr)hWnd.Value);
            if (c != null)
            {
                var r = WindowBoundsStorage.Get(hWnd);
                s_updateBoundsMethod?.Invoke(c, [r.X, r.Y, r.Width, r.Height]);
            }
        }
        catch { }
    }
    public static int SetWindowRgn(HWND hwnd, HRGN hrgn, BOOL fRedraw) => 1;
    public static BOOL SetWindowText(HWND hWnd, string? text)
    {
        WindowTextStorage.Set(hWnd, text);
        return (BOOL)true;
    }
    public static HRESULT SHAutoComplete(HWND hwndEdit, SHELL_AUTOCOMPLETE_FLAGS flags) => HRESULT.S_OK;
    public static BOOL ShowWindow(HWND hWnd, SHOW_WINDOW_CMD nCmdShow)
    {
        // Native ShowWindow synchronously sends WM_SHOWWINDOW; that message
        // is what runs Control.CreateControl and Form.Load on show. Deliver
        // it so the show lifecycle happens (see NativeWindow.Wasm.cs).
        bool shown = nCmdShow != SHOW_WINDOW_CMD.SW_HIDE;
        global::System.Windows.Forms.NativeWindow.WasmNotifyShowWindow(hWnd, shown);
        return (BOOL)true;
    }
    public static unsafe BOOL InvalidateRect(HWND hWnd, RECT* lpRect, BOOL bErase) => (BOOL)true;
    public static unsafe bool IsAccelerator(HACCEL hAccel, int cAccelEntries, MSG* lpMsg, ushort* lpwCmd) => false;
    public static unsafe BOOL RedrawWindow(HWND hWnd, RECT* lprcUpdate, HRGN hrgnUpdate, REDRAW_WINDOW_FLAGS flags) => (BOOL)true;
    public static unsafe BOOL ScrollWindow(HWND hWnd, int XAmount, int YAmount, RECT* lpRect, RECT* rectClip) => (BOOL)true;
    public static BOOL UpdateWindow(HWND hWnd) => (BOOL)true;
    public static unsafe BOOL ValidateRect(HWND hWnd, RECT* lpRect) => (BOOL)true;
}
