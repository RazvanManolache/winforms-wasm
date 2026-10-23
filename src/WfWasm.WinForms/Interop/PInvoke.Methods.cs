// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Printing;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;
using Windows.Win32.System.WinRT;
using Windows.Win32.System.StationsAndDesktops;
using Windows.Win32.Media.Audio;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.Ime;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32
{
    internal static partial class PInvoke
    {
        // Batch 1
        public static HKL ActivateKeyboardLayout(HKL hkl, uint flags) => hkl;
        public static BOOL AdjustWindowRectEx(ref RECT lpRect, WINDOW_STYLE dwStyle, BOOL bMenu, WINDOW_EX_STYLE dwExStyle) => (BOOL)true;
        public static unsafe BOOL AdjustWindowRectEx(RECT* lpRect, WINDOW_STYLE dwStyle, BOOL bMenu, WINDOW_EX_STYLE dwExStyle) => (BOOL)true;
        public static BOOL AdjustWindowRectExForDpi(ref RECT lpRect, WINDOW_STYLE dwStyle, BOOL bMenu, WINDOW_EX_STYLE dwExStyle, uint dpi) => (BOOL)true;
        public static unsafe BOOL AdjustWindowRectExForDpi(RECT* lpRect, WINDOW_STYLE dwStyle, BOOL bMenu, WINDOW_EX_STYLE dwExStyle, uint dpi) => (BOOL)true;
        public static BOOL AreDpiAwarenessContextsEqual(DPI_AWARENESS_CONTEXT dpiContextA, DPI_AWARENESS_CONTEXT dpiContextB) => (BOOL)(dpiContextA.Value == dpiContextB.Value);
        public static LRESULT CallNextHookEx(HHOOK hhk, int nCode, WPARAM wParam, LPARAM lParam) => default;
        public static unsafe BOOL ClipCursor(RECT* lpRect) => (BOOL)true;
        public static BOOL ClipCursor(in RECT lpRect) => (BOOL)true;
        public static BOOL CloseDesktop(HDESK hDesktop) => (BOOL)true;
        public static HRESULT CloseThemeData(HTHEME hTheme) => HRESULT.S_OK;
        public static unsafe HRESULT CoGetClassObject(Guid* rclsid, CLSCTX dwClsContext, void* pvReserved, Guid* riid, void** ppv) => HRESULT.E_NOINTERFACE;
        public static unsafe HRESULT CoGetClassObject(in Guid rclsid, CLSCTX dwClsContext, void* pvReserved, in Guid riid, void** ppv) => HRESULT.E_NOINTERFACE;
        public static unsafe HRESULT CoRegisterMessageFilter(IMessageFilter* lpMessageFilter, IMessageFilter** lplpMessageFilter) => HRESULT.S_OK;
        public static unsafe HRESULT CoRegisterMessageFilter(void* lpMessageFilter, void** lplpMessageFilter) => HRESULT.S_OK;
        public static COMMON_DLG_ERRORS CommDlgExtendedError() => COMMON_DLG_ERRORS.CDERR_GENERALCODES;
        public static unsafe HACCEL CreateAcceleratorTable(ACCEL* paccel, int cAccel) => default;
        public static unsafe HBRUSH CreateBrushIndirect(LOGBRUSH* plbrush) => default;
        public static HBRUSH CreateBrushIndirect(in LOGBRUSH plbrush) => default;
        public static HPALETTE CreateHalftonePalette(HDC hdc) => default;
        public static HMENU CreateMenu() => default;
        public static unsafe HRESULT CreateOleAdviseHolder(IOleAdviseHolder** ppOAHolder) => HRESULT.S_OK;
        public static unsafe HRESULT CreateOleAdviseHolder(void** ppOAHolder) => HRESULT.S_OK;
        public static HBRUSH CreatePatternBrush(HBITMAP hbm) => default;
        public static HRGN CreateRectRgn(int x1, int y1, int x2, int y2) => default;
        public static unsafe HRESULT CreateStdAccessibleObject(HWND hwnd, int idObject, Guid* riid, void** ppvObj) => HRESULT.E_NOTIMPL;
        public static unsafe HRESULT CreateStdAccessibleObject(HWND hwnd, int idObject, in Guid riid, void** ppvObj) => HRESULT.E_NOTIMPL;
        public static LRESULT DefFrameProc(HWND hWnd, HWND hWndMDIClient, uint uMsg, WPARAM wParam, LPARAM lParam) => default;
        public static LRESULT DefMDIChildProc(HWND hWnd, uint uMsg, WPARAM wParam, LPARAM lParam) => default;
        public static BOOL DestroyCursor(HCURSOR hCursor) => (BOOL)true;
        public static BOOL DestroyMenu(HMENU hMenu) => (BOOL)true;
        public static BOOL DestroyWindow(HWND hWnd)
        {
            // Native DestroyWindow synchronously sends WM_DESTROY; the fork
            // relies on that message to stash per-handle state (notably
            // Control.Text into _text) before the handle is released.
            global::System.Windows.Forms.NativeWindow.WasmNotifyDestroyWindow(hWnd);
            return (BOOL)true;
        }
        public static unsafe LRESULT DispatchMessage(MSG* lpMsg) => default;
        public static LRESULT DispatchMessage(in MSG lpMsg) => default;
        public static unsafe int DocumentProperties(HWND hWnd, PRINTER_HANDLE hPrinter, PCWSTR pDeviceName, DEVMODEW* pDevModeOutput, DEVMODEW* pDevModeInput, uint fMode) => 0;
        public static BOOL DrawEdge(HDC hdc, ref RECT qrc, DRAWEDGE_FLAGS edge, DRAW_EDGE_FLAGS grfFlags) => (BOOL)true;
        public static unsafe BOOL DrawEdge(HDC hdc, RECT* qrc, DRAWEDGE_FLAGS edge, DRAW_EDGE_FLAGS grfFlags) => (BOOL)true;
        public static unsafe HRESULT DrawThemeBackground(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* pRect, RECT* pClipRect) => HRESULT.S_OK;
        public static HRESULT DrawThemeBackground(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pRect, in RECT pClipRect) => HRESULT.S_OK;
        public static unsafe HRESULT DrawThemeEdge(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* pDestRect, uint uEdge, uint uFlags, RECT* pContentRect) => HRESULT.S_OK;
        public static unsafe HRESULT DrawThemeEdge(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pDestRect, DRAWEDGE_FLAGS uEdge, DRAW_EDGE_FLAGS uFlags, out RECT pContentRect) { pContentRect = default; return HRESULT.S_OK; }
        public static unsafe HRESULT DrawThemeEdge(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pDestRect, uint uEdge, uint uFlags, out RECT pContentRect) { pContentRect = default; return HRESULT.S_OK; }
        public static unsafe HRESULT DrawThemeParentBackground(HWND hwnd, HDC hdc, RECT* prc) => HRESULT.S_OK;
        public static HRESULT DrawThemeParentBackground(HWND hwnd, HDC hdc, in RECT prc) => HRESULT.S_OK;

        // Batch 2
        public static unsafe HRESULT DrawThemeText(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, PCWSTR pszText, int cchText, uint dwTextFlags, uint dwTextFlags2, RECT* pRect)
        {
            if (pszText.Value != null && pRect != null && cchText != 0)
            {
                int len = cchText;
                if (len < 0)
                {
                    char* p = pszText.Value;
                    len = 0;
                    while (*p != '\0') { len++; p++; }
                }

                if (len > 0)
                {
                    WasmTextBridge.TryDrawThemeText(hdc, new ReadOnlySpan<char>(pszText.Value, len), *pRect, dwTextFlags);
                }
            }

            return HRESULT.S_OK;
        }
        public static unsafe int DrawTextEx(HDC hdc, PWSTR lpchText, int cchText, RECT* lprc, DRAW_TEXT_FORMAT format, DRAWTEXTPARAMS* lpdtp)
        {
            if (lprc == null) return 0;
            if (lpchText.Value == null || cchText == 0)
            {
                if (format.HasFlag(DRAW_TEXT_FORMAT.DT_CALCRECT))
                {
                    lprc->right = lprc->left;
                    lprc->bottom = lprc->top;
                }
                return 0;
            }

            int len = cchText;
            if (len < 0)
            {
                char* p = lpchText.Value;
                len = 0;
                while (*p != '\0') { len++; p++; }
            }
            if (len == 0)
            {
                if (format.HasFlag(DRAW_TEXT_FORMAT.DT_CALCRECT))
                {
                    lprc->right = lprc->left;
                    lprc->bottom = lprc->top;
                }
                return 0;
            }

            ReadOnlySpan<char> text = new(lpchText.Value, len);
            if (WasmTextBridge.TryDrawText(hdc, text, lprc, format, lpdtp, out int bridgeHeight))
            {
                return bridgeHeight;
            }
            int leftMargin = (lpdtp != null) ? lpdtp->iLeftMargin : 0;
            int rightMargin = (lpdtp != null) ? lpdtp->iRightMargin : 0;
            int wrapWidth = (lprc != null) ? lprc->right - lprc->left : 0;
            int totalHeight = WasmTextBridge.MeasureHeuristic(text, wrapWidth, format, leftMargin, rightMargin, out int totalWidth);

            if (format.HasFlag(DRAW_TEXT_FORMAT.DT_CALCRECT))
            {
                lprc->right = lprc->left + Math.Max(1, totalWidth);
                lprc->bottom = lprc->top + totalHeight;
            }

            return totalHeight;
        }
        public static unsafe HRESULT DrawThemeText(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, string pszText, int cchText, uint dwTextFlags, uint dwTextFlags2, ref RECT pRect)
        {
            if (!string.IsNullOrEmpty(pszText))
            {
                WasmTextBridge.TryDrawThemeText(hdc, pszText.AsSpan(), pRect, dwTextFlags);
            }

            return HRESULT.S_OK;
        }
        public static unsafe BOOL DuplicateHandle(HANDLE hSourceProcessHandle, HANDLE hSourceHandle, HANDLE hTargetProcessHandle, HANDLE* lpTargetHandle, uint dwDesiredAccess, BOOL bInheritHandle, DUPLICATE_HANDLE_OPTIONS dwOptions) => (BOOL)true;
        public static unsafe HRESULT DwmGetWindowAttribute(HWND hwnd, DWMWINDOWATTRIBUTE dwAttribute, void* pvAttribute, uint cbAttribute) => HRESULT.S_OK;
        public static unsafe HRESULT DwmSetWindowAttribute(HWND hwnd, DWMWINDOWATTRIBUTE dwAttribute, void* pvAttribute, uint cbAttribute) => HRESULT.S_OK;
        public static BOOL Ellipse(HDC hdc, int left, int top, int right, int bottom) => (BOOL)true;
        public static unsafe BOOL EnumDisplaySettings(PCWSTR lpszDeviceName, ENUM_DISPLAY_SETTINGS_MODE iModeNum, DEVMODEW* lpDevMode) => (BOOL)false;
        public static BOOL EnumDisplaySettings(string? lpszDeviceName, ENUM_DISPLAY_SETTINGS_MODE iModeNum, ref DEVMODEW lpDevMode) => (BOOL)false;
        public static unsafe BOOL ExtTextOut(HDC hdc, int x, int y, ETO_OPTIONS options, RECT* lprect, PCWSTR lpString, uint c, int* lpDx)
        {
            if (lpString.Value != null && c > 0)
            {
                WasmTextBridge.TryExtTextOut(hdc, x, y, options, lprect, new ReadOnlySpan<char>(lpString.Value, (int)c));
            }

            return (BOOL)true;
        }
        public static int FillRect(HDC hDC, in RECT lprc, HBRUSH hbr) => WasmFillBridge.TryFill(hDC, lprc, hbr);
        public static unsafe int FillRect(HDC hDC, RECT* lprc, HBRUSH hbr) => lprc is null ? 0 : WasmFillBridge.TryFill(hDC, *lprc, hbr);
        public static int FillRect<T>(T hDC, RECT lprc, HBRUSH hbr) where T : IHandle<HDC>
        {
            HDC hdc = default;
            try { hdc = hDC.Handle; } catch { }
            return WasmFillBridge.TryFill(hdc, lprc, hbr);
        }
        public static unsafe HINSTANCE FindExecutable(PCWSTR lpFile, PCWSTR lpDirectory, PWSTR lpResult) => default;
        public static unsafe HINSTANCE FindExecutable(char* lpFile, char* lpDirectory, char* lpResult) => default;
        public static unsafe HWND FindWindow(PCWSTR lpClassName, PCWSTR lpWindowName) => default;
        public static unsafe HWND FindWindow(string? lpClassName, string? lpWindowName) => default;
        public static unsafe uint FormatMessage(FORMAT_MESSAGE_OPTIONS dwFlags, void* lpSource, uint dwMessageId, uint dwLanguageId, PWSTR lpBuffer, uint nSize, void* Arguments) => 0;
        public static HWND GetActiveWindow() => new HWND(global::System.Windows.Forms.WasmForeground.Current);
        // Async state matches sync state headless (no message-queue edge bits):
        // host-fed buttons + Shift/Ctrl/Alt read pressed, the rest read 0.
        public static short GetAsyncKeyState(int vKey) => GetKeyState(vKey);
        public static DPI_AWARENESS GetAwarenessFromDpiAwarenessContext(DPI_AWARENESS_CONTEXT value) => DPI_AWARENESS.DPI_AWARENESS_PER_MONITOR_AWARE;
        public static unsafe int GetBitmapBits(HBITMAP hbit, int cb, void* lpvBits) => 0;
        public static COLORREF GetBkColor(HDC hdc) => default;
        public static BACKGROUND_MODE GetBkMode(HDC hdc) => BACKGROUND_MODE.OPAQUE;
        public static HWND GetCapture() => new HWND(global::System.Windows.Forms.WasmCaptureState.Current);
        public static uint GetCaretBlinkTime() => 500;
        public static unsafe BOOL GetCaretPos(Point* lpPoint) => (BOOL)true;
        public static BOOL GetCaretPos(out Point lpPoint) { lpPoint = default; return (BOOL)true; }
        public static unsafe int GetClassName(HWND hWnd, PWSTR lpClassName, int nMaxCount) => 0;
        public static unsafe int GetClassName(HWND hWnd, char* lpClassName, int nMaxCount) => 0;
        public static unsafe BOOL GetClassInfo(HINSTANCE hInstance, PCWSTR lpClassName, WNDCLASSW* lpWndClass)
        {
            if (lpWndClass != null)
                lpWndClass->lpfnWndProc = (delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT>)(void*)1;
            return (BOOL)true;
        }
        public static unsafe BOOL GetClipCursor(RECT* lpRect) => (BOOL)true;
        public static BOOL GetClipCursor(out RECT lpRect) { lpRect = default; return (BOOL)true; }
        public static HGDIOBJ GetCurrentObject(HDC hdc, OBJ_TYPE type) => default;
        public static HANDLE GetCurrentProcess() => (HANDLE)(nint)(-1);
        public static uint GetCurrentProcessId() => 1;
        public static unsafe HRESULT GetCurrentThemeName(PWSTR pszThemeFileName, int cchMaxNameChars, PWSTR pszColorBuff, int cchMaxColorChars, PWSTR pszSizeBuff, int cchMaxSizeChars) => HRESULT.S_OK;
        public static HANDLE GetCurrentThread() => (HANDLE)(nint)(-2);
        public static HCURSOR GetCursor() => default;
        public static unsafe BOOL GetCursorPos(Point* lpPoint) { if (lpPoint != null) *lpPoint = global::System.Windows.Forms.WasmMouseState.Position; return (BOOL)true; }
        public static BOOL GetCursorPos(out Point lpPoint) { lpPoint = global::System.Windows.Forms.WasmMouseState.Position; return (BOOL)true; }

        // Batch 3
        public static unsafe int GetDIBits(HDC hdc, HBITMAP hbm, uint start, uint cLines, void* lpvBits, BITMAPINFO* lpbmi, DIB_USAGE usage) => 0;
        public static uint GetDoubleClickTime() => 500;
        public static uint GetDpiForSystem() => 96;
        public static unsafe HRESULT GetErrorInfo(uint dwReserved, IErrorInfo** pperrinfo) => HRESULT.S_FALSE;
        public static unsafe BOOL GetExitCodeThread(HANDLE hThread, uint* lpExitCode) => (BOOL)true;
        public static HWND GetFocus() => default;
        public static unsafe HRESULT GetHGlobalFromILockBytes(void* plkbyt, HGLOBAL* phglobal) => HRESULT.S_OK;
        public static unsafe int GetKeyboardLayoutList(int nBuff, HKL* lpList) => 0;
        public static unsafe BOOL GetKeyboardState(byte* lpKeyState) => (BOOL)true;
        public static unsafe int GetLocaleInfoEx(PCWSTR lpLocaleName, uint LCType, PWSTR lpLCData, int cchData) => 0;
        public static HDC_MAP_MODE GetMapMode(HDC hdc) => HDC_MAP_MODE.MM_TEXT;
        public static unsafe BOOL GetMenuItemInfo(HMENU hmenu, uint item, BOOL fByPosition, MENUITEMINFOW* lpmii) => (BOOL)false;
        public static BOOL GetMenuItemInfo(HMENU hmenu, uint item, BOOL fByPosition, ref MENUITEMINFOW lpmii) => (BOOL)false;
        public static unsafe BOOL GetMessage(MSG* lpMsg, HWND hWnd, uint wMsgFilterMin, uint wMsgFilterMax) => (BOOL)true;
        public static uint GetMessagePos() => 0;
        public static COLORREF GetNearestColor(HDC hdc, COLORREF color) => color;
        public static unsafe BOOL GetPhysicalCursorPos(Point* lpPoint) => (BOOL)true;
        public static BOOL GetPhysicalCursorPos(out Point lpPoint) { lpPoint = Point.Empty; return (BOOL)true; }
        public static unsafe FARPROC GetProcAddress(HMODULE hModule, PCSTR lpProcName) => (FARPROC)(nint)1;
        public static unsafe FARPROC GetProcAddress(HINSTANCE hModule, PCSTR lpProcName) => (FARPROC)(nint)1;
        public static unsafe HRESULT GetProcessDpiAwareness(HANDLE hprocess, PROCESS_DPI_AWARENESS* value) { if (value != null) *value = PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE; return HRESULT.S_OK; }
        public static HRESULT GetProcessDpiAwareness(HANDLE hprocess, out PROCESS_DPI_AWARENESS value) { value = PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE; return HRESULT.S_OK; }
        public static HWINSTA GetProcessWindowStation() => (HWINSTA)(IntPtr)1;
        public static R2_MODE GetROP2(HDC hdc) => R2_MODE.R2_COPYPEN;
        public static unsafe GDI_REGION_TYPE GetRgnBox(HRGN hrgn, RECT* lprc) => GDI_REGION_TYPE.SIMPLEREGION;
        public static GDI_REGION_TYPE GetRgnBox(HRGN hrgn, out RECT lprc) { lprc = default; return GDI_REGION_TYPE.SIMPLEREGION; }
        public static unsafe BOOL GetScrollBarInfo(HWND hwnd, uint idObject, SCROLLBARINFO* psbi) => (BOOL)false;
        public static BOOL GetScrollBarInfo(HWND hwnd, uint idObject, ref SCROLLBARINFO psbi) => (BOOL)false;
        public static unsafe BOOL GetScrollBarInfo(HWND hwnd, OBJECT_IDENTIFIER idObject, SCROLLBARINFO* psbi) => (BOOL)false;
        public static BOOL GetScrollBarInfo(HWND hwnd, OBJECT_IDENTIFIER idObject, ref SCROLLBARINFO psbi) => (BOOL)false;
        public static unsafe uint GetShortPathName(PCWSTR lpszLongPath, PWSTR lpszShortPath, uint cchBuffer) => 0;
        public static uint GetShortPathName(string lpszLongPath, Span<char> lpszShortPath) => 0;
        public static uint GetShortPathName(PCWSTR lpszLongPath, Span<char> lpszShortPath) => 0;
        public static unsafe int DrawText(HDC hdc, char* lpchText, int cchText, ref RECT lprc, DRAW_TEXT_FORMAT format)
        {
            fixed (RECT* pRect = &lprc)
            {
                return DrawTextEx(hdc, (PWSTR)lpchText, cchText, pRect, format, null);
            }
        }
        public static HWND SetFocus(HWND hWnd = default) => default;
        public static void GetStartupInfo(out STARTUPINFOW lpStartupInfo) => lpStartupInfo = default;
        public static int GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX nIndex, uint dpi) => PInvokeCore.GetSystemMetrics(nIndex);
        public static unsafe uint GetSystemPaletteEntries(HDC hdc, uint iStart, uint cEntries, PALETTEENTRY* pEntries) => 0;
        public static COLORREF GetTextColor(HDC hdc) => WasmTextBridge.GetTextColor(hdc);
        public static unsafe BOOL GetTextMetrics(HDC hdc, void* lptm)
        {
            if (lptm != null)
            {
                var tm = (Windows.Win32.Graphics.Gdi.TEXTMETRICW*)lptm;
                global::System.Drawing.Font font = WasmTextBridge.SelectedFont(hdc)
                    ?? global::System.Drawing.SystemFonts.DefaultFont;
                float em = font.Unit == global::System.Drawing.GraphicsUnit.Pixel
                    ? font.Size
                    : font.Size * 96f / 72f;
                global::System.Drawing.GdiTmRow row = global::System.Drawing.GdiTextMetrics.GetRow(font.Name, em);
                tm->tmHeight = row.Height;
                tm->tmAscent = row.Ascent;
                tm->tmDescent = row.Descent;
                tm->tmInternalLeading = Math.Max(0, row.Height - (int)em);
                tm->tmExternalLeading = row.ExternalLeading;
                tm->tmAveCharWidth = row.AveWidth;
                tm->tmMaxCharWidth = row.MaxWidth;
                tm->tmPitchAndFamily = (Windows.Win32.Graphics.Gdi.TMPF_FLAGS)global::System.Drawing.GdiTextMetrics.GetPitchAndFamily(font.Name);
            }
            return (BOOL)true;
        }
        public static uint GetThemeAppProperties() => 0;
        public static unsafe HRESULT GetThemeBackgroundContentRect(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* pBoundingRect, RECT* pContentRect) => HRESULT.S_OK;
        public static HRESULT GetThemeBackgroundContentRect(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pBoundingRect, out RECT pContentRect) { pContentRect = default; return HRESULT.S_OK; }

        // Batch 4
        public static unsafe HRESULT GetThemeBackgroundExtent(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* pContentRect, RECT* pExtentRect) => HRESULT.S_OK;
        public static HRESULT GetThemeBackgroundExtent(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pContentRect, out RECT pExtentRect) { pExtentRect = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeBackgroundRegion(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* pRect, HRGN* pRegion) => HRESULT.S_OK;
        public static HRESULT GetThemeBackgroundRegion(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT pRect, out HRGN pRegion) { pRegion = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeBool(HTHEME hTheme, int iPartId, int iStateId, int iPropId, BOOL* pfVal) => HRESULT.S_OK;
        public static HRESULT GetThemeBool(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, out BOOL pfVal) { pfVal = (BOOL)false; return HRESULT.S_OK; }
        public static HRESULT GetThemeBool(HTHEME hTheme, int iPartId, int iStateId, int iPropId, out BOOL pfVal) { pfVal = (BOOL)false; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeColor(HTHEME hTheme, int iPartId, int iStateId, int iPropId, COLORREF* pColor) => HRESULT.S_OK;
        public static HRESULT GetThemeColor(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, out COLORREF pColor) { pColor = default; return HRESULT.S_OK; }
        public static HRESULT GetThemeColor(HTHEME hTheme, int iPartId, int iStateId, int iPropId, out COLORREF pColor) { pColor = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeEnumValue(HTHEME hTheme, int iPartId, int iStateId, int iPropId, int* piVal) => HRESULT.S_OK;
        public static HRESULT GetThemeEnumValue(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, out int piVal) { piVal = 0; return HRESULT.S_OK; }
        public static HRESULT GetThemeEnumValue(HTHEME hTheme, int iPartId, int iStateId, int iPropId, out int piVal) { piVal = 0; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeFont(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, int iPropId, LOGFONTW* pFont) => HRESULT.S_OK;
        public static HRESULT GetThemeFont(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, int iPropId, out LOGFONTW pFont) { pFont = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeFilename(HTHEME hTheme, int iPartId, int iStateId, int iPropId, PWSTR pszThemeFileName, int cchMaxBuffChars) => HRESULT.S_OK;
        public static HRESULT GetThemeFilename(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, PWSTR pszThemeFileName, int cchMaxBuffChars) => HRESULT.S_OK;
        public static unsafe HRESULT GetThemeInt(HTHEME hTheme, int iPartId, int iStateId, int iPropId, int* piVal) => HRESULT.S_OK;
        public static HRESULT GetThemeInt(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, out int piVal) { piVal = 0; return HRESULT.S_OK; }
        public static HRESULT GetThemeInt(HTHEME hTheme, int iPartId, int iStateId, int iPropId, out int piVal) { piVal = 0; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeMargins(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, int iPropId, RECT* prc, MARGINS* pMargins) => HRESULT.S_OK;
        public static unsafe HRESULT GetThemeMargins(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, RECT* prc, out MARGINS pMargins) { pMargins = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeMargins(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, int iPropId, RECT* prc, out MARGINS pMargins) { pMargins = default; return HRESULT.S_OK; }
        public static HRESULT GetThemeMargins(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, in RECT prc, out MARGINS pMargins) { pMargins = default; return HRESULT.S_OK; }
        public static HRESULT GetThemeMargins(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, int iPropId, in RECT prc, out MARGINS pMargins) { pMargins = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemePartSize(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* prc, THEMESIZE eSize, Size* psz) => HRESULT.S_OK;
        public static unsafe HRESULT GetThemePartSize(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, RECT* prc, THEMESIZE eSize, out SIZE psz) { psz = default; return HRESULT.S_OK; }
        public static HRESULT GetThemePartSize(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT prc, THEMESIZE eSize, out Size psz) { psz = default; return HRESULT.S_OK; }
        public static HRESULT GetThemePartSize(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, in RECT prc, THEMESIZE eSize, out SIZE psz) { psz = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemePosition(HTHEME hTheme, int iPartId, int iStateId, int iPropId, Point* pPoint) => HRESULT.S_OK;
        public static HRESULT GetThemePosition(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, out Point pPoint) { pPoint = default; return HRESULT.S_OK; }
        public static HRESULT GetThemePosition(HTHEME hTheme, int iPartId, int iStateId, int iPropId, out Point pPoint) { pPoint = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeString(HTHEME hTheme, int iPartId, int iStateId, int iPropId, PWSTR pszBuff, int cchMaxBuffChars) => HRESULT.S_OK;
        public static HRESULT GetThemeString(HTHEME hTheme, int iPartId, int iStateId, THEME_PROPERTY_SYMBOL_ID iPropId, PWSTR pszBuff, int cchMaxBuffChars) => HRESULT.S_OK;
        public static unsafe HRESULT GetThemeDocumentationProperty(PCWSTR pszThemeName, PCWSTR pszPropertyName, PWSTR pszValueBuff, int cchMaxValChars) => HRESULT.S_OK;
        public static BOOL GetThemeSysBool(HTHEME hTheme, THEME_PROPERTY_SYMBOL_ID iBoolId) => (BOOL)false;
        public static int GetThemeSysInt(HTHEME hTheme, int iIntId) => 0;
        public static HRESULT GetThemeSysInt(HTHEME hTheme, THEME_PROPERTY_SYMBOL_ID iIntId, out int piValue) { piValue = 0; return HRESULT.S_OK; }
        public static HRESULT GetThemeSysInt(HTHEME hTheme, int iIntId, out int piValue) { piValue = 0; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeTextExtent(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, PCWSTR pszText, int cchText, uint dwTextFlags, RECT* pBoundingRect, RECT* pExtentRect) => HRESULT.S_OK;
        public static HRESULT GetThemeTextExtent(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, PCWSTR pszText, int cchText, uint dwTextFlags, in RECT pBoundingRect, out RECT pExtentRect) { pExtentRect = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeTextExtent(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, string pszText, int cchText, DRAW_TEXT_FORMAT dwTextFlags, RECT* pBoundingRect, out RECT pExtentRect) { pExtentRect = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeTextExtent(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, string pszText, int cchText, DRAW_TEXT_FORMAT dwTextFlags, in RECT pBoundingRect, out RECT pExtentRect) { pExtentRect = default; return HRESULT.S_OK; }
        public static unsafe HRESULT GetThemeTextMetrics(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, void* ptm) => HRESULT.S_OK;
        public static unsafe HRESULT GetThemeTextMetrics(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, out TEXTMETRICW ptm) { ptm = default; return HRESULT.S_OK; }
        public static DPI_AWARENESS_CONTEXT GetThreadDpiAwarenessContext() => DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2;
        public static DPI_HOSTING_BEHAVIOR GetThreadDpiHostingBehavior() => DPI_HOSTING_BEHAVIOR.DPI_HOSTING_BEHAVIOR_DEFAULT;
        public static uint GetTickCount() => (uint)Environment.TickCount;
        public static GDI_REGION_TYPE GetUpdateRgn(HWND hWnd, HRGN hRgn, BOOL bErase) => GDI_REGION_TYPE.SIMPLEREGION;
        internal static unsafe WIN32_ERROR RegLoadMUIString(Windows.Win32.System.Registry.HKEY hKey, PCWSTR pszValue, PWSTR pszOutBuf, uint cbOutBuf, uint* pcbData, uint Flags, char* pszDirectory) => WIN32_ERROR.ERROR_FILE_NOT_FOUND;
        public static unsafe BOOL GetUserObjectInformation(HANDLE hObj, USER_OBJECT_INFORMATION_INDEX nIndex, void* pvInfo, uint nLength, uint* lpnLengthNeeded)
        {
            if (pvInfo != null && nLength >= (uint)sizeof(USEROBJECTFLAGS))
            {
                var flags = (USEROBJECTFLAGS*)pvInfo;
                // NOT WSF_VISIBLE: SystemInformation.UserInteractive stays false,
                // so modal loops fail fast with InvalidOperationException
                // (catchable + reported) instead of spinning the single browser
                // thread forever Ã¢â‚¬â€ no nested pump can receive browser events.
                flags->dwFlags = 0;
            }
            return (BOOL)true;
        }
        public static unsafe BOOL GetViewportExtEx(HDC hdc, SIZE* lpsize) => (BOOL)true;
        public static DPI_AWARENESS_CONTEXT GetWindowDpiAwarenessContext(HWND hwnd) => DPI_AWARENESS_CONTEXT.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2;
        public static unsafe BOOL GetWindowPlacement(HWND hWnd, WINDOWPLACEMENT* lpwndpl) => (BOOL)true;
        public static BOOL GetWindowPlacement(HWND hWnd, ref WINDOWPLACEMENT lpwndpl) => (BOOL)true;
        public static BOOL HideCaret(HWND hWnd) => (BOOL)true;
        public static unsafe HRESULT HitTestThemeBackground(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, uint dwOptions, RECT* pRect, HRGN hrgn, Point ptTest, ushort* pwHitTestCode) => HRESULT.S_OK;
        public static unsafe HRESULT HitTestThemeBackground(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, HIT_TEST_BACKGROUND_OPTIONS dwOptions, in RECT pRect, HRGN hrgn, Point ptTest, out ushort pwHitTestCode) { pwHitTestCode = 0; return HRESULT.S_OK; }
        public static unsafe HRESULT HitTestThemeBackground(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, HIT_TEST_BACKGROUND_OPTIONS dwOptions, RECT* pRect, HRGN hrgn, Point ptTest, out ushort pwHitTestCode) { pwHitTestCode = 0; return HRESULT.S_OK; }
        public static HIMAGELIST ImageList_Create(int cx, int cy, IMAGELIST_CREATION_FLAGS flags, int cInitial, int cGrow) => (HIMAGELIST)global::System.Windows.Forms.WasmImageListStore.Create(cx, cy, 0);
        public static HIMAGELIST ImageList_Duplicate(HIMAGELIST himl) => (HIMAGELIST)global::System.Windows.Forms.WasmImageListStore.Duplicate(himl);
        public static unsafe HIMAGELIST ImageList_Read(void* pstm) => (HIMAGELIST)global::System.Windows.Forms.WasmImageListStore.Create(0, 0, 0);
        public static HIMC ImmAssociateContext(HWND hwnd, HIMC himc) => default;
        public static HIMC ImmCreateContext() => default;

        // Batch 5
        public static unsafe BOOL ImmGetConversionStatus(HIMC himc, IME_CONVERSION_MODE* lpfdwConversion, IME_SENTENCE_MODE* lpfdwSentence) => (BOOL)true;
        public static BOOL ImmGetOpenStatus(HIMC himc) => (BOOL)false;
        public static BOOL ImmNotifyIME(HIMC himc, NOTIFY_IME_ACTION dwAction, NOTIFY_IME_INDEX dwIndex, uint dwValue) => (BOOL)true;
        public static BOOL ImmSetConversionStatus(HIMC himc, IME_CONVERSION_MODE fdwConversion, IME_SENTENCE_MODE fdwSentence) => (BOOL)true;
        public static BOOL ImmSetOpenStatus(HIMC himc, BOOL fOpen) => (BOOL)true;
        public static void InitCommonControls() { }
        public static unsafe BOOL InitCommonControlsEx(INITCOMMONCONTROLSEX* picce) => (BOOL)true;
        public static BOOL InitCommonControlsEx(in INITCOMMONCONTROLSEX picce) => (BOOL)true;
        public static GDI_REGION_TYPE IntersectClipRect(HDC hdc, int left, int top, int right, int bottom) => GDI_REGION_TYPE.SIMPLEREGION;
        // No uxtheme engine exists in the browser (OpenThemeData answers
        // null, so VisualStyleRenderer.IsSupported is already false):
        // report theming disabled so vendors take their desktop-
        // disabled classic paths directly instead of try-themed +
        // first-chance InvalidOperationException per paint (observed on
        // PdnTrackBar.OnPaint -> PaintThemed on every dialog repaint).
        public static BOOL IsAppThemed() => (BOOL)false;
        public static unsafe BOOL IsDialogMessage(HWND hDlg, MSG* lpMsg) => (BOOL)false;
        public static BOOL IsDialogMessage(HWND hDlg, in MSG lpMsg) => (BOOL)false;
        public static BOOL IsProcessDPIAware() => (BOOL)true;
        public static BOOL IsThemeBackgroundPartiallyTransparent(HTHEME hTheme, int iPartId, int iStateId) => (BOOL)false;
        public static BOOL IsThemePartDefined(HTHEME hTheme, int iPartId, int iStateId) => (BOOL)true;
        public static BOOL IsValidDpiAwarenessContext(DPI_AWARENESS_CONTEXT value) => (BOOL)true;
        public static BOOL IsWindowUnicode(HWND hWnd) => (BOOL)true;
        public static BOOL IsZoomed(HWND hWnd) => (BOOL)false;
        public static unsafe BOOL LPtoDP(HDC hdc, Point* lppt, int c) => (BOOL)true;
        public static BOOL LPtoDP(HDC hdc, Span<Point> lpPoints) => (BOOL)true;
        public static BOOL LineTo(HDC hdc, int x, int y) => (BOOL)true;
        public static unsafe HCURSOR LoadCursor(HINSTANCE hInstance, PCWSTR lpCursorName) => (HCURSOR)(nint)1;
        public static unsafe LRESULT LresultFromObject(Guid* riid, WPARAM wParam, void* pAcc) => default;
        public static unsafe LRESULT LresultFromObject(in Guid riid, WPARAM wParam, void* pAcc) => default;
        public static BOOL MessageBeep(MESSAGEBOX_STYLE uType) => (BOOL)true;
        public static unsafe int MessageBox(HWND hWnd, PCWSTR lpText, PCWSTR lpCaption, MESSAGEBOX_STYLE uType) => ShowWasmMessageBox(lpText, lpCaption, uType);
        public static unsafe int MessageBox(HWND hWnd, string? lpText, string? lpCaption, MESSAGEBOX_STYLE uType)
            => ShowWasmMessageBox(lpText, lpCaption, uType);

        // Visible MessageBox emulation (coordinator item B10). Presents a managed modal dialog
        // through the existing WasmDialogHost-tracked ShowDialog path (which returns immediately
        // on WASM while the dialog stays open and interactive) and always returns IDOK, preserving
        // the previous silent-stub return semantics so existing caller flow is unaffected.
        // Never throws: any failure falls back to the old silent IDOK.
        private static int ShowWasmMessageBox(string? text, string? caption, MESSAGEBOX_STYLE style)
        {
            try
            {
                CreateWasmMessageBox(text, caption, style).ShowDialog();
            }
            catch
            {
            }

            return (int)MESSAGEBOX_RESULT.IDOK;
        }

        private static global::System.Windows.Forms.Form CreateWasmMessageBox(string? text, string? caption, MESSAGEBOX_STYLE style)
        {
            var dialog = new global::System.Windows.Forms.Form
            {
                Text = caption ?? string.Empty,
                StartPosition = global::System.Windows.Forms.FormStartPosition.CenterParent,
                FormBorderStyle = global::System.Windows.Forms.FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowIcon = false,
                ShowInTaskbar = false,
            };

            const int pad = 12;
            const int buttonWidth = 80;
            const int buttonHeight = 26;
            const int buttonGap = 6;

            var label = new global::System.Windows.Forms.Label
            {
                Text = text ?? string.Empty,
                AutoSize = true,
                MaximumSize = new global::System.Drawing.Size(340, 0),
                Location = new global::System.Drawing.Point(pad, pad),
            };
            dialog.Controls.Add(label);

            int textWidth = 340;
            int textHeight = 32;
            try
            {
                global::System.Drawing.Size preferred = label.GetPreferredSize(new global::System.Drawing.Size(340, 0));
                textWidth = Math.Max(0, Math.Min(340, preferred.Width));
                textHeight = Math.Max(0, preferred.Height);
            }
            catch
            {
            }

            (global::System.Windows.Forms.DialogResult Result, string Text)[] buttons = ((uint)style & (uint)MESSAGEBOX_STYLE.MB_TYPEMASK) switch
            {
                (uint)MESSAGEBOX_STYLE.MB_OKCANCEL => [(global::System.Windows.Forms.DialogResult.OK, "OK"),
                    (global::System.Windows.Forms.DialogResult.Cancel, "Cancel")],
                (uint)MESSAGEBOX_STYLE.MB_ABORTRETRYIGNORE => [(global::System.Windows.Forms.DialogResult.Abort, "Abort"),
                    (global::System.Windows.Forms.DialogResult.Retry, "Retry"),
                    (global::System.Windows.Forms.DialogResult.Ignore, "Ignore")],
                (uint)MESSAGEBOX_STYLE.MB_YESNOCANCEL => [(global::System.Windows.Forms.DialogResult.Yes, "Yes"),
                    (global::System.Windows.Forms.DialogResult.No, "No"),
                    (global::System.Windows.Forms.DialogResult.Cancel, "Cancel")],
                (uint)MESSAGEBOX_STYLE.MB_YESNO => [(global::System.Windows.Forms.DialogResult.Yes, "Yes"),
                    (global::System.Windows.Forms.DialogResult.No, "No")],
                (uint)MESSAGEBOX_STYLE.MB_RETRYCANCEL => [(global::System.Windows.Forms.DialogResult.Retry, "Retry"),
                    (global::System.Windows.Forms.DialogResult.Cancel, "Cancel")],
                (uint)MESSAGEBOX_STYLE.MB_CANCELTRYCONTINUE => [(global::System.Windows.Forms.DialogResult.Cancel, "Cancel"),
                    (global::System.Windows.Forms.DialogResult.TryAgain, "Try Again"),
                    (global::System.Windows.Forms.DialogResult.Continue, "Continue")],
                _ => [(global::System.Windows.Forms.DialogResult.OK, "OK")],
            };

            int totalButtonsWidth = buttons.Length * buttonWidth + (buttons.Length - 1) * buttonGap;
            int clientWidth = Math.Max(textWidth, totalButtonsWidth) + pad * 2;
            int panelTop = pad + textHeight + pad;
            int x = clientWidth - pad - totalButtonsWidth;
            uint defaultIndex = ((uint)style & (uint)MESSAGEBOX_STYLE.MB_DEFMASK) >> 8;
            if (defaultIndex >= (uint)buttons.Length) defaultIndex = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = new global::System.Windows.Forms.Button
                {
                    Text = buttons[i].Text,
                    DialogResult = buttons[i].Result,
                    Size = new global::System.Drawing.Size(buttonWidth, buttonHeight),
                    Location = new global::System.Drawing.Point(x, panelTop),
                };
                dialog.Controls.Add(button);
                if ((uint)i == defaultIndex) dialog.AcceptButton = button;
                if (buttons[i].Result is global::System.Windows.Forms.DialogResult.Cancel
                    or global::System.Windows.Forms.DialogResult.Abort
                    or global::System.Windows.Forms.DialogResult.No) dialog.CancelButton = button;
                x += buttonWidth + buttonGap;
            }

            dialog.ClientSize = new global::System.Drawing.Size(clientWidth, panelTop + buttonHeight + pad);
            return dialog;
        }

        // Headless-FillRect consumer half (coordinator item A4). There is no HDC->Graphics map
        // in this fork: DeviceContextHdcScope yields a null HDC (Graphics.GetHdc() is zero) and
        // every HBRUSH is the (nint)1 sentinel, so FillRect cannot recover the target bitmap or
        // the brush color on its own. This bridge closes that gap once a paint scope registers
        // its target Graphics (and brush COLORREFs): FillRect then fills for real on the CPU
        // bitmap. With nothing registered it stays a success no-op behind a single null check,
        // so per-frame cost is ~zero for already-correct paths. TryFill never throws; register
        // and unregister are lock-guarded for test parallelism. Producer wiring lives outside
        // the owned Interop files: register the PaintEventArgs Graphics in DeviceContextHdcScope
        // and record COLORREFs in CreateBrushScope/SelectObjectScope.
        internal static class WasmFillBridge
        {
            private static readonly object s_lock = new();
            private static Dictionary<nint, global::System.Drawing.Graphics>? s_targets;
            private static Dictionary<nint, uint>? s_brushColors;
            // Draw-after-release grace: desktop tolerates GDI calls on an
            // HDC after ReleaseHdc (Syncfusion MenuTextCellRenderer releases
            // the grid Graphics HDC, then paints the item text through the
            // same HDC). Our token registry is stricter than GDI, so a
            // released token lost its target and every such paint kept the
            // quiet-zero stub (blank menu rows). Released mappings move to
            // a bounded side table instead of dying: the token still names
            // its surface (minted tokens never repeat), so post-release
            // draws land where desktop puts them. Bounded (evict-oldest) so
            // per-paint GetHdc/ReleaseHdc churn cannot pin surfaces without
            // limit. Never-registered HDCs still resolve to nothing.
            private const int ReleasedCap = 32;
            private static Dictionary<nint, global::System.Drawing.Graphics>? s_released;
            private static readonly Queue<nint> s_releasedOrder = new();

            internal static IDisposable TrackTarget(HDC hdc, global::System.Drawing.Graphics? graphics)
            {
                if (graphics is null) return NullScope.Instance;
                lock (s_lock)
                {
                    s_targets ??= new();
                    s_targets[hdc.Value] = graphics;
                    // Fresh registration wins over a stale release record
                    // (the shared zero HDC re-registers every paint scope).
                    s_released?.Remove(hdc.Value);
                }

                return new UnregisterOnDispose(hdc, graphics);
            }

            internal static bool TryGetTarget(HDC hdc, out global::System.Drawing.Graphics? graphics)
            {
                graphics = null;
                if (s_targets is null && s_released is null) return false;
                lock (s_lock)
                {
                    return ResolveTarget(hdc, ref graphics);
                }
            }

            private static bool ResolveTarget(HDC hdc, ref global::System.Drawing.Graphics? graphics)
            {
                if (s_targets is not null
                    && s_targets.TryGetValue(hdc.Value, out graphics)
                    && graphics is not null)
                {
                    return true;
                }
                graphics = null;
                return s_released is not null
                    && s_released.TryGetValue(hdc.Value, out graphics)
                    && graphics is not null;
            }

            private static void RememberReleased(nint key, global::System.Drawing.Graphics graphics)
            {
                s_released ??= new();
                if (!s_released.ContainsKey(key))
                {
                    s_released[key] = graphics;
                    s_releasedOrder.Enqueue(key);
                    while (s_releasedOrder.Count > ReleasedCap)
                    {
                        nint oldest = s_releasedOrder.Dequeue();
                        if (!s_releasedOrder.Contains(oldest))
                            s_released.Remove(oldest);
                    }
                }
            }

            internal static void NoteBrushColor(HBRUSH brush, COLORREF color)
            {
                lock (s_lock)
                {
                    s_brushColors ??= new();
                    s_brushColors[brush.Value] = color.Value;
                }
            }
            private static int s_brushSerial;
            // Unique handle per created brush (a shared sentinel would
            // alias every brush to the last-noted color).
            internal static HBRUSH NewBrushHandle(COLORREF color)
            {
                nint handle = global::System.Threading.Interlocked.Increment(ref s_brushSerial) + 0x10000;
                var brush = (HBRUSH)handle;
                NoteBrushColor(brush, color);
                return brush;
            }
            internal static void ForgetBrush(HGDIOBJ ho)
            {
                lock (s_lock)
                {
                    s_brushColors?.Remove((nint)ho);
                }
            }

            internal static int TryFill(HDC hdc, in RECT lprc, HBRUSH hbr)
            {
                if (s_targets is null) return 1;

                global::System.Drawing.Graphics? graphics;
                uint colorValue;
                lock (s_lock)
                {
                    if (s_targets is null || !s_targets.TryGetValue(hdc.Value, out graphics) || graphics is null) return 1;
                    if (s_brushColors is null || !s_brushColors.TryGetValue(hbr.Value, out colorValue)) return 1;
                }

                try
                {
                    Rectangle rect = lprc;
                    if (rect.Width <= 0 || rect.Height <= 0) return 1;
                    using var brush = new SolidBrush(ColorTranslator.FromWin32((int)colorValue));
                    graphics.FillRectangle(brush, rect);
                }
                catch
                {
                }

                return 1;
            }

            // Scratch-DC registry for Graphics.GetHdc: each call mints a
            // distinct token (0x7000 range) so offscreen paint scopes
            // (ImageList.GetBitmap, DrawText over scratch bitmaps) resolve
            // their target instead of colliding on the shared null HDC.
            private static int s_scratchSerial;
            private static readonly Dictionary<nint, IDisposable> s_scratchScopes = new();

            internal static IntPtr RegisterScratch(global::System.Drawing.Graphics graphics)
            {
                nint token = global::System.Threading.Interlocked.Increment(ref s_scratchSerial) + 0x7000;
                IDisposable scope = TrackTarget((HDC)token, graphics);
                lock (s_lock)
                {
                    s_scratchScopes[token] = scope;
                }

                return token;
            }

            internal static void UnregisterScratch(IntPtr hdc)
            {
                IDisposable? scope = null;
                lock (s_lock)
                {
                    if (s_scratchScopes.TryGetValue((nint)hdc, out scope))
                    {
                        s_scratchScopes.Remove((nint)hdc);
                    }
                }

                try { scope?.Dispose(); } catch { }
            }

            private sealed class UnregisterOnDispose : IDisposable
            {
                private readonly HDC _hdc;
                private readonly global::System.Drawing.Graphics _graphics;

                internal UnregisterOnDispose(HDC hdc, global::System.Drawing.Graphics graphics)
                {
                    _hdc = hdc;
                    _graphics = graphics;
                }

                public void Dispose()
                {
                    lock (s_lock)
                    {
                        if (s_targets is not null
                            && s_targets.TryGetValue(_hdc.Value, out global::System.Drawing.Graphics? current)
                            && ReferenceEquals(current, _graphics))
                        {
                            s_targets.Remove(_hdc.Value);
                            // Draw-after-release parity: keep resolving the
                            // token to its surface (bounded side table).
                            RememberReleased(_hdc.Value, _graphics);
                        }
                    }
                }
            }

            private sealed class NullScope : IDisposable
            {
                internal static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
        // GDI text bridge: DrawTextEx/ExtTextOut/DrawThemeText paint through the
        // registered Graphics (same target registry as WasmFillBridge) using the
        // DC's recorded font/color/alignment state, recorded by the PInvokeCore
        // hooks (CreateFontIndirect/SelectObject/SetTextColor/...). Unregistered
        // DCs keep the metric heuristic in DrawTextEx and paint nothing, exactly
        // as before. Fast path is one dictionary lookup per paint scope.
        internal static class WasmTextBridge
        {
            private sealed class TextState
            {
                public global::System.Drawing.Font? Font;
                public nint FontHandle;
                public uint TextColor;
                public uint BkColor = 0x00FFFFFF;
                public int BkMode = 1; // BACKGROUND_MODE.TRANSPARENT
                public uint TextAlign;
            }

            private static readonly Dictionary<nint, TextState> s_states = new();
            private static readonly object s_stateLock = new();

            private static TextState StateFor(HDC hdc)
            {
                lock (s_stateLock)
                {
                    if (!s_states.TryGetValue(hdc.Value, out TextState? state))
                    {
                        state = new TextState();
                        s_states[hdc.Value] = state;
                    }

                    return state;
                }
            }

            internal static global::System.Drawing.Font? SelectedFont(HDC hdc) => StateFor(hdc).Font;

            internal static global::System.Drawing.Size MeasureExtent(HDC hdc, string text)
            {
                global::System.Drawing.Font font = SelectedFont(hdc)
                    ?? global::System.Drawing.SystemFonts.DefaultFont;
                float em = font.Unit == global::System.Drawing.GraphicsUnit.Pixel
                    ? font.Size
                    : font.Size * 96f / 72f;
                // Height is the GDI cell height (extent cy == tmHeight on
                // Windows); width is the tight single-line advance sum.
                int height = global::System.Drawing.GdiTextMetrics.GetRow(font.Name, em).Height;
                if (string.IsNullOrEmpty(text))
                {
                    if (global::System.Windows.Forms.WasmFrameCounters.PerfEnabled)
                        global::System.Windows.Forms.WasmFrameCounters.NoteTextMeasure(1, 0, 0);
                    return new global::System.Drawing.Size(0, height);
                }
                // Desktop GDI advances are additive per-char integers
                // (oracle GdiAbcTable, verified 9/9); canvas outline sums
                // run +4-9% wide, so prefer the table for single-line
                // tabled text and keep the tight measure as fallback.
                // Perf item 4: note volume + table misses (each fallback
                // is a Bitmap scratch plus a sync JSImport pair).
                int abc = 0;
                bool tabled = text.IndexOf((char)10) < 0 && text.IndexOf((char)13) < 0
                    && global::System.Drawing.GdiAbcTable.TryMeasureString(font.Name, em, text, out abc);
                if (global::System.Windows.Forms.WasmFrameCounters.PerfEnabled)
                    global::System.Windows.Forms.WasmFrameCounters.NoteTextMeasure(1, text.Length, tabled ? 0 : 1);
                if (tabled)
                    return new global::System.Drawing.Size(abc, height);
                using var scratch = new global::System.Drawing.Bitmap(1, 1);
                using var graphics = global::System.Drawing.Graphics.FromImage(scratch);
                string edgeTrimmed = text.TrimStart(' ');
                int leading = text.Length - edgeTrimmed.Length;
                global::System.Drawing.SizeF size = string.IsNullOrWhiteSpace(text)
                    ? graphics.MeasureString(text, font, global::System.Drawing.SizeF.Empty)
                    : graphics.MeasureStringTight(edgeTrimmed, font);
                // GDI parity: GetTextExtentPoint32 counts every blank advance,
                // leading as well as trailing. The tight raster drops them:
                // TrimEnd strips trailing, and the JS wrap-split turns a
                // leading blank into a separator with no advance (Telerik
                // escapes inter-word spaces as space+ZWJ, which measured
                // width 1 instead of a space advance, joining RTE words).
                // Leading blanks are stripped above (as TrimEnd already does
                // for trailing); every edge blank is added back at the
                // measured space advance. Interior blanks stay in the tight
                // span (no double count). Bare whitespace keeps the
                // whitespace-only branch.
                int trailing = 0;
                for (int i = text.Length - 1; i >= 0 && text[i] == ' '; i--) trailing++;
                int blanks = leading + trailing;
                if (blanks > 0 && blanks < text.Length)
                {
                    float spaceW = graphics.MeasureString(" ", font, global::System.Drawing.SizeF.Empty).Width;
                    if (spaceW > 0f) size.Width += blanks * spaceW;
                }
                return new global::System.Drawing.Size(Math.Max(0, (int)Math.Ceiling(size.Width)), height);
            }

            // No-target metric fallback (DCs without a scratch bitmap paint
            // nothing, but measurement must still answer: Telerik TPF text
            // and tree labels measure through target-less DCs, and
            // stub-zero extents collapsed them). Word-wraps when WORDBREAK
            // is set (without SINGLELINE) and wrapWidth > 0; previously
            // every measure came back single-line height, squeezing
            // wrapped elements such as BookShelf tile titles to slivers.
            // Returns total height; measuredWidth includes the margins.
            internal static int MeasureHeuristic(ReadOnlySpan<char> text, int wrapWidth, DRAW_TEXT_FORMAT format, int leftMargin, int rightMargin, out int measuredWidth)
            {
                const int lineHeight = 16;
                bool noPrefix = format.HasFlag(DRAW_TEXT_FORMAT.DT_NOPREFIX);
                bool wrap = format.HasFlag(DRAW_TEXT_FORMAT.DT_WORDBREAK)
                    && !format.HasFlag(DRAW_TEXT_FORMAT.DT_SINGLELINE)
                    && wrapWidth > 0;
                // Pass 1: words (advance sums) with -1 sentinels for hard breaks.
                var words = new global::System.Collections.Generic.List<float>();
                float wordW = 0f;
                bool inWord = false;
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = text[i];
                    if (ch == '\r') continue;
                    if (ch == '\n')
                    {
                        if (inWord) { words.Add(wordW); wordW = 0f; inWord = false; }
                        words.Add(-1f);
                        continue;
                    }
                    if (ch == ' ')
                    {
                        if (inWord) { words.Add(wordW); wordW = 0f; inWord = false; }
                        continue;
                    }
                    if (ch == '&' && !noPrefix)
                    {
                        if (i + 1 < text.Length && text[i + 1] == '&') { i++; }
                        else { continue; }
                    }
                    wordW += CharAdvance(ch);
                    inWord = true;
                }
                if (inWord) words.Add(wordW);
                // Pass 2: pack words into lines.
                const float spaceW = 4f;
                int lineCount = 1;
                float currentLineWidth = 0f;
                float maxLineWidth = 0f;
                bool lineHasWord = false;
                foreach (float w in words)
                {
                    if (w < 0f)
                    {
                        if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;
                        lineCount++;
                        currentLineWidth = 0f;
                        lineHasWord = false;
                        continue;
                    }
                    float need = lineHasWord ? currentLineWidth + spaceW + w : w;
                    if (wrap && lineHasWord && need > wrapWidth)
                    {
                        if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;
                        lineCount++;
                        currentLineWidth = w;
                    }
                    else
                    {
                        currentLineWidth = need;
                    }
                    lineHasWord = true;
                }
                if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;
                int totalWidth = (int)global::System.Math.Ceiling(maxLineWidth) + leftMargin + rightMargin;
                int totalHeight = lineCount * lineHeight;
                measuredWidth = global::System.Math.Max(1, totalWidth);
                return global::System.Math.Max(lineHeight, totalHeight);
            }

            internal static float CharAdvance(char ch) => ch switch
            {
                ' ' => 4f,
                '.' or ',' or ':' or ';' or '!' or '|' or '\'' or '`' => 3f,
                'i' or 'l' or 'j' or 'I' or '[' or ']' or '(' or ')' or '{' or '}' => 4f,
                'f' or 't' or 'r' => 5f,
                'm' or 'w' or 'M' or 'W' or '@' or '%' => 11f,
                >= 'A' and <= 'Z' => 8.5f,
                >= '0' and <= '9' => 7f,
                _ => 6.8f
            };

            // Word-chunk GDI paint: each tabled word rasterizes at its accumulated
            // oracle x so ink lands on desktop advances. Whole-line browser
            // shaping cannot reproduce GDI widths (eval6: MS end +8, RTC end
            // -12), while DrawString blits origin-exact, so per-word offsets
            // are exact up to intra-word shaping. False (whole blit) unless
            // the line is fully tabled with a space.
            internal static bool TryPaintWords(global::System.Drawing.Graphics graphics, string text, global::System.Drawing.Font font, global::System.Drawing.Brush brush, float x, float y, global::System.Drawing.StringFormat sf)
            {
                float em = font.Unit == global::System.Drawing.GraphicsUnit.Pixel
                    ? font.Size
                    : font.Size * 96f / 72f;
                if (!global::System.Drawing.GdiAbcTable.TryPlanWords(font.Name, em, text, out var runs, out _))
                    return false;
                foreach (var run in runs)
                    graphics.DrawString(run.Word, font, brush, new global::System.Drawing.PointF(x + run.XOffset, y), sf);
                return true;
            }

            // Rect-clipped variant for single-line Near DrawText: each word
            // keeps the original rect (shifted left edge) so cell clipping
            // is preserved; line alignment comes from the shared format.
            internal static bool TryPaintWordsRect(global::System.Drawing.Graphics graphics, string text, global::System.Drawing.Font font, global::System.Drawing.Brush brush, global::System.Drawing.RectangleF layout, global::System.Drawing.StringFormat sf)
            {
                float em = font.Unit == global::System.Drawing.GraphicsUnit.Pixel
                    ? font.Size
                    : font.Size * 96f / 72f;
                if (!global::System.Drawing.GdiAbcTable.TryPlanWords(font.Name, em, text, out var runs, out _))
                    return false;
                foreach (var run in runs)
                {
                    if (run.XOffset >= layout.Width)
                        continue;
                    var wordRect = new global::System.Drawing.RectangleF(
                        layout.X + run.XOffset, layout.Y,
                        Math.Max(1, layout.Width - run.XOffset), layout.Height);
                    graphics.DrawString(run.Word, font, brush, wordRect, sf);
                }
                return true;
            }

            internal static HFONT CreateFont(in LOGFONTW logFont)
            {
                var style = global::System.Drawing.FontStyle.Regular;
                if ((int)logFont.lfWeight >= 700) style |= global::System.Drawing.FontStyle.Bold;
                if (logFont.lfItalic != 0) style |= global::System.Drawing.FontStyle.Italic;
                if (logFont.lfUnderline != 0) style |= global::System.Drawing.FontStyle.Underline;
                if (logFont.lfStrikeOut != 0) style |= global::System.Drawing.FontStyle.Strikeout;
                string face = logFont.FaceName.IsEmpty ? "Microsoft Sans Serif" : new string(logFont.FaceName);
                var font = new global::System.Drawing.Font(
                    face,
                    Math.Max(1, Math.Abs(logFont.lfHeight)),
                    style,
                    global::System.Drawing.GraphicsUnit.Pixel);
                return (HFONT)font.ToHfont();
            }

            internal static HGDIOBJ SelectObject(HDC hdc, HGDIOBJ h)
            {
                if (global::System.Drawing.Font.TryFromHfont((nint)h, out global::System.Drawing.Font? font) && font is not null)
                {
                    TextState state = StateFor(hdc);
                    nint previous = state.FontHandle;
                    state.Font = font;
                    state.FontHandle = (nint)h;
                    return (HGDIOBJ)(previous != 0 ? previous : (nint)1);
                }

                return (HGDIOBJ)(nint)1;
            }

            internal static COLORREF SetTextColor(HDC hdc, COLORREF color)
            {
                TextState state = StateFor(hdc);
                uint previous = state.TextColor;
                state.TextColor = color.Value;
                return (COLORREF)previous;
            }

            internal static COLORREF GetTextColor(HDC hdc) => (COLORREF)StateFor(hdc).TextColor;

            internal static COLORREF SetBkColor(HDC hdc, COLORREF color)
            {
                TextState state = StateFor(hdc);
                uint previous = state.BkColor;
                state.BkColor = color.Value;
                return (COLORREF)previous;
            }

            internal static BACKGROUND_MODE SetBkMode(HDC hdc, BACKGROUND_MODE mode)
            {
                TextState state = StateFor(hdc);
                var previous = (BACKGROUND_MODE)state.BkMode;
                state.BkMode = (int)mode;
                return previous;
            }

            internal static TEXT_ALIGN_OPTIONS SetTextAlign(HDC hdc, TEXT_ALIGN_OPTIONS align)
            {
                TextState state = StateFor(hdc);
                var previous = (TEXT_ALIGN_OPTIONS)state.TextAlign;
                state.TextAlign = (uint)align;
                return previous;
            }


            // Bounded GDI-paint census (tile-text diagnosis): which DrawText
            // paints arrive, with what rect, and whether a world transform
            // was active when they arrived. Rewriter-note: keep this block
            // simple (plain fields, no out-vars, no System.Math) - the IL
            // rewriter has dropped fancier blocks in this file before.
            internal static int s_gdiPaintCalls;
            internal static int s_gdiPaintHits;
            internal static int s_gdiPaintEmpty;
            private static readonly string[] s_gdiLastPaints = new string[8];
            private static int s_gdiLastPaintIndex;
            internal static bool HasWorldTransform(global::System.Drawing.Graphics graphics)
            {
                try { return !graphics.Transform.IsIdentity; } catch { return false; }
            }
            internal static void NoteGdiPaint(string preview, int left, int top, int right, int bottom, bool hadTransform)
            {
                s_gdiPaintCalls++;
                if (right <= left || bottom <= top) s_gdiPaintEmpty++;
                string p = preview.Length > 24 ? preview.Substring(0, 24) : preview;
                s_gdiLastPaints[s_gdiLastPaintIndex % 8] = p + " " + left + "," + top + " " + right + "," + bottom + (hadTransform ? " xf" : " idx");
                s_gdiLastPaintIndex++;
            }
            public static string DumpGdiPaintCensus()
            {
                global::System.Text.StringBuilder sb = new global::System.Text.StringBuilder();
                sb.Append("paints=").Append(s_gdiPaintHits).Append(" empty=").Append(s_gdiPaintEmpty).Append(" attempts=").Append(s_gdiPaintCalls).Append(" ext=").Append(s_extCalls).Append(';');
                if (s_fallbackError is not null) sb.Append(" fberr=").Append(s_fallbackError).Append(';');
                for (int i = 0; i < 8; i++)
                {
                    string e = s_gdiLastPaints[(s_gdiLastPaintIndex + i) % 8];
                    if (e is not null) sb.Append('[').Append(e).Append(']');
                }
                return sb.ToString();
            }
            internal static unsafe bool TryDrawText(
                HDC hdc,
                ReadOnlySpan<char> text,
                RECT* lprc,
                DRAW_TEXT_FORMAT format,
                DRAWTEXTPARAMS* lpdtp,
                out int height)
            {
                height = 0;
                if (lprc is null || text.IsEmpty) return false;
                NoteGdiPaint(text.Length > 24 ? text.ToString().Substring(0, 24) : text.ToString(), lprc->left, lprc->top, lprc->right, lprc->bottom, false);
                if (!WasmFillBridge.TryGetTarget(hdc, out global::System.Drawing.Graphics? graphics) || graphics is null) return false;

                TextState state = StateFor(hdc);
                global::System.Drawing.Font font = state.Font ?? global::System.Drawing.SystemFonts.DefaultFont;
                Rectangle rect = *lprc;
                int leftMargin = lpdtp is not null ? (int)lpdtp->iLeftMargin : 0;
                int rightMargin = lpdtp is not null ? (int)lpdtp->iRightMargin : 0;
                string cleaned = CleanPrefix(text, format.HasFlag(DRAW_TEXT_FORMAT.DT_NOPREFIX));
                using var sf = BuildFormat(format);

                try
                {
                    if (format.HasFlag(DRAW_TEXT_FORMAT.DT_CALCRECT))
                    {
                        int measureWidth = format.HasFlag(DRAW_TEXT_FORMAT.DT_SINGLELINE)
                            ? int.MaxValue
                            : Math.Max(1, rect.Width - leftMargin - rightMargin);
                        global::System.Drawing.SizeF size = graphics.MeasureStringTight(cleaned, font, measureWidth, sf);
                        if (format.HasFlag(DRAW_TEXT_FORMAT.DT_SINGLELINE))
                        {
                            float emCalc = font.Unit == global::System.Drawing.GraphicsUnit.Pixel
                                ? font.Size
                                : font.Size * 96f / 72f;
                            if (global::System.Drawing.GdiAbcTable.TryMeasureString(font.Name, emCalc, cleaned, out int abcCalc))
                                size = new global::System.Drawing.SizeF(abcCalc, size.Height);
                        }
                        int width = Math.Max(1, (int)Math.Ceiling(size.Width) + leftMargin + rightMargin);
                        int textHeight = Math.Max(1, (int)Math.Ceiling(size.Height));
                        lprc->right = lprc->left + width;
                        lprc->bottom = lprc->top + textHeight;
                        height = textHeight;
                        return true;
                    }

                    s_gdiPaintHits++;
                    // GDI text draws in device coords: real DrawTextEx ignores
                    // the GDI+ world transform (TPF paints absolute element
                    // rects under absolute translates), while our DrawString
                    // emulation applies it, double-adding the offset. Reset
                    // to identity around the paint; page unit/scale stay.
                    global::System.Drawing.Drawing2D.Matrix savedXf = graphics.Transform;
                    graphics.Transform = new global::System.Drawing.Drawing2D.Matrix();
                    try
                    {
                    if (state.BkMode == 2) // BACKGROUND_MODE.OPAQUE
                    {
                        using var bk = new global::System.Drawing.SolidBrush(FromColorRef(state.BkColor));
                        graphics.FillRectangle(bk, rect);
                    }

                    var layout = new global::System.Drawing.RectangleF(
                        rect.X + leftMargin,
                        rect.Y,
                        Math.Max(1, rect.Width - leftMargin - rightMargin),
                        Math.Max(1, rect.Height));
                    using var brush = new global::System.Drawing.SolidBrush(FromColorRef(state.TextColor));
                    bool singleLine = format.HasFlag(DRAW_TEXT_FORMAT.DT_SINGLELINE)
                        || (cleaned.IndexOf('\n') < 0 && cleaned.IndexOf('\r') < 0);
                    if (!(singleLine && sf.Alignment == global::System.Drawing.StringAlignment.Near
                        && TryPaintWordsRect(graphics, cleaned, font, brush, layout, sf)))
                        graphics.DrawString(cleaned, font, brush, layout, sf);
                    }
                    finally { graphics.Transform = savedXf; }
                    height = font.Height;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            internal static unsafe bool TryDrawThemeText(HDC hdc, ReadOnlySpan<char> text, RECT rect, uint flags)
            {
                RECT r = rect;
                return TryDrawText(hdc, text, &r, (DRAW_TEXT_FORMAT)flags, null, out _);
            }

            internal static int s_extCalls;
            internal static string? s_fallbackError;
            internal static unsafe bool TryExtTextOut(HDC hdc, int x, int y, ETO_OPTIONS options, RECT* lprect, ReadOnlySpan<char> text)
            {
                if (text.IsEmpty) return false;
                if (!WasmFillBridge.TryGetTarget(hdc, out global::System.Drawing.Graphics? graphics) || graphics is null) return false;

                TextState state = StateFor(hdc);
                global::System.Drawing.Font font = state.Font ?? global::System.Drawing.SystemFonts.DefaultFont;

                try
                {
                    // Same GDI device-coords rule as TryDrawText: ExtTextOut
                    // ignores the GDI+ world transform.
                    global::System.Drawing.Drawing2D.Matrix savedXf = graphics.Transform;
                    graphics.Transform = new global::System.Drawing.Drawing2D.Matrix();
                    try
                    {
                    if (options.HasFlag(ETO_OPTIONS.ETO_OPAQUE) && lprect is not null)
                    {
                        using var bk = new global::System.Drawing.SolidBrush(FromColorRef(state.BkColor));
                        Rectangle opaqueRect = *lprect;
                        graphics.FillRectangle(bk, opaqueRect);
                    }

                    s_extCalls++;
                    string cleaned = text.ToString();
                    using var sf = new global::System.Drawing.StringFormat
                    {
                        FormatFlags = global::System.Drawing.StringFormatFlags.NoWrap,
                    };
                    float px = x;
                    float py = y;
                    uint horizontal = state.TextAlign & 6u; // TA_RIGHT=2, TA_CENTER=6
                    if (horizontal != 0)
                    {
                        float emAlign = font.Unit == global::System.Drawing.GraphicsUnit.Pixel
                        ? font.Size
                        : font.Size * 96f / 72f;
                    float width = global::System.Drawing.GdiAbcTable.TryMeasureString(font.Name, emAlign, cleaned, out int abcAlign)
                        ? abcAlign
                        : graphics.MeasureStringTight(cleaned, font).Width;
                        px = horizontal == 6u ? x - (width / 2f) : x - width;
                    }

                    uint vertical = state.TextAlign & 24u; // TA_BOTTOM=8, TA_BASELINE=24
                    if (vertical == 8u)
                    {
                        py = y - font.Height;
                    }
                    else if (vertical == 24u)
                    {
                        py = y - (font.Height * 0.8f);
                    }

                    using var brush = new global::System.Drawing.SolidBrush(FromColorRef(state.TextColor));
                    if (!TryPaintWords(graphics, cleaned, font, brush, px, py, sf))
                        graphics.DrawString(cleaned, font, brush, new global::System.Drawing.PointF(px, py), sf);
                    }
                    finally { graphics.Transform = savedXf; }
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static global::System.Drawing.Color FromColorRef(uint colorRef)
                => global::System.Drawing.ColorTranslator.FromWin32((int)colorRef);

            private static global::System.Drawing.StringFormat BuildFormat(DRAW_TEXT_FORMAT format)
            {
                var sf = new global::System.Drawing.StringFormat();
                if (format.HasFlag(DRAW_TEXT_FORMAT.DT_CENTER))
                {
                    sf.Alignment = global::System.Drawing.StringAlignment.Center;
                }
                else if (format.HasFlag(DRAW_TEXT_FORMAT.DT_RIGHT))
                {
                    sf.Alignment = global::System.Drawing.StringAlignment.Far;
                }

                if (format.HasFlag(DRAW_TEXT_FORMAT.DT_VCENTER))
                {
                    sf.LineAlignment = global::System.Drawing.StringAlignment.Center;
                }
                else if (format.HasFlag(DRAW_TEXT_FORMAT.DT_BOTTOM))
                {
                    sf.LineAlignment = global::System.Drawing.StringAlignment.Far;
                }

                if (format.HasFlag(DRAW_TEXT_FORMAT.DT_SINGLELINE))
                {
                    sf.FormatFlags |= global::System.Drawing.StringFormatFlags.NoWrap;
                }

                if (format.HasFlag(DRAW_TEXT_FORMAT.DT_END_ELLIPSIS))
                {
                    sf.Trimming = global::System.Drawing.StringTrimming.EllipsisCharacter;
                }

                return sf;
            }

            private static string CleanPrefix(ReadOnlySpan<char> text, bool keepPrefix)
            {
                if (keepPrefix || text.IndexOf('&') < 0) return text.ToString();
                var sb = new global::System.Text.StringBuilder(text.Length);
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = text[i];
                    if (ch == '&' && i + 1 < text.Length)
                    {
                        if (text[i + 1] == '&')
                        {
                            sb.Append('&');
                            i++;
                        }

                        continue;
                    }

                    sb.Append(ch);
                }

                return sb.ToString();
            }
        }
        public static BOOL MoveToEx(HDC hdc, int x, int y, out Point lppt) { lppt = Point.Empty; return (BOOL)true; }
        public static unsafe BOOL MoveToEx(HDC hdc, int x, int y, Point* lppt) => (BOOL)true;
        public static unsafe uint MsgWaitForMultipleObjectsEx(uint nCount, HANDLE* pHandles, uint dwMilliseconds, QUEUE_STATUS_FLAGS dwWakeMask, MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS dwFlags) => 0;
        public static uint OemKeyScan(ushort wOemChar) => 0;
        public static unsafe HRESULT OleCreatePropertyFrame(HWND hwndOwner, uint x, uint y, PCWSTR lpszCaption, uint cObjects, void** ppUnk, uint cPages, Guid* pPageClsID, uint lcid, uint dwReserved = 0, void* pvReserved = null) => HRESULT.S_OK;
        public static unsafe HRESULT OleCreatePropertyFrame(HWND hwndOwner, uint x, uint y, string? lpszCaption, uint cObjects, void** ppUnk, uint cPages, Guid* pPageClsID, uint lcid, uint dwReserved = 0, void* pvReserved = null) => HRESULT.S_OK;
        public static unsafe HRESULT OleCreatePropertyFrameIndirect(void* pParams) => HRESULT.S_OK;
        public static HDESK OpenInputDesktop(uint dwFlags, BOOL fInherit, DESKTOP_ACCESS_FLAGS dwDesiredAccess) => default;
        public static unsafe HTHEME OpenThemeData(HWND hwnd, PCWSTR pszClassList) => default;
        public static unsafe HTHEME OpenThemeData(HWND hwnd, string? pszClassList) => default;
        public static BOOL PatBlt(HDC hdc, int x, int y, int w, int h, ROP_CODE rop) => (BOOL)true;
        public static void PostQuitMessage(int nExitCode) { }

        // Batch 6
        public static BOOL PostThreadMessage(uint idThread, uint Msg, WPARAM wParam, LPARAM lParam) => (BOOL)true;
        public static unsafe ushort RegisterClass(WNDCLASSW* lpWndClass) => 1;
        public static ushort RegisterClass(in WNDCLASSW lpWndClass) => 1;
        public static unsafe uint RegisterWindowMessage(PCWSTR lpString) => 0xC000;
        public static uint RegisterWindowMessage(string lpString) => 0xC000;
        public static BOOL ReleaseCapture() { global::System.Windows.Forms.WasmCaptureState.Clear(); return (BOOL)true; }
        public static unsafe ITEMIDLIST* SHBrowseForFolder(BROWSEINFOW* lpbi) => null;
        public static unsafe HRESULT SHGetKnownFolderPath(Guid* rfid, uint dwFlags, HANDLE hToken, PWSTR* ppszPath) => HRESULT.E_FAIL;
        public static unsafe BOOL SHGetPathFromIDListEx(ITEMIDLIST* pidl, PWSTR pszPath, uint cchPath, GPFIDL_FLAGS uOpts) => (BOOL)false;
        public static unsafe HRESULT SHGetSpecialFolderLocation(HWND hwnd, int csidl, ITEMIDLIST** ppidl) => HRESULT.E_FAIL;
        public static unsafe HRESULT SHGetSpecialFolderLocation(int csidl, out ITEMIDLIST* ppidl) { ppidl = null; return HRESULT.E_FAIL; }
        public static unsafe HRESULT SHCreateItemFromParsingName(string pszPath, IBindCtx* pbc, in Guid riid, out void* ppv) { ppv = null; return HRESULT.E_NOTIMPL; }
        public static nuint SetTimer(HWND hWnd, nuint nIDEvent, uint uElapse, TIMERPROC? lpTimerFunc = null) => 1;
        public static LRESULT SendDlgItemMessage(HWND hDlg, int nIDDlgItem, uint Msg, WPARAM wParam, LPARAM lParam) => default;
        public static unsafe BOOL SendMessageCallback(HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam, delegate* unmanaged[Stdcall]<HWND, uint, nuint, LRESULT, void> lpResultCallBack, nuint dwData) => (BOOL)true;
        public static unsafe uint SendInput(uint cInputs, INPUT* pInputs, int cbSize) => cInputs;
        public static unsafe LRESULT SendMessageTimeout(HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam, SEND_MESSAGE_TIMEOUT_FLAGS fuFlags, uint uTimeout, nuint* lpdwResult) => default;
        public static HCURSOR SetCursor(HCURSOR hCursor) => hCursor;
        public static BOOL SetCursorPos(int x, int y) => (BOOL)true;
        public static unsafe BOOL SetKeyboardState(byte* lpKeyState) => (BOOL)true;
        public static BOOL SetProcessDPIAware() => (BOOL)true;
        public static HRESULT SetProcessDpiAwareness(PROCESS_DPI_AWARENESS value) => HRESULT.S_OK;
        public static BOOL SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT value) => (BOOL)true;
        public static BOOL SetSuspendState(BOOL bHibernate, BOOL bForce, BOOL bWakeupEventsDisabled) => (BOOL)true;
        public static DPI_AWARENESS_CONTEXT SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT dpiContext) => dpiContext;
        public static DPI_HOSTING_BEHAVIOR SetThreadDpiHostingBehavior(DPI_HOSTING_BEHAVIOR value) => value;
        public static int SetScrollInfo(HWND hWnd, SCROLLBAR_CONSTANTS nBar, in SCROLLINFO lpsi, BOOL redraw) => 0;
        public static unsafe int SetScrollInfo(HWND hWnd, SCROLLBAR_CONSTANTS nBar, SCROLLINFO* lpsi, BOOL redraw) => 0;
        public static unsafe BOOL SetViewportExtEx(HDC hdc, int x, int y, SIZE* lpsz) => (BOOL)true;
        public static unsafe BOOL SetViewportOrgEx(HDC hdc, int x, int y, Point* lppt) => (BOOL)true;
        public static HWINEVENTHOOK SetWinEventHook(uint eventMin, uint eventMax, HMODULE hmodWinEventProc, WINEVENTPROC pfnWinEventProc, uint idProcess, uint idThread, uint dwFlags) => default;
        public static BOOL SetWindowDisplayAffinity(HWND hWnd, WINDOW_DISPLAY_AFFINITY dwAffinity) => (BOOL)true;
        public static unsafe BOOL SetWindowExtEx(HDC hdc, int x, int y, SIZE* lpsz) => (BOOL)true;
        public static unsafe BOOL SetWindowOrgEx(HDC hdc, int x, int y, Point* lppt) => (BOOL)true;
        public static unsafe BOOL SetWindowPlacement(HWND hWnd, WINDOWPLACEMENT* lpwndpl) => (BOOL)true;
        public static BOOL SetWindowPlacement(HWND hWnd, in WINDOWPLACEMENT lpwndpl) => (BOOL)true;
        public static unsafe HRESULT SetWindowTheme(HWND hwnd, PCWSTR pszSubAppName, PCWSTR pszSubIdList) => HRESULT.S_OK;
        public static unsafe HRESULT SetWindowTheme(HWND hwnd, string? pszSubAppName, string? pszSubIdList) => HRESULT.S_OK;
        public static HHOOK SetWindowsHookEx(WINDOWS_HOOK_ID idHook, HOOKPROC lpfn, HINSTANCE hmod, uint dwThreadId) => default;
        public static unsafe HINSTANCE ShellExecute(HWND hwnd, PCWSTR lpOperation, PCWSTR lpFile, PCWSTR lpParameters, PCWSTR lpDirectory, SHOW_WINDOW_CMD nShowCmd) => default;
        public static unsafe HINSTANCE ShellExecute(HWND hwnd, string? lpOperation, string? lpFile, string? lpParameters, string? lpDirectory, SHOW_WINDOW_CMD nShowCmd) => default;
        public static BOOL ShowCaret(HWND hWnd) => (BOOL)true;

        // Batch 7
        public static int ShowCursor(BOOL bShow) => bShow ? 0 : -1;
        public static unsafe HRESULT StgOpenStorageOnILockBytes(void* plkbyt, void* pstgPriority, STGM grfMode, void* snbExclude, uint reserved, void** ppstgOpen) => HRESULT.E_FAIL;
        public static unsafe int StretchDIBits(HDC hdc, int xDest, int yDest, int DestWidth, int DestHeight, int xSrc, int ySrc, int SrcWidth, int SrcHeight, void* lpBits, BITMAPINFO* lpbmi, DIB_USAGE iUsage, ROP_CODE rop) => 0;
        public static unsafe HRESULT TaskDialogIndirect(void* pTaskConfig, int* pnButton, int* pnRadioButton, BOOL* pfVerificationFlagChecked) => HRESULT.S_OK;
        public static unsafe BOOL TrackMouseEvent(TRACKMOUSEEVENT* lpEventTrack) => (BOOL)true;
        public static BOOL TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack) => (BOOL)true;
        public static unsafe BOOL TranslateMDISysAccel(HWND hWndClient, MSG* lpMsg) => (BOOL)false;
        public static BOOL TranslateMDISysAccel(HWND hWndClient, in MSG lpMsg) => (BOOL)false;
        public static unsafe BOOL TranslateMessage(MSG* lpMsg) => (BOOL)true;
        public static BOOL TranslateMessage(in MSG lpMsg) => (BOOL)true;
        public static BOOL UiaClientsAreListening() => (BOOL)false;
        public static unsafe HRESULT UiaGetReservedNotSupportedValue(void** punkNotSupportedValue) => HRESULT.S_OK;
        public static unsafe HRESULT UiaHostProviderFromHwnd(HWND hwnd, IRawElementProviderSimple** ppProvider) => HRESULT.S_OK;
        public static unsafe HRESULT UiaRaiseAutomationEvent(void* pProvider, UIA_EVENT_ID id) => HRESULT.S_OK;
        public static unsafe HRESULT UiaRaiseAutomationPropertyChangedEvent(void* pProvider, UIA_PROPERTY_ID id, VARIANT oldValue, VARIANT newValue) => HRESULT.S_OK;
        public static unsafe HRESULT UiaRaiseStructureChangedEvent(void* pProvider, StructureChangeType structureChangeType, int* pRuntimeId, int cRuntimeIdLen) => HRESULT.S_OK;
        public static unsafe HRESULT UiaRaiseNotificationEvent(IRawElementProviderSimple* provider, NotificationKind notificationKind, NotificationProcessing notificationProcessing, BSTR displayString, BSTR activityId) => HRESULT.S_OK;
        public static BOOL UnhookWinEvent(HWINEVENTHOOK hWinEventHook) => (BOOL)true;
        public static unsafe HRESULT VarFormat(VARIANT* pvarIn, PWSTR pstrTemplate, VARFORMAT_FIRST_DAY nFirstDay, VARFORMAT_FIRST_WEEK nFirstWeek, uint dwFlags, BSTR* pbstrOut) => HRESULT.S_OK;
        public static unsafe HRESULT VarFormat(VARIANT* pvarIn, PCWSTR pstrTemplate, VARFORMAT_FIRST_DAY nFirstDay, VARFORMAT_FIRST_WEEK nFirstWeek, uint dwFlags, BSTR* pbstrOut) => HRESULT.S_OK;
        public static int GetClipRgn(HDC hdc, HRGN hrgn) => 0;
        public static BOOL UnhookWindowsHookEx(HHOOK hhk) => (BOOL)true;
        public static unsafe BOOL UnhookWindowsHookEx(void* hhk) => (BOOL)true;
        public static BOOL UnhookWindowsHookEx<T>(T hhk) where T : IHandle<HHOOK> => (BOOL)true;
        public static short VkKeyScan(char ch) => 0;
        public static BOOL WaitMessage() => (BOOL)true;
        public static HWND WindowFromPoint(Point Point) => default;

        // Missing SDK methods
        public static unsafe uint GetModuleFileName(HINSTANCE hModule, char* lpFilename, uint nSize) => 0;
        public static unsafe uint GetModuleFileName(HINSTANCE hModule, PWSTR lpFilename, uint nSize) => 0;
        public static unsafe HRESULT SHParseDisplayName(PCWSTR pszName, void* pbc, out ITEMIDLIST* ppidl, uint sfgaoIn, uint* psfgaoOut = null) { ppidl = null; return HRESULT.E_NOTIMPL; }
        public static unsafe HRESULT SHParseDisplayName(string? pszName, void* pbc, out ITEMIDLIST* ppidl, uint sfgaoIn, uint* psfgaoOut = null) { ppidl = null; return HRESULT.E_NOTIMPL; }
        public static unsafe HRESULT SHCreateShellItem(ITEMIDLIST* pidlParent, void* psfParent, ITEMIDLIST* pidl, IShellItem** ppsi) { if (ppsi != null) *ppsi = null; return HRESULT.E_NOTIMPL; }

        // ImageList methods (managed store: WasmImageListStore; designer
        // streams hydrate via WasmResourceShim, Images.Add stays
        // unimplemented - HBITMAP pixels are unavailable headless)
        // Managed insertion: hbmImage is a trackable fake minted by
        // Bitmap.GetHbitmap (see BitmapHandleMap); the store keeps the
        // actual pixels. The mask is ignored: decoded and 32bpp content
        // carries alpha intrinsically; sub-32bpp mask folding is a known
        // gap. Unresolvable handles keep the honest -1 failure.
        public static int ImageList_Add(HIMAGELIST himl, HBITMAP hbmImage, HBITMAP hbmMask)
        {
            if (global::System.Drawing.BitmapHandleMap.TryResolve((nint)hbmImage.Value, out global::System.Drawing.Bitmap? image)
                && image is not null)
            {
                return global::System.Windows.Forms.WasmImageListStore.AddImage((nint)himl.Value, image);
            }
            return -1;
        }
        public static BOOL ImageList_Destroy(HIMAGELIST himl) { global::System.Windows.Forms.WasmImageListStore.Destroy(himl); return (BOOL)true; }
        public static BOOL ImageList_Draw(HIMAGELIST himl, int i, HDC hdcDst, int x, int y, IMAGE_LIST_DRAW_STYLE fStyle) => (BOOL)global::System.Windows.Forms.WasmImageListStore.Draw(himl, i, hdcDst, x, y, 0, 0);
        public static BOOL ImageList_DrawEx(HIMAGELIST himl, int i, HDC hdcDst, int x, int y, int dx, int dy, COLORREF rgbBk, COLORREF rgbFg, IMAGE_LIST_DRAW_STYLE fStyle) => (BOOL)global::System.Windows.Forms.WasmImageListStore.Draw(himl, i, hdcDst, x, y, dx, dy);
        public static unsafe BOOL ImageList_GetIconSize(HIMAGELIST himl, int* cx, int* cy) { if (global::System.Windows.Forms.WasmImageListStore.GetIconSize(himl, out int w, out int h)) { if (cx != null) *cx = w; if (cy != null) *cy = h; return (BOOL)true; } if (cx != null) *cx = 0; if (cy != null) *cy = 0; return (BOOL)false; }
        public static int ImageList_GetImageCount(HIMAGELIST himl) => global::System.Windows.Forms.WasmImageListStore.GetCount(himl);
        // The native strip contract: hbmImage addresses the whole horizontal
        // strip and rcImage is the frame rect, so GetBitmap can LockBits +
        // copy the frame exactly like desktop. The strip is cached per list
        // version and rooted there (a method-local would die with GC before
        // FromHbitmap resolves its weak handle); superseded handles release.
        // Unknown token/slot keeps the honest false.
        public static BOOL ImageList_GetImageInfo(HIMAGELIST himl, int i, out IMAGEINFO pImageInfo)
        {
            pImageInfo = default;
            nint token = (nint)himl.Value;
            if (!global::System.Windows.Forms.WasmImageListStore.GetIconSize(himl, out int w, out int h)) return (BOOL)false;
            int count = global::System.Windows.Forms.WasmImageListStore.GetCount(himl);
            if (i < 0 || i >= count) return (BOOL)false;
            if (!global::System.Windows.Forms.WasmImageListStore.TryGetStripHandle(token, i, out nint img)) return (BOOL)false;
            // (TryGetStripHandle above guarantees a live, store-rooted strip handle.)
            // (registration is owned by the versioned store cache; no per-call Register.)
            nint msk = 0x61000000 | (1 << 16) | (i & 0xFFFF);
            pImageInfo = new IMAGEINFO { hbmImage = (HBITMAP)img, hbmMask = (HBITMAP)msk, rcImage = new RECT(i * w, 0, (i + 1) * w, h) };
            return (BOOL)true;
        }
        public static BOOL ImageList_Remove(HIMAGELIST himl, int i) => (BOOL)global::System.Windows.Forms.WasmImageListStore.RemoveAt((nint)himl.Value, i);
        public static BOOL ImageList_Replace(HIMAGELIST himl, int i, HBITMAP hbmImage, HBITMAP hbmMask)
        {
            if (global::System.Drawing.BitmapHandleMap.TryResolve((nint)hbmImage.Value, out global::System.Drawing.Bitmap? image)
                && image is not null)
            {
                return (BOOL)global::System.Windows.Forms.WasmImageListStore.SetImage((nint)himl.Value, i, image);
            }
            return (BOOL)false;
        }
        public static int ImageList_ReplaceIcon(HIMAGELIST himl, int i, HICON hicon) => -1;
        public static COLORREF ImageList_SetBkColor(HIMAGELIST himl, COLORREF clrBk) => default;
        public static unsafe BOOL ImageList_Write(HIMAGELIST himl, IStream* pstm) => (BOOL)true;
        public static unsafe HRESULT ImageList_WriteEx(HIMAGELIST himl, IMAGE_LIST_WRITE_STREAM_FLAGS dwFlags, IStream* pstm) => HRESULT.S_OK;
    }
}
