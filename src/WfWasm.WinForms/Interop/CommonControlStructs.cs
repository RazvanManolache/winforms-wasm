// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Windows.Win32.UI.Controls
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct INITCOMMONCONTROLSEX
    {
        public uint dwSize;
        public INITCOMMONCONTROLSEX_ICC dwICC;
    }

    [Flags]
    public enum INITCOMMONCONTROLSEX_ICC : uint
    {
        ICC_LISTVIEW_CLASSES = 0x00000001,
        ICC_TREEVIEW_CLASSES = 0x00000002,
        ICC_BAR_CLASSES = 0x00000004,
        ICC_TAB_CLASSES = 0x00000008,
        ICC_UPDOWN_CLASS = 0x00000010,
        ICC_PROGRESS_CLASS = 0x00000020,
        ICC_HOTKEY_CLASS = 0x00000040,
        ICC_ANIMATE_CLASS = 0x00000080,
        ICC_WIN95_CLASSES = 0x000000FF,
        ICC_DATE_CLASSES = 0x00000100,
        ICC_USEREX_CLASSES = 0x00000200,
        ICC_COOL_CLASSES = 0x00000400,
        ICC_INTERNET_CLASSES = 0x00000800,
        ICC_PAGESCROLLER_CLASS = 0x00001000,
        ICC_NATIVEFNTCTL_CLASS = 0x00002000,
        ICC_STANDARD_CLASSES = 0x00004000,
        ICC_LINK_CLASS = 0x00008000,
    }

    [Flags]
    public enum NMCUSTOMDRAW_DRAW_STAGE : uint
    {
        CDDS_PREPAINT = 0x00000001,
        CDDS_POSTPAINT = 0x00000002,
        CDDS_PREERASE = 0x00000003,
        CDDS_POSTERASE = 0x00000004,
        CDDS_ITEM = 0x00010000,
        CDDS_ITEMPREPAINT = 0x00010001,
        CDDS_ITEMPOSTPAINT = 0x00010002,
        CDDS_ITEMPREERASE = 0x00010003,
        CDDS_ITEMPOSTERASE = 0x00010004,
        CDDS_SUBITEM = 0x00020000,
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NMCUSTOMDRAW
    {
        public NMHDR hdr;
        public NMCUSTOMDRAW_DRAW_STAGE dwDrawStage;
        public HDC hdc;
        public RECT rc;
        public nuint dwItemSpec;
        public NMCUSTOMDRAW_DRAW_STATE_FLAGS uItemState;
        public nint lItemlParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVCUSTOMDRAW
    {
        public NMCUSTOMDRAW nmcd;
        public COLORREF clrText;
        public COLORREF clrTextBk;
        public int iSubItem;
        public NMLVCUSTOMDRAW_ITEM_TYPE dwItemType;
        public COLORREF clrFace;
        public int iIconEffect;
        public int iIconPhase;
        public int iPartId;
        public int iStateId;
        public RECT rcText;
        public uint uAlign;
    }

    public enum NMLVCUSTOMDRAW_ITEM_TYPE : uint
    {
        LVCDI_ITEM = 0x00000000,
        LVCDI_GROUP = 0x00000001,
        LVCDI_ITEMSLIST = 0x00000002,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMTVCUSTOMDRAW
    {
        public NMCUSTOMDRAW nmcd;
        public COLORREF clrText;
        public COLORREF clrTextBk;
        public int iLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLISTVIEW
    {
        public NMHDR hdr;
        public int iItem;
        public int iSubItem;
        public uint uNewState;
        public uint uOldState;
        public LIST_VIEW_ITEM_FLAGS uChanged;
        public Point ptAction;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVDISPINFOW
    {
        public NMHDR hdr;
        public LVITEMW item;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVKEYDOWN
    {
        public NMHDR hdr;
        public ushort wVKey;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVCACHEHINT
    {
        public NMHDR hdr;
        public int iFrom;
        public int iTo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVODSTATECHANGE
    {
        public NMHDR hdr;
        public int iFrom;
        public int iTo;
        public LIST_VIEW_ITEM_STATE_FLAGS uNewState;
        public LIST_VIEW_ITEM_STATE_FLAGS uOldState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NMLVGETINFOTIPW
    {
        public NMHDR hdr;
        public uint dwFlags;
        public PWSTR pszText;
        public int cchTextMax;
        public int iItem;
        public int iSubItem;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVFINDITEMW
    {
        public NMHDR hdr;
        public int iStart;
        public LVFINDINFOW lvfi;
    }

    [Flags]
    public enum LVFINDINFOW_FLAGS : uint
    {
        LVFI_PARAM = 0x0001,
        LVFI_STRING = 0x0002,
        LVFI_SUBSTRING = 0x0004,
        LVFI_PARTIAL = 0x0008,
        LVFI_WRAP = 0x0020,
        LVFI_NEARESTXY = 0x0040,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LVFINDINFOW
    {
        public LVFINDINFOW_FLAGS flags;
        public PCWSTR psz;
        public nint lParam;
        public Point pt;
        public uint vkDirection;
    }

    [Flags]
    public enum LVCOLUMNW_FORMAT : int
    {
        LVCFMT_LEFT = 0x0000,
        LVCFMT_RIGHT = 0x0001,
        LVCFMT_CENTER = 0x0002,
        LVCFMT_JUSTIFYMASK = 0x0003,
        LVCFMT_IMAGE = 0x0800,
        LVCFMT_BITMAP_ON_RIGHT = 0x1000,
        LVCFMT_COL_HAS_IMAGES = 0x8000,
        LVCFMT_FIXED_WIDTH = 0x00100,
        LVCFMT_NO_DPI_SCALE = 0x40000,
        LVCFMT_FIXED_RATIO = 0x80000,
        LVCFMT_LINE_BREAK = 0x100000,
        LVCFMT_FILL = 0x200000,
        LVCFMT_WRAP = 0x400000,
        LVCFMT_NO_TITLE = 0x800000,
        LVCFMT_SPLITBUTTON = 0x1000000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LVCOLUMNW
    {
        public LVCOLUMNW_MASK mask;
        public LVCOLUMNW_FORMAT fmt;
        public int cx;
        public PWSTR pszText;
        public int cchTextMax;
        public int iSubItem;
        public int iImage;
        public int iOrder;
        public int cxMin;
        public int cxDefault;
        public int cxIdeal;
    }

    [Flags]
    public enum LIST_VIEW_GROUP_STATE_FLAGS : uint
    {
        LVGS_NORMAL = 0x00000000,
        LVGS_COLLAPSED = 0x00000001,
        LVGS_HIDDEN = 0x00000002,
        LVGS_NOHEADER = 0x00000004,
        LVGS_COLLAPSIBLE = 0x00000008,
        LVGS_FOCUSED = 0x00000010,
        LVGS_SELECTED = 0x00000020,
        LVGS_SUBSETED = 0x00000040,
        LVGS_SUBSETLINKFOCUSED = 0x00000080,
    }

    [Flags]
    public enum LIST_VIEW_GROUP_ALIGN_FLAGS : uint
    {
        LVGA_HEADER_LEFT = 0x00000001,
        LVGA_HEADER_CENTER = 0x00000002,
        LVGA_HEADER_RIGHT = 0x00000004,
        LVGA_FOOTER_LEFT = 0x00000008,
        LVGA_FOOTER_CENTER = 0x00000010,
        LVGA_FOOTER_RIGHT = 0x00000020,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LVGROUP
    {
        public uint cbSize;
        public LVGROUP_MASK mask;
        public PWSTR pszHeader;
        public int cchHeader;
        public PWSTR pszFooter;
        public int cchFooter;
        public int iGroupId;
        public LIST_VIEW_GROUP_STATE_FLAGS stateMask;
        public LIST_VIEW_GROUP_STATE_FLAGS state;
        public LIST_VIEW_GROUP_ALIGN_FLAGS uAlign;
        public PWSTR pszSubtitle;
        public uint cchSubtitle;
        public PWSTR pszTask;
        public uint cchTask;
        public PWSTR pszDescriptionTop;
        public uint cchDescriptionTop;
        public PWSTR pszDescriptionBottom;
        public uint cchDescriptionBottom;
        public int iTitleImage;
        public int iExtendedImage;
        public int iFirstItem;
        public uint cItems;
        public PWSTR pszSubsetTitle;
        public uint cchSubsetTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVINSERTMARK
    {
        public uint cbSize;
        public uint dwFlags;
        public int iItem;
        public uint dwReserved;
    }

    [Flags]
    public enum LIST_VIEW_BACKGROUND_IMAGE_FLAGS : uint
    {
        LVBKIF_SOURCE_NONE = 0x00000000,
        LVBKIF_SOURCE_HBITMAP = 0x00000001,
        LVBKIF_SOURCE_URL = 0x00000002,
        LVBKIF_SOURCE_MASK = 0x00000003,
        LVBKIF_STYLE_NORMAL = 0x00000000,
        LVBKIF_STYLE_TILE = 0x00000010,
        LVBKIF_STYLE_MASK = 0x00000010,
        LVBKIF_FLAG_TILEOFFSET = 0x00000100,
        LVBKIF_TYPE_WATERMARK = 0x10000000,
        LVBKIF_FLAG_ALPHABLEND = 0x20000000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LVBKIMAGEW
    {
        public LIST_VIEW_BACKGROUND_IMAGE_FLAGS ulFlags;
        public HBITMAP hbm;
        public PWSTR pszImage;
        public uint cchImageMax;
        public int xOffsetPercent;
        public int yOffsetPercent;
    }

    public enum LVTILEVIEWINFO_MASK : uint
    {
        LVTVIM_TILESIZE = 0x00000001,
        LVTVIM_COLUMNS = 0x00000002,
        LVTVIM_LABELMARGIN = 0x00000004,
    }

    [Flags]
    public enum LVTILEVIEWINFO_FLAGS : uint
    {
        LVTVIF_AUTOSIZE = 0x00000000,
        LVTVIF_FIXEDWIDTH = 0x00000001,
        LVTVIF_FIXEDHEIGHT = 0x00000002,
        LVTVIF_FIXEDSIZE = 0x00000003,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVTILEVIEWINFO
    {
        public uint cbSize;
        public LVTILEVIEWINFO_MASK dwMask;
        public LVTILEVIEWINFO_FLAGS dwFlags;
        public SIZE sizeTile;
        public int cLines;
        public RECT rcLabelMargin;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HDLAYOUT
    {
        public RECT* prc;
        public WINDOWPOS* pwpos;
    }

    [Flags]
    public enum TCITEMHEADERA_MASK : uint
    {
        TCIF_TEXT = 0x0001,
        TCIF_IMAGE = 0x0002,
        TCIF_RTLREADING = 0x0004,
        TCIF_PARAM = 0x0008,
        TCIF_STATE = 0x0010,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TCITEMW
    {
        public TCITEMHEADERA_MASK mask;
        public uint dwState;
        public uint dwStateMask;
        public PWSTR pszText;
        public int cchTextMax;
        public int iImage;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NMTTDISPINFOW
    {
        public NMHDR hdr;
        public PWSTR lpszText;
        public fixed char szText[80];
        public HINSTANCE hinst;
        public TOOLTIP_FLAGS uFlags;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TVHITTESTINFO
    {
        public Point pt;
        public TVHITTESTINFO_FLAGS flags;
        public HTREEITEM hItem;
    }

    public enum NMDATETIMECHANGE_FLAGS : uint
    {
        GDT_VALID = 0,
        GDT_NONE = 1,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMDATETIMECHANGE
    {
        public NMHDR hdr;
        public NMDATETIMECHANGE_FLAGS dwFlags;
        public SYSTEMTIME st;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NMDAYSTATE
    {
        public NMHDR hdr;
        public SYSTEMTIME stStart;
        public int cDayState;
        public uint* prgDayState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMSELCHANGE
    {
        public NMHDR hdr;
        public SYSTEMTIME stSelStart;
        public SYSTEMTIME stSelEnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMVIEWCHANGE
    {
        public NMHDR hdr;
        public MONTH_CALDENDAR_MESSAGES_VIEW dwOldView;
        public MONTH_CALDENDAR_MESSAGES_VIEW dwNewView;
    }

    public enum MCGRIDINFO_PART : uint
    {
        MCGIP_CALENDARCONTROL = 0,
        MCGIP_NEXT = 1,
        MCGIP_PREV = 2,
        MCGIP_FOOTER = 3,
        MCGIP_CALENDAR = 4,
        MCGIP_CALENDARHEADER = 5,
        MCGIP_CALENDARBODY = 6,
        MCGIP_CALENDARROW = 7,
        MCGIP_CALENDARCELL = 8,
    }

    [Flags]
    public enum MCGRIDINFO_FLAGS : uint
    {
        MCGIF_DATE = 0x00000001,
        MCGIF_RECT = 0x00000002,
        MCGIF_NAME = 0x00000004,
        MCGIR_TITLE = 0x00000001,
        MCGIR_DAY = 0x00000002,
        MCGIR_COLHEADER = 0x00000003,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MCGRIDINFO
    {
        public uint cbSize;
        public MCGRIDINFO_PART dwPart;
        public MCGRIDINFO_FLAGS dwFlags;
        public int iCalendar;
        public int iRow;
        public int iCol;
        public BOOL bSelected;
        public SYSTEMTIME stStart;
        public SYSTEMTIME stEnd;
        public RECT rc;
        public PWSTR pszName;
        public nuint cchName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    public enum THEMESIZE
    {
        TS_MIN = 0,
        TS_TRUE = 1,
        TS_DRAW = 2,
    }

    [Flags]
    public enum HIT_TEST_BACKGROUND_OPTIONS : uint
    {
        HTTB_BACKGROUNDSEG = 0x0000,
        HTTB_FIXEDBORDER = 0x0002,
        HTTB_CAPTION = 0x0004,
        HTTB_RESIZINGBORDER_LEFT = 0x0010,
        HTTB_RESIZINGBORDER_TOP = 0x0020,
        HTTB_RESIZINGBORDER_RIGHT = 0x0040,
        HTTB_RESIZINGBORDER_BOTTOM = 0x0080,
        HTTB_RESIZINGBORDER = 0x00F0,
        HTTB_SIZINGTEMPLATE = 0x0100,
        HTTB_SYSTEMSIZINGTEMPLATE = 0x0200,
    }

    public enum NotificationKind
    {
        ItemAdded = 0,
        ItemRemoved = 1,
        ActionCompleted = 2,
        ActionAborted = 3,
        Other = 4,
    }

    public enum NotificationProcessing
    {
        ImportantAll = 0,
        ImportantMostRecent = 1,
        All = 2,
        MostRecent = 3,
        CurrentThenMostRecent = 4,
    }

    [Flags]
    public enum LIST_VIEW_ITEM_FLAGS : uint
    {
        LVIF_TEXT = 0x0001,
        LVIF_IMAGE = 0x0002,
        LVIF_PARAM = 0x0004,
        LVIF_STATE = 0x0008,
        LVIF_INDENT = 0x0010,
        LVIF_NORECOMPUTE = 0x0800,
        LVIF_GROUPID = 0x0100,
        LVIF_COLUMNS = 0x0200,
        LVIF_COLFMT = 0x00010000,
    }

    public enum NM_TREEVIEW_ACTION : uint
    {
        UNKNOWN = 0x0000,
        BYMOUSE = 0x0001,
        BYKEYBOARD = 0x0002,
        TVC_UNKNOWN = 0x0000,
        TVC_BYMOUSE = 0x0001,
        TVC_BYKEYBOARD = 0x0002,
        TVE_COLLAPSE = 0x0001,
        TVE_EXPAND = 0x0002,
        TVE_TOGGLE = 0x0003,
        TVE_EXPANDPARTIAL = 0x4000,
        TVE_COLLAPSERESET = 0x8000,
    }

    [Flags]
    public enum HDI_MASK : uint
    {
        HDI_WIDTH = 0x0001,
        HDI_HEIGHT = 0x0001,
        HDI_TEXT = 0x0002,
        HDI_FORMAT = 0x0004,
        HDI_LPARAM = 0x0008,
        HDI_BITMAP = 0x0010,
        HDI_IMAGE = 0x0020,
        HDI_DI_SETITEM = 0x0040,
        HDI_ORDER = 0x0080,
        HDI_FILTER = 0x0100,
        HDI_STATE = 0x0200,
    }
}
