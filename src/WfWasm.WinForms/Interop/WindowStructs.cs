// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Windows.Win32.UI.WindowsAndMessaging
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DRAWITEMSTRUCT
    {
        public uint CtlType;
        public uint CtlID;
        public uint itemID;
        public uint itemAction;
        public ODS_FLAGS itemState;
        public HWND hwndItem;
        public HDC hDC;
        public RECT rcItem;
        public nuint itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEASUREITEMSTRUCT
    {
        public uint CtlType;
        public uint CtlID;
        public uint itemID;
        public uint itemWidth;
        public uint itemHeight;
        public nuint itemData;
    }

    [Flags]
    public enum MENU_ITEM_MASK : uint
    {
        MIIM_STATE = 0x00000001,
        MIIM_ID = 0x00000002,
        MIIM_SUBMENU = 0x00000004,
        MIIM_CHECKMARKS = 0x00000008,
        MIIM_TYPE = 0x00000010,
        MIIM_DATA = 0x00000020,
        MIIM_STRING = 0x00000040,
        MIIM_BITMAP = 0x00000080,
        MIIM_FTYPE = 0x00000100,
    }

    [Flags]
    public enum MENU_ITEM_TYPE : uint
    {
        MFT_STRING = 0x00000000,
        MFT_BITMAP = 0x00000004,
        MFT_MENUBARBREAK = 0x00000020,
        MFT_MENUBREAK = 0x00000040,
        MFT_OWNERDRAW = 0x00000100,
        MFT_RADIOCHECK = 0x00000200,
        MFT_SEPARATOR = 0x00000800,
        MFT_RIGHTORDER = 0x00002000,
        MFT_RIGHTJUSTIFY = 0x00004000,
    }

    [Flags]
    public enum MENU_ITEM_STATE : uint
    {
        MFS_GRAYED = 0x00000003,
        MFS_DISABLED = 0x00000003,
        MFS_CHECKED = 0x00000008,
        MFS_HILITE = 0x00000080,
        MFS_ENABLED = 0x00000000,
        MFS_UNCHECKED = 0x00000000,
        MFS_UNHILITE = 0x00000000,
        MFS_DEFAULT = 0x00001000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MENUITEMINFOW
    {
        public uint cbSize;
        public MENU_ITEM_MASK fMask;
        public MENU_ITEM_TYPE fType;
        public MENU_ITEM_STATE fState;
        public uint wID;
        public HMENU hSubMenu;
        public HBITMAP hbmpChecked;
        public HBITMAP hbmpUnchecked;
        public nuint dwItemData;
        public PWSTR dwTypeData;
        public uint cch;
        public HBITMAP hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public Point ptReserved;
        public Point ptMaxSize;
        public Point ptMaxPosition;
        public Point ptMinTrackSize;
        public Point ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NCCALCSIZE_PARAMS
    {
        public _rgrc_e__FixedBuffer rgrc;
        public WINDOWPOS* lppos;

        public struct _rgrc_e__FixedBuffer
        {
            public RECT _0;
            public RECT _1;
            public RECT _2;
            public ref RECT this[int index] => ref global::System.Runtime.CompilerServices.Unsafe.Add(ref _0, index);
        }
    }

    [Flags]
    public enum WINDOWPLACEMENT_FLAGS : uint
    {
        WPF_SETMINPOSITION = 0x0001,
        WPF_RESTORETOMAXIMIZED = 0x0002,
        WPF_ASYNCWINDOWPLACEMENT = 0x0004,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public uint length;
        public WINDOWPLACEMENT_FLAGS flags;
        public SHOW_WINDOW_CMD showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct WNDCLASSW
    {
        public WNDCLASS_STYLES style;
        public delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public HINSTANCE hInstance;
        public HICON hIcon;
        public HCURSOR hCursor;
        public HBRUSH hbrBackground;
        public PCWSTR lpszMenuName;
        public PCWSTR lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CLIENTCREATESTRUCT
    {
        public HMENU hWindowMenu;
        public uint idFirstChild;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct COPYDATASTRUCT
    {
        public nuint dwData;
        public uint cbData;
        public void* lpData;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EVENTMSG
    {
        public uint message;
        public uint paramL;
        public uint paramH;
        public uint time;
        public HWND hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HELPINFO
    {
        public uint cbSize;
        public int iContextType;
        public int iCtrlId;
        public HANDLE hItemHandle;
        public nuint dwContextId;
        public Point MousePos;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEHOOKSTRUCT
    {
        public Point pt;
        public HWND hwnd;
        public uint wHitTestCode;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SCROLLBARINFO
    {
        public uint cbSize;
        public RECT rcScrollBar;
        public int dxyLineButton;
        public int xyThumbTop;
        public int xyThumbBottom;
        public int reserved;
        public fixed uint rgstate[6];
    }

    [Flags]
    public enum STARTUPINFOW_FLAGS : uint
    {
        STARTF_USESHOWWINDOW = 0x00000001,
        STARTF_USESIZE = 0x00000002,
        STARTF_USEPOSITION = 0x00000004,
        STARTF_USECOUNTCHARS = 0x00000008,
        STARTF_USEFILLATTRIBUTE = 0x00000010,
        STARTF_RUNFULLSCREEN = 0x00000020,
        STARTF_FORCEONFEEDBACK = 0x00000040,
        STARTF_FORCEOFFFEEDBACK = 0x00000080,
        STARTF_USESTDHANDLES = 0x00000100,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct STARTUPINFOW
    {
        public uint cb;
        public PWSTR lpReserved;
        public PWSTR lpDesktop;
        public PWSTR lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public STARTUPINFOW_FLAGS dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public byte* lpReserved2;
        public HANDLE hStdInput;
        public HANDLE hStdOutput;
        public HANDLE hStdError;
    }

    public enum WINDOW_DISPLAY_AFFINITY : uint
    {
        WDA_NONE = 0x00000000,
        WDA_MONITOR = 0x00000001,
        WDA_EXCLUDEFROMCAPTURE = 0x00000011,
    }

    public enum SYS_COLOR_INDEX
    {
        COLOR_SCROLLBAR = 0,
        COLOR_BACKGROUND = 1,
        COLOR_ACTIVECAPTION = 2,
        COLOR_INACTIVECAPTION = 3,
        COLOR_MENU = 4,
        COLOR_WINDOW = 5,
        COLOR_WINDOWFRAME = 6,
        COLOR_MENUTEXT = 7,
        COLOR_WINDOWTEXT = 8,
        COLOR_CAPTIONTEXT = 9,
        COLOR_ACTIVEBORDER = 10,
        COLOR_INACTIVEBORDER = 11,
        COLOR_APPWORKSPACE = 12,
        COLOR_HIGHLIGHT = 13,
        COLOR_HIGHLIGHTTEXT = 14,
        COLOR_BTNFACE = 15,
        COLOR_BTNSHADOW = 16,
        COLOR_GRAYTEXT = 17,
        COLOR_BTNTEXT = 18,
        COLOR_INACTIVECAPTIONTEXT = 19,
        COLOR_BTNHIGHLIGHT = 20,
        COLOR_3DDKSHADOW = 21,
        COLOR_3DLIGHT = 22,
        COLOR_INFOTEXT = 23,
        COLOR_INFOBK = 24,
        COLOR_HOTLIGHT = 26,
        COLOR_GRADIENTACTIVECAPTION = 27,
        COLOR_GRADIENTINACTIVECAPTION = 28,
        COLOR_MENUHILIGHT = 29,
        COLOR_MENUBAR = 30,
    }

    [Flags]
    public enum ACCEL_VIRT_FLAGS : byte
    {
        FVIRTKEY = 0x01,
        FNOINVERT = 0x02,
        FSHIFT = 0x04,
        FCONTROL = 0x08,
        FALT = 0x10,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ACCEL
    {
        public ACCEL_VIRT_FLAGS fVirt;
        public ushort key;
        public ushort cmd;
    }

    internal readonly struct HDESK : IEquatable<HDESK>
    {
        public readonly nint Value;
        public HDESK(nint value) => Value = value;
        public static HDESK Null => default;
        public bool IsNull => Value == 0;
        public static implicit operator nint(HDESK value) => value.Value;
        public static explicit operator HDESK(nint value) => new(value);
        public static bool operator ==(HDESK left, HDESK right) => left.Value == right.Value;
        public static bool operator !=(HDESK left, HDESK right) => left.Value != right.Value;
        public override bool Equals(object? obj) => obj is HDESK other && Equals(other);
        public bool Equals(HDESK other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct USEROBJECTFLAGS
    {
        public BOOL fInherit;
        public BOOL fReserved;
        public uint dwFlags;
    }

    [Flags]
    public enum DESKTOP_ACCESS_FLAGS : uint
    {
        DESKTOP_READOBJECTS = 0x0001,
        DESKTOP_CREATEWINDOW = 0x0002,
        DESKTOP_CREATEMENU = 0x0004,
        DESKTOP_HOOKCONTROL = 0x0008,
        DESKTOP_JOURNALRECORD = 0x0010,
        DESKTOP_JOURNALPLAYBACK = 0x0020,
        DESKTOP_ENUMERATE = 0x0040,
        DESKTOP_WRITEOBJECTS = 0x0080,
        DESKTOP_SWITCHDESKTOP = 0x0100,
    }

    public enum USER_OBJECT_INFORMATION_INDEX
    {
        UOI_FLAGS = 1,
        UOI_NAME = 2,
        UOI_TYPE = 3,
        UOI_USER_SID = 4,
        UOI_HEAPSIZE = 5,
        UOI_IO = 6,
    }

    public enum WINDOWS_HOOK_ID
    {
        WH_MSGFILTER = -1,
        WH_JOURNALRECORD = 0,
        WH_JOURNALPLAYBACK = 1,
        WH_KEYBOARD = 2,
        WH_GETMESSAGE = 3,
        WH_CALLWNDPROC = 4,
        WH_CBT = 5,
        WH_SYSMSGFILTER = 6,
        WH_MOUSE = 7,
        WH_HARDWARE = 8,
        WH_DEBUG = 9,
        WH_SHELL = 10,
        WH_FOREGROUNDIDLE = 11,
        WH_CALLWNDPROCRET = 12,
        WH_KEYBOARD_LL = 13,
        WH_MOUSE_LL = 14,
    }

    [Flags]
    public enum SEND_MESSAGE_TIMEOUT_FLAGS : uint
    {
        SMTO_NORMAL = 0x0000,
        SMTO_BLOCK = 0x0001,
        SMTO_ABORTIFHUNG = 0x0002,
        SMTO_NOTIMEOUTIFNOTHUNG = 0x0008,
        SMTO_ERRORONEXIT = 0x0020,
    }

    [Flags]
    public enum QUEUE_STATUS_FLAGS : uint
    {
        QS_KEY = 0x0001,
        QS_MOUSEMOVE = 0x0002,
        QS_MOUSEBUTTON = 0x0004,
        QS_POSTMESSAGE = 0x0008,
        QS_TIMER = 0x0010,
        QS_PAINT = 0x0020,
        QS_SENDMESSAGE = 0x0040,
        QS_HOTKEY = 0x0080,
        QS_ALLPOSTMESSAGE = 0x0100,
        QS_RAWINPUT = 0x0400,
        QS_MOUSE = QS_MOUSEMOVE | QS_MOUSEBUTTON,
        QS_INPUT = QS_MOUSE | QS_KEY | QS_RAWINPUT,
        QS_ALLEVENTS = QS_INPUT | QS_POSTMESSAGE | QS_TIMER | QS_PAINT | QS_HOTKEY,
        QS_ALLINPUT = QS_INPUT | QS_POSTMESSAGE | QS_TIMER | QS_PAINT | QS_HOTKEY | QS_SENDMESSAGE,
    }

    [Flags]
    public enum MSG_WAIT_FOR_MULTIPLE_OBJECTS_EX_FLAGS : uint
    {
        MWMO_WAITALL = 0x0001,
        MWMO_ALERTABLE = 0x0002,
        MWMO_INPUTAVAILABLE = 0x0004,
    }

    public enum TILE_WINDOWS_HOW : uint
    {
        MDITILE_VERTICAL = 0x0000,
        MDITILE_HORIZONTAL = 0x0001,
        MDITILE_SKIPDISABLED = 0x0002,
        MDITILE_ZORDER = 0x0004,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal partial struct ICONINFO
    {
        public BOOL fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public HBITMAP hbmMask;
        public HBITMAP hbmColor;
    }

    internal delegate void WINEVENTPROC(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);
}
