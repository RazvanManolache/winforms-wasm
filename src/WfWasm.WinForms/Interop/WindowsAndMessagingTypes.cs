// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Windows.Win32.UI.WindowsAndMessaging
{
    [Flags]
    internal enum MESSAGEBOX_STYLE : uint
    {
        MB_OK = 0x00000000,
        MB_OKCANCEL = 0x00000001,
        MB_ABORTRETRYIGNORE = 0x00000002,
        MB_YESNOCANCEL = 0x00000003,
        MB_YESNO = 0x00000004,
        MB_RETRYCANCEL = 0x00000005,
        MB_CANCELTRYCONTINUE = 0x00000006,
        MB_ICONHAND = 0x00000010,
        MB_ICONQUESTION = 0x00000020,
        MB_ICONEXCLAMATION = 0x00000030,
        MB_ICONASTERISK = 0x00000040,
        MB_USERICON = 0x00000080,
        MB_ICONWARNING = 0x00000030,
        MB_ICONERROR = 0x00000010,
        MB_ICONINFORMATION = 0x00000040,
        MB_ICONSTOP = 0x00000010,
        MB_DEFBUTTON1 = 0x00000000,
        MB_DEFBUTTON2 = 0x00000100,
        MB_DEFBUTTON3 = 0x00000200,
        MB_DEFBUTTON4 = 0x00000300,
        MB_APPLMODAL = 0x00000000,
        MB_SYSTEMMODAL = 0x00001000,
        MB_TASKMODAL = 0x00002000,
        MB_HELP = 0x00004000,
        MB_NOFOCUS = 0x00008000,
        MB_SETFOREGROUND = 0x00010000,
        MB_DEFAULT_DESKTOP_ONLY = 0x00020000,
        MB_TOPMOST = 0x00040000,
        MB_RIGHT = 0x00080000,
        MB_RTLREADING = 0x00100000,
        MB_SERVICE_NOTIFICATION = 0x00200000,
        MB_TYPEMASK = 0x0000000F,
        MB_ICONMASK = 0x000000F0,
        MB_DEFMASK = 0x00000F00,
        MB_MODEMASK = 0x00003000,
        MB_MISCMASK = 0x0000C000,
    }

    internal enum MESSAGEBOX_RESULT
    {
        IDOK = 1,
        IDCANCEL = 2,
        IDABORT = 3,
        IDRETRY = 4,
        IDIGNORE = 5,
        IDYES = 6,
        IDNO = 7,
        IDCLOSE = 8,
        IDHELP = 9,
        IDTRYAGAIN = 10,
        IDCONTINUE = 11,
    }

    [Flags]
    internal enum ODS_FLAGS : uint
    {
        ODS_SELECTED = 0x0001,
        ODS_GRAYED = 0x0002,
        ODS_DISABLED = 0x0004,
        ODS_CHECKED = 0x0008,
        ODS_FOCUS = 0x0010,
        ODS_DEFAULT = 0x0020,
        ODS_HOTLIGHT = 0x0040,
        ODS_INACTIVE = 0x0080,
        ODS_NOACCEL = 0x0100,
        ODS_NOFOCUSRECT = 0x0200,
        ODS_COMBOBOXEDIT = 0x1000,
    }

    internal enum DFC_TYPE : uint
    {
        DFC_CAPTION = 1,
        DFC_MENU = 2,
        DFC_SCROLL = 3,
        DFC_BUTTON = 4,
        DFC_POPUPMENU = 5,
    }

    [Flags]
    internal enum DFCS_STATE : uint
    {
        DFCS_CAPTIONCLOSE = 0x0000,
        DFCS_CAPTIONMIN = 0x0001,
        DFCS_CAPTIONMAX = 0x0002,
        DFCS_CAPTIONRESTORE = 0x0003,
        DFCS_CAPTIONHELP = 0x0004,
        DFCS_MENUARROW = 0x0000,
        DFCS_MENUCHECK = 0x0001,
        DFCS_MENUBULLET = 0x0002,
        DFCS_MENUARROWRIGHT = 0x0004,
        DFCS_SCROLLUP = 0x0000,
        DFCS_SCROLLDOWN = 0x0001,
        DFCS_SCROLLLEFT = 0x0002,
        DFCS_SCROLLRIGHT = 0x0003,
        DFCS_SCROLLCOMBOBOX = 0x0005,
        DFCS_SCROLLSIZEGRIP = 0x0008,
        DFCS_SCROLLSIZEGRIPRIGHT = 0x0010,
        DFCS_BUTTONCHECK = 0x0000,
        DFCS_BUTTONRADIOIMAGE = 0x0001,
        DFCS_BUTTONRADIOMASK = 0x0002,
        DFCS_BUTTONRADIO = 0x0004,
        DFCS_BUTTON3STATE = 0x0008,
        DFCS_BUTTONPUSH = 0x0010,
        DFCS_INACTIVE = 0x0100,
        DFCS_PUSHED = 0x0200,
        DFCS_CHECKED = 0x0400,
        DFCS_TRANSPARENT = 0x0800,
        DFCS_HOT = 0x1000,
        DFCS_ADJUSTRECT = 0x2000,
        DFCS_FLAT = 0x4000,
        DFCS_MONO = 0x8000,
    }

    internal enum SCROLLBAR_COMMAND : int
    {
        SB_LINEUP = 0,
        SB_LINELEFT = 0,
        SB_LINEDOWN = 1,
        SB_LINERIGHT = 1,
        SB_PAGEUP = 2,
        SB_PAGELEFT = 2,
        SB_PAGEDOWN = 3,
        SB_PAGERIGHT = 3,
        SB_THUMBPOSITION = 4,
        SB_THUMBTRACK = 5,
        SB_TOP = 6,
        SB_LEFT = 6,
        SB_BOTTOM = 7,
        SB_RIGHT = 7,
        SB_ENDSCROLL = 8,
    }

    [Flags]
    internal enum MINIMIZEDMETRICS_ARRANGE : int
    {
        ARW_BOTTOMLEFT = 0x0000,
        ARW_BOTTOMRIGHT = 0x0001,
        ARW_TOPLEFT = 0x0002,
        ARW_TOPRIGHT = 0x0003,
        ARW_STARTLEFT = 0x0000,
        ARW_STARTUP = 0x0004,
        ARW_HIDE = 0x0008,
    }

    internal enum CWP_FLAGS : uint
    {
        CWP_ALL = 0x0000,
        CWP_SKIPINVISIBLE = 0x0001,
        CWP_SKIPDISABLED = 0x0002,
        CWP_SKIPTRANSPARENT = 0x0004,
    }

    [Flags]
    internal enum DI_FLAGS : uint
    {
        DI_MASK = 0x0001,
        DI_IMAGE = 0x0002,
        DI_NORMAL = 0x0003,
        DI_COMPAT = 0x0004,
        DI_DEFAULTSIZE = 0x0008,
        DI_NOMIRROR = 0x0010,
    }

    internal enum PEEK_MESSAGE_REMOVE_TYPE : uint
    {
        PM_NOREMOVE = 0x0000,
        PM_REMOVE = 0x0001,
        PM_NOYIELD = 0x0002,
    }

    internal enum GET_ANCESTOR_FLAGS : uint
    {
        GA_PARENT = 1,
        GA_ROOT = 2,
        GA_ROOTOWNER = 3,
    }

    [Flags]
    internal enum MENU_ITEM_FLAGS : uint
    {
        MF_BYCOMMAND = 0x00000000,
        MF_BYPOSITION = 0x00000400,
        MF_ENABLED = 0x00000000,
        MF_GRAYED = 0x00000001,
        MF_DISABLED = 0x00000002,
        MF_CHECKED = 0x00000008,
        MF_UNCHECKED = 0x00000000,
        MF_POPUP = 0x00000010,
        MF_SEPARATOR = 0x00000800,
        MF_STRING = 0x00000000,
        MF_OWNERDRAW = 0x00000100,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PAINTSTRUCT
    {
        public HDC hdc;
        public BOOL fErase;
        public RECT rcPaint;
        public BOOL fRestore;
        public BOOL fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public TRACKMOUSEEVENT_FLAGS dwFlags;
        public HWND hwndTrack;
        public uint dwHoverTime;
    }

    [Flags]
    internal enum TRACKMOUSEEVENT_FLAGS : uint
    {
        TME_HOVER = 0x00000001,
        TME_LEAVE = 0x00000002,
        TME_NONCLIENT = 0x00000010,
        TME_QUERY = 0x40000000,
        TME_CANCEL = 0x80000000,
    }

    [Flags]
    internal enum MOUSE_EVENT_FLAGS : uint
    {
        MOUSEEVENTF_MOVE = 0x0001,
        MOUSEEVENTF_LEFTDOWN = 0x0002,
        MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008,
        MOUSEEVENTF_RIGHTUP = 0x0010,
        MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040,
        MOUSEEVENTF_XDOWN = 0x0080,
        MOUSEEVENTF_XUP = 0x0100,
        MOUSEEVENTF_WHEEL = 0x0800,
        MOUSEEVENTF_HWHEEL = 0x1000,
        MOUSEEVENTF_MOVE_NOCOALESCE = 0x2000,
        MOUSEEVENTF_VIRTUALDESK = 0x4000,
        MOUSEEVENTF_ABSOLUTE = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HIGHCONTRASTW
    {
        public uint cbSize;
        public HIGHCONTRASTW_FLAGS dwFlags;
        public PWSTR lpszDefaultScheme;
    }

    [Flags]
    internal enum HIGHCONTRASTW_FLAGS : uint
    {
        HCF_HIGHCONTRASTON = 0x00000001,
        HCF_AVAILABLE = 0x00000002,
        HCF_HOTKEYACTIVE = 0x00000004,
        HCF_CONFIRMHOTKEY = 0x00000008,
        HCF_HOTKEYSOUND = 0x00000010,
        HCF_INDICATOR = 0x00000020,
        HCF_HOTKEYAVAILABLE = 0x00000040,
    }

    internal delegate void TIMERPROC(HWND hWnd, uint uMsg, nuint nIDEvent, uint dwTime);
}
