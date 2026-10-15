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
        public static BOOL DestroyWindow(HWND hWnd) => (BOOL)true;
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
        public static unsafe HRESULT DrawThemeText(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, PCWSTR pszText, int cchText, uint dwTextFlags, uint dwTextFlags2, RECT* pRect) => HRESULT.S_OK;
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
            int lineCount = 1;
            float currentLineWidth = 0;
            float maxLineWidth = 0;
            bool noPrefix = format.HasFlag(DRAW_TEXT_FORMAT.DT_NOPREFIX);

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\r') continue;
                if (ch == '\n')
                {
                    lineCount++;
                    if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;
                    currentLineWidth = 0;
                    continue;
                }

                if (ch == '&' && !noPrefix)
                {
                    if (i + 1 < text.Length && text[i + 1] == '&')
                    {
                        i++;
                    }
                    else
                    {
                        continue;
                    }
                }

                float charW = ch switch
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

                currentLineWidth += charW;
            }

            if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;

            int lineHeight = 16;
            int totalWidth = (int)Math.Ceiling(maxLineWidth);
            int totalHeight = lineCount * lineHeight;

            if (lpdtp != null)
            {
                totalWidth += lpdtp->iLeftMargin + lpdtp->iRightMargin;
            }

            if (format.HasFlag(DRAW_TEXT_FORMAT.DT_CALCRECT))
            {
                lprc->right = lprc->left + Math.Max(1, totalWidth);
                lprc->bottom = lprc->top + Math.Max(lineHeight, totalHeight);
            }

            return Math.Max(lineHeight, totalHeight);
        }
        public static unsafe HRESULT DrawThemeText(HTHEME hTheme, HDC hdc, int iPartId, int iStateId, string pszText, int cchText, uint dwTextFlags, uint dwTextFlags2, ref RECT pRect) => HRESULT.S_OK;
        public static unsafe BOOL DuplicateHandle(HANDLE hSourceProcessHandle, HANDLE hSourceHandle, HANDLE hTargetProcessHandle, HANDLE* lpTargetHandle, uint dwDesiredAccess, BOOL bInheritHandle, DUPLICATE_HANDLE_OPTIONS dwOptions) => (BOOL)true;
        public static unsafe HRESULT DwmGetWindowAttribute(HWND hwnd, DWMWINDOWATTRIBUTE dwAttribute, void* pvAttribute, uint cbAttribute) => HRESULT.S_OK;
        public static unsafe HRESULT DwmSetWindowAttribute(HWND hwnd, DWMWINDOWATTRIBUTE dwAttribute, void* pvAttribute, uint cbAttribute) => HRESULT.S_OK;
        public static BOOL Ellipse(HDC hdc, int left, int top, int right, int bottom) => (BOOL)true;
        public static unsafe BOOL EnumDisplaySettings(PCWSTR lpszDeviceName, ENUM_DISPLAY_SETTINGS_MODE iModeNum, DEVMODEW* lpDevMode) => (BOOL)false;
        public static BOOL EnumDisplaySettings(string? lpszDeviceName, ENUM_DISPLAY_SETTINGS_MODE iModeNum, ref DEVMODEW lpDevMode) => (BOOL)false;
        public static unsafe BOOL ExtTextOut(HDC hdc, int x, int y, ETO_OPTIONS options, RECT* lprect, PCWSTR lpString, uint c, int* lpDx) => (BOOL)true;
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
        public static HWND GetActiveWindow() => default;
        public static short GetAsyncKeyState(int vKey) => 0;
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
        public static COLORREF GetTextColor(HDC hdc) => default;
        public static unsafe BOOL GetTextMetrics(HDC hdc, void* lptm)
        {
            if (lptm != null)
            {
                var tm = (Windows.Win32.Graphics.Gdi.TEXTMETRICW*)lptm;
                tm->tmHeight = 13;
                tm->tmAveCharWidth = 7;
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
                // thread forever — no nested pump can receive browser events.
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
        public static HIMAGELIST ImageList_Create(int cx, int cy, IMAGELIST_CREATION_FLAGS flags, int cInitial, int cGrow) => default;
        public static HIMAGELIST ImageList_Duplicate(HIMAGELIST himl) => default;
        public static unsafe HIMAGELIST ImageList_Read(void* pstm) => default;
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
        public static BOOL IsAppThemed() => (BOOL)true;
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

            internal static IDisposable TrackTarget(HDC hdc, global::System.Drawing.Graphics? graphics)
            {
                if (graphics is null) return NullScope.Instance;
                lock (s_lock)
                {
                    s_targets ??= new();
                    s_targets[hdc.Value] = graphics;
                }

                return new UnregisterOnDispose(hdc, graphics);
            }

            internal static void NoteBrushColor(HBRUSH brush, COLORREF color)
            {
                lock (s_lock)
                {
                    s_brushColors ??= new();
                    s_brushColors[brush.Value] = color.Value;
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

        // ImageList methods
        public static int ImageList_Add(HIMAGELIST himl, HBITMAP hbmImage, HBITMAP hbmMask) => -1;
        public static BOOL ImageList_Destroy(HIMAGELIST himl) => (BOOL)true;
        public static BOOL ImageList_Draw(HIMAGELIST himl, int i, HDC hdcDst, int x, int y, IMAGE_LIST_DRAW_STYLE fStyle) => (BOOL)true;
        public static BOOL ImageList_DrawEx(HIMAGELIST himl, int i, HDC hdcDst, int x, int y, int dx, int dy, COLORREF rgbBk, COLORREF rgbFg, IMAGE_LIST_DRAW_STYLE fStyle) => (BOOL)true;
        public static unsafe BOOL ImageList_GetIconSize(HIMAGELIST himl, int* cx, int* cy) { if (cx != null) *cx = 0; if (cy != null) *cy = 0; return (BOOL)true; }
        public static int ImageList_GetImageCount(HIMAGELIST himl) => 0;
        public static BOOL ImageList_GetImageInfo(HIMAGELIST himl, int i, out IMAGEINFO pImageInfo) { pImageInfo = default; return (BOOL)true; }
        public static BOOL ImageList_Remove(HIMAGELIST himl, int i) => (BOOL)true;
        public static BOOL ImageList_Replace(HIMAGELIST himl, int i, HBITMAP hbmImage, HBITMAP hbmMask) => (BOOL)true;
        public static int ImageList_ReplaceIcon(HIMAGELIST himl, int i, HICON hicon) => -1;
        public static COLORREF ImageList_SetBkColor(HIMAGELIST himl, COLORREF clrBk) => default;
        public static unsafe BOOL ImageList_Write(HIMAGELIST himl, IStream* pstm) => (BOOL)true;
        public static unsafe HRESULT ImageList_WriteEx(HIMAGELIST himl, IMAGE_LIST_WRITE_STREAM_FLAGS dwFlags, IStream* pstm) => HRESULT.S_OK;
    }
}
