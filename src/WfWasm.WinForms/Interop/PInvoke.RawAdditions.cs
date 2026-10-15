// Fork-owned: raw PInvoke entry points that the upstream wrapper files (PInvoke.*.cs) call into.

using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Ole;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32
{
    internal static unsafe partial class PInvoke
    {
        public static BOOL GetTextExtentPoint32W(HDC hdc, char* lpString, int c, SIZE* lpSize) => (BOOL)true;
        public static BOOL GetTextExtentPoint32W(HDC hdc, PCWSTR lpString, int c, SIZE* lpSize) => (BOOL)true;

        public static HHOOK SetWindowsHookEx(WINDOWS_HOOK_ID idHook, delegate* unmanaged[Stdcall]<int, WPARAM, LPARAM, LRESULT> lpfn, HINSTANCE hmod, uint dwThreadId) => default;

        public static HWINEVENTHOOK SetWinEventHook(uint eventMin, uint eventMax, HMODULE hmodWinEventProc, delegate* unmanaged[Stdcall]<HWINEVENTHOOK, uint, HWND, int, int, uint, uint, void> pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags) => default;

        public static HRESULT UiaGetReservedNotSupportedValue(global::Windows.Win32.System.Com.IUnknown** punkNotSupportedValue) => HRESULT.S_OK;

        public static HRESULT OleCreatePropertyFrame(HWND hwndOwner, uint x, uint y, string? lpszCaption, uint cObjects, global::Windows.Win32.System.Com.IUnknown** ppUnk, uint cPages, in Guid pPageClsID, uint lcid, uint dwReserved = 0, void* pvReserved = null) => HRESULT.S_OK;

        public static HRESULT StgOpenStorageOnILockBytes(global::Windows.Win32.System.Com.ILockBytes* plkbyt, global::Windows.Win32.System.Com.IStorage* pstgPriority, global::Windows.Win32.System.Com.STGM grfMode, void* snbExclude, uint reserved, global::Windows.Win32.System.Com.IStorage** ppstgOpen) => HRESULT.E_FAIL;

        public static BOOL KillTimer(HWND hWnd, nuint uIDEvent) => (BOOL)true;

        public static HRESULT DrawThemeBackground(global::Windows.Win32.UI.Controls.HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pRect, RECT* pClipRect) => HRESULT.S_OK;

        public static HRESULT DrawThemeText(global::Windows.Win32.UI.Controls.HTHEME hTheme, HDC hdc, int iPartId, int iStateId, string pszText, int cchText, DRAW_TEXT_FORMAT dwTextFlags, uint dwTextFlags2, in RECT pRect) => HRESULT.S_OK;

        public static BOOL GetTextExtentPoint32W(HDC hdc, char* lpString, int c, SIZE* lpSize, int _dummy) => (BOOL)true;
        public static BOOL EnableScrollBar(HWND hWnd, uint wSBflags, ENABLE_SCROLL_BAR_ARROWS wArrows) => (BOOL)true;

        private static nint s_hwndCounter = 0x10000;
        [ThreadStatic]
        private static int s_createDepth = 0;

        public static HWND CreateWindowEx(
            WINDOW_EX_STYLE dwExStyle,
            char* lpClassName,
            char* lpWindowName,
            WINDOW_STYLE dwStyle,
            int X,
            int Y,
            int nWidth,
            int nHeight,
            HWND hWndParent,
            HMENU hMenu,
            HINSTANCE hInstance,
            void* lpParam)
        {
            if (++s_createDepth > 5)
            {
                s_createDepth = 0;
                throw new InvalidOperationException("CreateWindowEx recursion detected:\n" + Environment.StackTrace);
            }
            try
            {
                var hwnd = (HWND)(++s_hwndCounter);
                if ((nuint)lpWindowName > 0xFFFF)
                {
                    WindowTextStorage.Set(hwnd, new string(lpWindowName));
                }
                WindowBoundsStorage.Set(hwnd, X, Y, nWidth, nHeight);
                global::System.Windows.Forms.NativeWindow.DispatchCreateWindow(hwnd, lpClassName);
                return hwnd;
            }
            finally
            {
                s_createDepth--;
            }
        }

        public static LRESULT UiaReturnRawElementProvider(HWND hwnd, WPARAM wParam, LPARAM lParam, IRawElementProviderSimple* el) => default;

        public static HRESULT UiaDisconnectProvider(IRawElementProviderSimple* pProvider) => HRESULT.S_OK;
    }

    internal static unsafe partial class PInvokeCore
    {
        public static HBRUSH GetSysColorBrush(SYS_COLOR_INDEX nIndex) => (HBRUSH)(nint)1;

        public static HRESULT RegisterDragDrop(HWND hwnd, IDropTarget* pDropTarget) => HRESULT.S_OK;
    }
}
