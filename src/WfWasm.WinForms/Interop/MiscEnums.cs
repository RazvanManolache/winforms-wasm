// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32.UI.Accessibility
{
    internal enum OBJECT_IDENTIFIER : int
    {
        OBJID_WINDOW = 0x00000000,
        OBJID_SYSMENU = -1,
        OBJID_TITLEBAR = -2,
        OBJID_MENU = -3,
        OBJID_CLIENT = -4,
        OBJID_VSCROLL = -5,
        OBJID_HSCROLL = -6,
        OBJID_SIZEGRIP = -7,
        OBJID_CARET = -8,
        OBJID_CURSOR = -9,
        OBJID_ALERT = -10,
        OBJID_SOUND = -11,
        OBJID_QUERYCLASSNAMEIDX = -12,
        OBJID_NATIVEOM = -16,
    }
}

namespace Windows.Win32.Data.HtmlHelp
{
    internal enum HTML_HELP_COMMAND : int
    {
        HH_DISPLAY_TOPIC = 0x0000,
        HH_HELP_FINDER = 0x0000,
        HH_DISPLAY_TOC = 0x0001,
        HH_DISPLAY_INDEX = 0x0002,
        HH_DISPLAY_SEARCH = 0x0003,
        HH_SET_WIN_TYPE = 0x0004,
        HH_GET_WIN_TYPE = 0x0005,
        HH_GET_WIN_HANDLE = 0x0006,
        HH_ENUM_INFO_TYPE = 0x0007,
        HH_SET_INFO_TYPE = 0x0008,
        HH_SYNC = 0x0009,
        HH_ADD_NAVISTYLE = 0x000A,
        HH_SET_EXCLUSIVE = 0x000B,
        HH_SET_POPUP_POS = 0x000C,
        HH_CLOSE_ALL = 0x000D,
        HH_ALINK_LOOKUP = 0x000E,
        HH_KEYWORD_LOOKUP = 0x000F,
        HH_DISPLAY_TEXT_POPUP = 0x000E,
        HH_HELP_CONTEXT = 0x000F,
        HH_TP_HELP_CONTEXTMENU = 0x0010,
        HH_TP_HELP_WM_HELP = 0x0011,
        HH_FTS_DEFAULT_PROXIMITY = -1,
    }
}

namespace Windows.Win32.UI.HiDpi
{
    internal enum DPI_HOSTING_BEHAVIOR
    {
        DPI_HOSTING_BEHAVIOR_INVALID = -1,
        DPI_HOSTING_BEHAVIOR_DEFAULT = 0,
        DPI_HOSTING_BEHAVIOR_MIXED = 1,
    }
}

namespace Windows.Win32.Graphics.Dwm
{
    internal enum DWMWINDOWATTRIBUTE : uint
    {
        DWMWA_NCRENDERING_ENABLED = 1,
        DWMWA_NCRENDERING_POLICY = 2,
        DWMWA_TRANSITIONS_FORCEDISABLED = 3,
        DWMWA_ALLOW_NCPAINT = 4,
        DWMWA_CAPTION_BUTTON_BOUNDS = 5,
        DWMWA_NONCLIENT_RTL_LAYOUT = 6,
        DWMWA_FORCE_ICONIC_REPRESENTATION = 7,
        DWMWA_FLIP3D_POLICY = 8,
        DWMWA_EXTENDED_FRAME_BOUNDS = 9,
        DWMWA_HAS_ICONIC_BITMAP = 10,
        DWMWA_DISALLOW_PEEK = 11,
        DWMWA_EXCLUDED_FROM_PEEK = 12,
        DWMWA_CLOAK = 13,
        DWMWA_CLOAKED = 14,
        DWMWA_FREEZE_REPRESENTATION = 15,
        DWMWA_PASSIVE_UPDATE_MODE = 16,
        DWMWA_USE_HOSTWINDOW_FRAME_ACCENT_POLICY = 17,
        DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
        DWMWA_WINDOW_CORNER_PREFERENCE = 33,
        DWMWA_BORDER_COLOR = 34,
        DWMWA_CAPTION_COLOR = 35,
        DWMWA_TEXT_COLOR = 36,
        DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 37,
        DWMWA_SYSTEMBACKDROP_TYPE = 38,
    }
}

namespace Windows.Win32.UI.Controls
{
    internal enum MONTH_CALDENDAR_MESSAGES_VIEW : uint
    {
        MCMV_MONTH = 0,
        MCMV_YEAR = 1,
        MCMV_DECADE = 2,
        MCMV_CENTURY = 3,
    }

    [Flags]
    internal enum IMAGE_LIST_DRAW_STYLE : uint
    {
        ILD_NORMAL = 0x00000000,
        ILD_TRANSPARENT = 0x00000001,
        ILD_MASK = 0x00000010,
        ILD_IMAGE = 0x00000020,
        ILD_ROP = 0x00000040,
        ILD_BLEND25 = 0x00000002,
        ILD_BLEND50 = 0x00000004,
        ILD_OVERLAYMASK = 0x00000F00,
        ILD_PRESERVEALPHA = 0x00001000,
        ILD_SCALE = 0x00002000,
        ILD_DPISCALE = 0x00004000,
        ILD_ASYNC = 0x00008000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMTVDISPINFOW
    {
        public NMHDR hdr;
        public TVITEMW item;
    }

    [Flags]
    internal enum CFM_MASK : uint
    {
        CFM_BOLD = 0x00000001,
        CFM_ITALIC = 0x00000002,
        CFM_UNDERLINE = 0x00000004,
        CFM_STRIKEOUT = 0x00000008,
        CFM_PROTECTED = 0x00000010,
        CFM_LINK = 0x00000020,
        CFM_SIZE = 0x80000000,
        CFM_COLOR = 0x40000000,
        CFM_FACE = 0x20000000,
        CFM_OFFSET = 0x10000000,
        CFM_CHARSET = 0x08000000,
        CFM_BACKCOLOR = 0x04000000,
    }

    [Flags]
    internal enum CFE_EFFECTS : uint
    {
        CFE_BOLD = 0x0001,
        CFE_ITALIC = 0x0002,
        CFE_UNDERLINE = 0x0004,
        CFE_STRIKEOUT = 0x0008,
        CFE_PROTECTED = 0x0010,
        CFE_LINK = 0x0020,
        CFE_AUTOCOLOR = 0x40000000,
        CFE_AUTOBACKCOLOR = 0x04000000,
    }
}
