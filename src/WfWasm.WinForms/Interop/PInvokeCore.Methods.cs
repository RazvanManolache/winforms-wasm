// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;
using Windows.Win32.System.WinRT;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32
{
    internal static partial class PInvokeCore
    {
        internal static HDC BeginPaint(HWND hWnd, out PAINTSTRUCT lpPaint)
        {
            lpPaint = default;
            return default;
        }

        internal static unsafe HDC BeginPaint(HWND hWnd, PAINTSTRUCT* lpPaint) => default;

        internal static BOOL EndPaint(HWND hWnd, in PAINTSTRUCT lpPaint) => (BOOL)true;
        internal static unsafe BOOL EndPaint(HWND hWnd, PAINTSTRUCT* lpPaint) => (BOOL)true;

        // No message queue exists in the browser: always report "no
        // message". This leaf must exist: the upstream generic
        // PeekMessage<T> wrapper forwards here, and the CsWin32-generated
        // PInvoke sibling is not compiled into the fork, so without it
        // the wrapper resolves back to itself and recurses to stack
        // overflow on the first modal message pump (New dialog).
        internal static unsafe BOOL PeekMessage(MSG* lpMsg, HWND hWnd, uint wMsgFilterMin, uint wMsgFilterMax, PEEK_MESSAGE_REMOVE_TYPE wRemoveMsg) => (BOOL)false;

        internal static LRESULT DefWindowProc(HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => default;

        internal static HWND GetDesktopWindow() => default;
        internal static HWND GetForegroundWindow() => default;
        internal static uint GetCurrentThreadId() => 1u;
        internal static uint GetThreadLocale() => 0x0409u;
        internal static unsafe uint GetWindowThreadProcessId(HWND hWnd, uint* lpdwProcessId = null) => 1;
        internal static unsafe int MapWindowPoints(HWND hWndFrom, HWND hWndTo, Point* lpPoints, uint cPoints) => 0;

        internal static HDC GetDC(HWND hWnd) => (HDC)(nint)1;
        internal static HDC GetDCEx(HWND hWnd, HRGN hrgnClip, GET_DCX_FLAGS flags) => (HDC)(nint)1;
        internal static HDC GetWindowDC(HWND hWnd) => (HDC)(nint)1;
        internal static int ReleaseDC(HWND hWnd, HDC hDC) => 1;

        internal static int GetDeviceCaps(HDC hdc, GET_DEVICE_CAPS_INDEX index) => index switch
        {
            GET_DEVICE_CAPS_INDEX.LOGPIXELSX => 96,
            GET_DEVICE_CAPS_INDEX.LOGPIXELSY => 96,
            GET_DEVICE_CAPS_INDEX.BITSPIXEL => 32,
            GET_DEVICE_CAPS_INDEX.PLANES => 1,
            _ => 0
        };

        internal static int GetSystemMetrics(SYSTEM_METRICS_INDEX nIndex) => nIndex switch
        {
            SYSTEM_METRICS_INDEX.SM_CXSCREEN => 1920,
            SYSTEM_METRICS_INDEX.SM_CYSCREEN => 1080,
            SYSTEM_METRICS_INDEX.SM_CXFULLSCREEN => 1920,
            SYSTEM_METRICS_INDEX.SM_CYFULLSCREEN => 1040,
            SYSTEM_METRICS_INDEX.SM_CXMAXTRACK => 10000,
            SYSTEM_METRICS_INDEX.SM_CYMAXTRACK => 10000,
            SYSTEM_METRICS_INDEX.SM_CXMAXIMIZED => 1920,
            SYSTEM_METRICS_INDEX.SM_CYMAXIMIZED => 1080,
            SYSTEM_METRICS_INDEX.SM_CXMINTRACK => 100,
            SYSTEM_METRICS_INDEX.SM_CYMINTRACK => 50,
            SYSTEM_METRICS_INDEX.SM_CXMIN => 100,
            SYSTEM_METRICS_INDEX.SM_CYMIN => 50,
            SYSTEM_METRICS_INDEX.SM_CXBORDER => 1,
            SYSTEM_METRICS_INDEX.SM_CYBORDER => 1,
            SYSTEM_METRICS_INDEX.SM_CXEDGE => 2,
            SYSTEM_METRICS_INDEX.SM_CYEDGE => 2,
            SYSTEM_METRICS_INDEX.SM_CYCAPTION => 23,
            SYSTEM_METRICS_INDEX.SM_CYMENU => 20,
            SYSTEM_METRICS_INDEX.SM_CXDRAG => 4,
            SYSTEM_METRICS_INDEX.SM_CYDRAG => 4,
            SYSTEM_METRICS_INDEX.SM_MOUSEPRESENT => 1,
            SYSTEM_METRICS_INDEX.SM_MOUSEWHEELPRESENT => 1,
            SYSTEM_METRICS_INDEX.SM_CMOUSEBUTTONS => 3,
            SYSTEM_METRICS_INDEX.SM_CMONITORS => 1,
            _ => 0
        };
        internal static uint GetSysColor(SYS_COLOR_INDEX nIndex) => 0;
        internal static HGDIOBJ GetStockObject(GET_STOCK_OBJECT_FLAGS i) => (HGDIOBJ)(nint)1;
        internal static OBJ_TYPE GetObjectType(HGDIOBJ h) => OBJ_TYPE.OBJ_MEMDC;

        internal static unsafe HRESULT CoCreateInstance<T>(in Guid rclsid, IUnknown* pUnkOuter, CLSCTX dwClsContext, out T* ppv) where T : unmanaged
        {
            ppv = null;
            return HRESULT.E_NOTIMPL;
        }

        internal static unsafe HRESULT CoCreateInstance<T>(Guid rclsid, IUnknown* pUnkOuter, CLSCTX dwClsContext, out T* ppv) where T : unmanaged
        {
            ppv = null;
            return HRESULT.E_NOTIMPL;
        }

        internal static BOOL DrawIconEx(HDC hdc, int xLeft, int yTop, HICON hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, HBRUSH hbrFlickerFreeDraw, DI_FLAGS diFlags) => (BOOL)true;
        internal static unsafe BOOL EnumChildWindows(HWND hWndParent, delegate* unmanaged[Stdcall]<HWND, LPARAM, BOOL> lpEnumFunc, LPARAM lParam) => (BOOL)true;
        internal static unsafe BOOL EnumWindows(delegate* unmanaged[Stdcall]<HWND, LPARAM, BOOL> lpEnumFunc, LPARAM lParam) => (BOOL)true;
        internal static unsafe BOOL EnumDisplayMonitors(HDC hdc, RECT* lprcClip, delegate* unmanaged[Stdcall]<HMONITOR, HDC, RECT*, LPARAM, BOOL> lpfnEnum, LPARAM dwData) => (BOOL)true;
        internal static BOOL GetIconInfo(HICON hIcon, out ICONINFO piconinfo) { piconinfo = default; return (BOOL)true; }
        internal static unsafe BOOL GetIconInfo(HICON hIcon, ICONINFO* piconinfo) => (BOOL)true;
        internal static unsafe int GetObject(HGDIOBJ hgdiobj, int cbBuffer, void* lpvObject) => cbBuffer;
        internal static unsafe uint GetPaletteEntries(HPALETTE hpal, uint iStart, uint cEntries, PALETTEENTRY* pEntries) => cEntries;
        internal static unsafe int GetWindowText(HWND hWnd, PWSTR lpString, int nMaxCount) => WindowTextStorage.CopyTo(hWnd, lpString, nMaxCount);
        internal static unsafe BOOL SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION uiAction, uint uiParam, void* pvParam, SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS fWinIni)
        {
            // No system parameters in the browser: answer desktop defaults
            // for the scalar int queries we know (hover metrics feed
            // ToolStrip's MouseHoverTimer; Interval=0 throws). Struct/bool
            // queries are left untouched, exactly as before.
            if (pvParam != null)
            {
                switch (uiAction)
                {
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSEHOVERTIME:
                        *(int*)pvParam = 400;
                        break;
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSEHOVERHEIGHT:
                    case SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETMOUSEHOVERWIDTH:
                        *(int*)pvParam = 4;
                        break;
                }
            }

            return (BOOL)true;
        }

        internal static unsafe uint GetRegionData(HRGN hrgn, uint nCount, RGNDATA* lpRgnData) => 0;

        internal static HDC CreateCompatibleDC(HDC hdc) => (HDC)1;
        internal static BOOL DeleteDC(HDC hdc) => (BOOL)true;
        internal static BOOL DeleteObject(HGDIOBJ ho) => (BOOL)true;
        internal static BOOL DestroyIcon(HICON hIcon) => (BOOL)true;

        internal static unsafe HBITMAP CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, void* lpBits) => (HBITMAP)(nint)1;
        internal static HBITMAP CreateCompatibleBitmap(HDC hdc, int cx, int cy) => (HBITMAP)(nint)1;

        internal static unsafe HBITMAP CreateDIBSection(HDC hdc, in BITMAPINFO pbmi, DIB_USAGE usage, void** ppvBits, HANDLE hSection, uint offset) => (HBITMAP)(nint)1;
        internal static unsafe HBITMAP CreateDIBSection(HDC hdc, BITMAPINFO* pbmi, DIB_USAGE usage, void** ppvBits, HANDLE hSection, uint offset) => (HBITMAP)(nint)1;
        internal static unsafe HBITMAP CreateDIBSection(HDC hdc, void* pbmi, DIB_USAGE usage, void** ppvBits, HANDLE hSection, uint offset) => (HBITMAP)(nint)1;

        internal static unsafe HFONT CreateFontIndirect(LOGFONTW* lplf) => (HFONT)(nint)1;
        internal static HFONT CreateFontIndirect(in LOGFONTW lplf) => (HFONT)(nint)1;

        internal static HPEN CreatePen(PEN_STYLE iStyle, int cWidth, COLORREF color) => (HPEN)(nint)1;
        internal static HBRUSH CreateSolidBrush(COLORREF color) => (HBRUSH)(nint)1;
        internal static HRGN CreateRectRgn(int x1, int y1, int x2, int y2) => (HRGN)(nint)1;
        internal static GDI_REGION_TYPE CombineRgn(HRGN hrgnDst, HRGN hrgnSrc1, HRGN hrgnSrc2, RGN_COMBINE_MODE iMode) => GDI_REGION_TYPE.SIMPLEREGION;

        internal static unsafe HDC CreateDCW(PCWSTR pwszDriver, PCWSTR pwszDevice, PCWSTR pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateDCW(char* pwszDriver, char* pwszDevice, char* pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateDCW(string? pwszDriver, string? pwszDevice, string? pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateICW(PCWSTR pwszDriver, PCWSTR pwszDevice, PCWSTR pszPort, DEVMODEW* pdm) => (HDC)(nint)1;
        internal static unsafe HDC CreateICW(char* pwszDriver, char* pwszDevice, char* pszPort, DEVMODEW* pdm) => (HDC)(nint)1;

        internal static HANDLE CopyImage(HANDLE h, GDI_IMAGE_TYPE type, int cx, int cy, IMAGE_FLAGS flags) => h;

        internal static HGDIOBJ SelectObject(HDC hdc, HGDIOBJ h) => (HGDIOBJ)(nint)1;
        internal static GDI_REGION_TYPE SelectClipRgn(HDC hdc, HRGN hrgn) => GDI_REGION_TYPE.SIMPLEREGION;
        internal static HPALETTE SelectPalette(HDC hdc, HPALETTE hPal, BOOL bForceBkgd) => default;
        internal static uint RealizePalette(HDC hdc) => 0;

        internal static int SaveDC(HDC hdc) => 1;
        internal static BOOL RestoreDC(HDC hdc, int nSavedDC) => (BOOL)true;

        internal static COLORREF SetBkColor(HDC hdc, COLORREF color) => default;
        internal static BACKGROUND_MODE SetBkMode(HDC hdc, BACKGROUND_MODE mode) => mode;
        internal static COLORREF SetTextColor(HDC hdc, COLORREF color) => default;
        internal static HDC_MAP_MODE SetMapMode(HDC hdc, HDC_MAP_MODE iMode) => iMode;
        internal static R2_MODE SetROP2(HDC hdc, R2_MODE rop2) => rop2;
        internal static TEXT_ALIGN_OPTIONS SetTextAlign(HDC hdc, TEXT_ALIGN_OPTIONS align) => align;

        internal static int GetClipRgn(HDC hdc, HRGN hrgn) => 0;

        internal static unsafe BOOL GetViewportOrgEx(HDC hdc, Point* lppt) => (BOOL)true;
        internal static BOOL GetViewportOrgEx(HDC hdc, out Point lppt) { lppt = Point.Empty; return (BOOL)true; }

        internal static unsafe BOOL GetWindowOrgEx(HDC hdc, Point* lppt) => (BOOL)true;
        internal static BOOL GetWindowOrgEx(HDC hdc, out Point lppt) { lppt = Point.Empty; return (BOOL)true; }

        internal static unsafe BOOL OffsetViewportOrgEx(HDC hdc, int x, int y, Point* lppt) => (BOOL)true;

        internal static unsafe BOOL GetWorldTransform(HDC hdc, XFORM* lpxf)
        {
            if (lpxf != null)
            {
                *lpxf = new XFORM { eM11 = 1.0f, eM22 = 1.0f };
            }
            return (BOOL)true;
        }

        internal static BOOL GetWorldTransform(HDC hdc, out XFORM lpxf)
        {
            lpxf = new XFORM { eM11 = 1.0f, eM22 = 1.0f };
            return (BOOL)true;
        }

        internal static BOOL Rectangle(HDC hdc, int left, int top, int right, int bottom) => (BOOL)true;

        internal static unsafe void* GlobalLock(HGLOBAL hMem) => (void*)hMem.Value;
        internal static BOOL GlobalUnlock(HGLOBAL hMem) => (BOOL)false;
        internal static nuint GlobalSize(HGLOBAL hMem) => 0;
        internal static HGLOBAL GlobalReAlloc(HGLOBAL hMem, nuint dwBytes, uint uFlags) => hMem;

        internal static HMONITOR MonitorFromPoint(Point pt, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;
        internal static unsafe HMONITOR MonitorFromRect(RECT* lprc, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;
        internal static HMONITOR MonitorFromRect(in RECT lprc, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;
        internal static HMONITOR MonitorFromWindow(HWND hwnd, MONITOR_FROM_FLAGS dwFlags) => (HMONITOR)(nint)1;

        internal static unsafe BOOL GetMonitorInfo(HMONITOR hMonitor, MONITORINFO* lpmi)
        {
            if (lpmi != null)
            {
                lpmi->cbSize = (uint)sizeof(MONITORINFO);
                lpmi->rcMonitor = new RECT(0, 0, 1920, 1080);
                lpmi->rcWork = new RECT(0, 0, 1920, 1040);
                lpmi->dwFlags = 1;
            }
            return (BOOL)true;
        }

        internal static BOOL GetMonitorInfo(HMONITOR hMonitor, ref MONITORINFO lpmi)
        {
            lpmi.cbSize = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>();
            lpmi.rcMonitor = new RECT(0, 0, 1920, 1080);
            lpmi.rcWork = new RECT(0, 0, 1920, 1040);
            lpmi.dwFlags = 1;
            return (BOOL)true;
        }

        internal static uint RegisterClipboardFormat(PCWSTR lpszFormat) => 0xC000u;
        internal static uint RegisterClipboardFormat(string lpszFormat) => 0xC000u;

        internal static unsafe int GetClipboardFormatName(uint format, PWSTR lpszFormatName, int cchMaxCount) => 0;
        internal static unsafe int GetClipboardFormatName(uint format, char* lpszFormatName, int cchMaxCount) => 0;

        internal static unsafe HRESULT OleInitialize(void* pvReserved) => HRESULT.S_OK;
        internal static void OleUninitialize() { }

        internal static unsafe HRESULT OleGetClipboard(IDataObject** ppDataObj) => HRESULT.E_FAIL;
        internal static unsafe HRESULT OleGetClipboard(void* ppDataObj) => HRESULT.E_FAIL;

        internal static unsafe HRESULT OleSetClipboard(IDataObject* pDataObj) => HRESULT.S_OK;
        internal static unsafe HRESULT OleSetClipboard(void* pDataObj) => HRESULT.S_OK;

        internal static HRESULT OleFlushClipboard() => HRESULT.S_OK;
        internal static HANDLE OleDuplicateData(HANDLE hSrc, CLIPBOARD_FORMAT cfFormat, global::Windows.Win32.System.Memory.GLOBAL_ALLOC_FLAGS uiFlags) => hSrc;

        internal static unsafe void ReleaseStgMedium(STGMEDIUM* pmedium) { }
        internal static void ReleaseStgMedium(ref STGMEDIUM pmedium) { }

        internal static unsafe HRESULT OleCreatePictureIndirect(PICTDESC* lpPictDesc, Guid* riid, BOOL fOwn, void** lplpObj) => HRESULT.E_NOTIMPL;
        internal static unsafe HRESULT OleCreatePictureIndirect(PICTDESC* lpPictDesc, in Guid riid, BOOL fOwn, void* lplpObj) => HRESULT.E_NOTIMPL;
        internal static unsafe HRESULT OleCreatePictureIndirect(PICTDESC* lpPictDesc, in Guid riid, BOOL fOwn, out object? lplpObj)
        {
            lplpObj = null;
            return HRESULT.E_NOTIMPL;
        }

        internal static unsafe HRESULT DoDragDrop(IDataObject* pDataObj, IDropSource* pDropSource, DROPEFFECT dwOKEffects, DROPEFFECT* pdwEffect)
        {
            if (pdwEffect != null) *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            return HRESULT.DRAGDROP_S_CANCEL;
        }

        internal static unsafe HRESULT DoDragDrop(void* pDataObj, void* pDropSource, DROPEFFECT dwOKEffects, out DROPEFFECT pdwEffect)
        {
            pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            return HRESULT.DRAGDROP_S_CANCEL;
        }

        internal static unsafe HRESULT DoDragDrop(void* pDataObj, void* pDropSource, uint dwOKEffects, out uint pdwEffect)
        {
            pdwEffect = 0;
            return HRESULT.DRAGDROP_S_CANCEL;
        }

        internal static unsafe uint DragQueryFile(HDROP hDrop, uint iFile, PWSTR lpszFile, uint cch) => 0;

        internal static BOOL BitBlt(HDC hdc, int x, int y, int cx, int cy, HDC hdcSrc, int x1, int y1, ROP_CODE rop) => (BOOL)true;
        internal static GDI_REGION_TYPE IntersectClipRect(HDC hdc, int left, int top, int right, int bottom) => GDI_REGION_TYPE.SIMPLEREGION;
        internal static LRESULT SendMessage(HWND hWnd, uint Msg, WPARAM wParam = default, LPARAM lParam = default) => default;

        internal static unsafe HRESULT CoCreateInstance(Guid* rclsid, void* pUnkOuter, CLSCTX dwClsContext, Guid* riid, void** ppv) => HRESULT.E_NOINTERFACE;
        internal static unsafe HRESULT CoCreateInstance(in Guid rclsid, void* pUnkOuter, CLSCTX dwClsContext, in Guid riid, void** ppv) => HRESULT.E_NOINTERFACE;
        internal static unsafe HRESULT CoCreateInstance(in Guid rclsid, void* pUnkOuter, CLSCTX dwClsContext, in Guid riid, out object? ppv)
        {
            ppv = null;
            return HRESULT.E_NOINTERFACE;
        }

        internal static unsafe HRESULT PropVariantClear(PROPVARIANT* pvar) => HRESULT.S_OK;

        internal static unsafe HRESULT SafeArrayDestroy(SAFEARRAY* psa) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayLock(SAFEARRAY* psa) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayUnlock(SAFEARRAY* psa) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayGetElement(SAFEARRAY* psa, int* rgIndices, void* pv) => HRESULT.S_OK;
        internal static unsafe HRESULT SafeArrayPutElement(SAFEARRAY* psa, int* rgIndices, void* pv) => HRESULT.S_OK;

        internal static unsafe int WideCharToMultiByte(uint codePage, uint dwFlags, PCWSTR lpWideCharStr, int cchWideChar, PSTR lpMultiByteStr, int cbMultiByte, PCSTR lpDefaultChar, BOOL* lpUsedDefaultChar) => 0;
        internal static unsafe int WideCharToMultiByte(uint codePage, uint dwFlags, string lpWideCharStr, int cchWideChar, PSTR lpMultiByteStr, int cbMultiByte, PCSTR lpDefaultChar) => 0;
        internal static unsafe int WideCharToMultiByte(uint codePage, uint dwFlags, string lpWideCharStr, int cchWideChar, void* lpMultiByteStr, int cbMultiByte, void* lpDefaultChar) => 0;

        internal static unsafe HRESULT WindowsCreateString(PCWSTR sourceString, uint length, HSTRING* @string) => HRESULT.S_OK;
        internal static HRESULT WindowsDeleteString(HSTRING @string) => HRESULT.S_OK;

        internal static unsafe HRESULT RoActivateInstance(HSTRING activatableClassId, IInspectable** instance) => HRESULT.E_NOTIMPL;

        internal static unsafe HRESULT LoadRegTypeLib(Guid* rguid, ushort wVerMajor, ushort wVerMinor, uint lcid, void** pptlib) => HRESULT.TYPE_E_LIBNOTREGISTERED;
        internal static unsafe HRESULT LoadRegTypeLib(in Guid rguid, ushort wVerMajor, ushort wVerMinor, uint lcid, void** pptlib) => HRESULT.TYPE_E_LIBNOTREGISTERED;

        internal static unsafe BOOL EnumThreadWindows(uint dwThreadId, delegate* unmanaged[Stdcall]<HWND, LPARAM, BOOL> lpfn, LPARAM lParam) => (BOOL)true;
        internal static unsafe BOOL SystemParametersInfoForDpi(uint uiAction, uint uiParam, void* pvParam, uint fWinIni, uint dpi) => (BOOL)true;

        internal static BOOL GetClientRect(HWND hWnd, out RECT lpRect)
        {
            var r = WindowBoundsStorage.Get(hWnd);
            lpRect = new RECT(0, 0, Math.Max(0, r.Width), Math.Max(0, r.Height));
            return (BOOL)true;
        }
        internal static BOOL GetWindowRect(HWND hWnd, out RECT lpRect)
        {
            var r = WindowBoundsStorage.Get(hWnd);
            lpRect = new RECT(r.Left, r.Top, r.Right, r.Bottom);
            return (BOOL)true;
        }
        internal static int GetWindowTextLength(HWND hWnd) => WindowTextStorage.GetLength(hWnd);
        internal static BOOL PostMessage(HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => (BOOL)true;
        internal static HRESULT RevokeDragDrop(HWND hwnd) => HRESULT.S_OK;
        internal static unsafe LRESULT CallWindowProc(delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> lpPrevWndFunc, HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => default;
        internal static unsafe LRESULT CallWindowProc(void* lpPrevWndFunc, HWND hWnd, uint Msg, WPARAM wParam, LPARAM lParam) => default;
    }
}

