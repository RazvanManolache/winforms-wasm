// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32.UI.Shell
{
    [Flags]
    internal enum AUTOCOMPLETEOPTIONS : int
    {
        ACO_NONE = 0x00,
        ACO_AUTOSUGGEST = 0x01,
        ACO_AUTOAPPEND = 0x02,
        ACO_SEARCH = 0x04,
        ACO_FILTERPREFIXES = 0x08,
        ACO_USETAB = 0x10,
        ACO_UPDOWNKEYDROPSLIST = 0x20,
        ACO_RTLREADING = 0x40,
        ACO_WORD_FILTER = 0x80,
        ACO_NOPREFIXFILTERING = 0x100,
    }

    [Guid("EAC042D0-41E6-11D0-934D-00AA00C38873")]
    internal unsafe partial struct IAutoComplete2 : IComIID, IVTable<IAutoComplete2, IAutoComplete2.Vtbl>
    {
        private static readonly Guid s_guid = new("EAC042D0-41E6-11D0-934D-00AA00C38873");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT Init(HWND hwndEdit, IUnknown* punkACL, PCWSTR pwszRegKeyPath, PCWSTR pwszQuickComplete) => HRESULT.S_OK;
        public HRESULT Enable(BOOL fEnable) => HRESULT.S_OK;
        public HRESULT SetOptions(uint dwFlag) => HRESULT.S_OK;
        public HRESULT GetOptions(AUTOCOMPLETEOPTIONS* pdwFlag) => HRESULT.E_NOTIMPL;
        public uint AddRef() => 1;
        public uint Release() => 0;

        public interface Interface { }
    }
}

namespace Windows.Win32.UI.Controls
{
    internal enum THEME_PROPERTY_SYMBOL_ID : uint
    {
        TMT_FLATMENUS = 1001,
        TMT_MINCOLORDEPTH = 1301,
        TMT_STRING = 201,
        TMT_INT = 202,
        TMT_BOOL = 203,
        TMT_COLOR = 204,
        TMT_MARGINS = 205,
        TMT_FILENAME = 206,
        TMT_SIZE = 207,
        TMT_POSITION = 208,
        TMT_RECT = 209,
        TMT_FONT = 210,
        TMT_INTLIST = 211,
        TMT_HBITMAP = 212,
        TMT_DISKSTREAM = 213,
        TMT_STREAM = 214,
        TMT_BITMAPREF = 215,

        // Booleans
        TMT_TRANSPARENT = 2201,
        TMT_AUTOSIZE = 2202,
        TMT_BORDERONLY = 2203,
        TMT_COMPOSITED = 2204,
        TMT_BGFILL = 2205,
        TMT_GLYPHTRANSPARENT = 2206,
        TMT_GLYPHONLY = 2207,
        TMT_ALWAYSSHOWSIZINGBAR = 2208,
        TMT_MIRRORIMAGE = 2209,
        TMT_UNIFORMSIZING = 2210,
        TMT_INTEGRALSIZING = 2211,
        TMT_SOURCEGROW = 2212,
        TMT_SOURCESHRINK = 2213,

        // Integers
        TMT_IMAGECOUNT = 2401,
        TMT_ALPHALEVEL = 2402,
        TMT_BORDERSIZE = 2403,
        TMT_ROUNDCORNERWIDTH = 2404,
        TMT_ROUNDCORNERHEIGHT = 2405,
        TMT_GRADIENTRATIO1 = 2406,
        TMT_GRADIENTRATIO2 = 2407,
        TMT_GRADIENTRATIO3 = 2408,
        TMT_GRADIENTRATIO4 = 2409,
        TMT_GRADIENTRATIO5 = 2410,
        TMT_PROGRESSCHUNKSIZE = 2411,
        TMT_PROGRESSSPACESIZE = 2412,
        TMT_SATURATION = 2413,
        TMT_TEXTBORDERSIZE = 2414,
        TMT_ALPHATHRESHOLD = 2415,
        TMT_WIDTH = 2416,
        TMT_HEIGHT = 2417,
        TMT_GLYPHINDEX = 2418,
        TMT_TRUESIZESTRETCHMARK = 2419,
        TMT_MINDPI1 = 2420,
        TMT_MINDPI2 = 2421,
        TMT_MINDPI3 = 2422,
        TMT_MINDPI4 = 2423,
        TMT_MINDPI5 = 2424,

        // Fonts
        TMT_GLYPHFONT = 2602,

        // Strings
        TMT_IMAGEFILE = 3001,
        TMT_IMAGEFILE1 = 3002,
        TMT_IMAGEFILE2 = 3003,
        TMT_IMAGEFILE3 = 3004,
        TMT_IMAGEFILE4 = 3005,
        TMT_IMAGEFILE5 = 3006,
        TMT_GLYPHIMAGEFILE = 3008,
        TMT_TEXT = 3201,

        // Positions / Sizes
        TMT_OFFSET = 3401,
        TMT_TEXTSHADOWOFFSET = 3402,
        TMT_MINSIZE = 3403,
        TMT_MINSIZE1 = 3404,
        TMT_MINSIZE2 = 3405,
        TMT_MINSIZE3 = 3406,
        TMT_MINSIZE4 = 3407,
        TMT_MINSIZE5 = 3408,

        // Margins
        TMT_SIZINGMARGINS = 3601,
        TMT_CONTENTMARGINS = 3602,
        TMT_CAPTIONMARGINS = 3603,

        // Colors
        TMT_BORDERCOLOR = 3801,
        TMT_FILLCOLOR = 3802,
        TMT_TEXTCOLOR = 3803,
        TMT_EDGELIGHTCOLOR = 3804,
        TMT_EDGEHIGHLIGHTCOLOR = 3805,
        TMT_EDGESHADOWCOLOR = 3806,
        TMT_EDGEDKSHADOWCOLOR = 3807,
        TMT_EDGEFILLCOLOR = 3808,
        TMT_TRANSPARENTCOLOR = 3809,
        TMT_GRADIENTCOLOR1 = 3810,
        TMT_GRADIENTCOLOR2 = 3811,
        TMT_GRADIENTCOLOR3 = 3812,
        TMT_GRADIENTCOLOR4 = 3813,
        TMT_GRADIENTCOLOR5 = 3814,
        TMT_SHADOWCOLOR = 3815,
        TMT_GLOWCOLOR = 3816,
        TMT_TEXTBORDERCOLOR = 3817,
        TMT_TEXTSHADOWCOLOR = 3818,
        TMT_GLYPHTEXTCOLOR = 3819,
        TMT_GLYPHTRANSPARENTCOLOR = 3820,
        TMT_FILLCOLORHINT = 3821,
        TMT_BORDERCOLORHINT = 3822,
        TMT_ACCENTCOLORHINT = 3823,

        // Enums
        TMT_BGTYPE = 4001,
        TMT_BORDERTYPE = 4002,
        TMT_FILLTYPE = 4003,
        TMT_SIZINGTYPE = 4004,
        TMT_HALIGN = 4005,
        TMT_CONTENTALIGNMENT = 4006,
        TMT_VALIGN = 4007,
        TMT_OFFSETTYPE = 4008,
        TMT_ICONEFFECT = 4009,
        TMT_TEXTSHADOWTYPE = 4010,
        TMT_IMAGELAYOUT = 4011,
        TMT_GLYPHTYPE = 4012,
        TMT_IMAGESELECTTYPE = 4013,
        TMT_GLYPHFONTSIZINGTYPE = 4014,
        TMT_TRUESIZESCALINGTYPE = 4015,
    }

    [Flags]
    internal enum SET_THEME_APP_PROPERTIES_FLAGS : uint
    {
        ALLOW_NONCLIENT = 0x00000001,
        ALLOW_CONTROLS = 0x00000002,
        ALLOW_WEBCONTENT = 0x00000004,
    }

    [Flags]
    internal enum NMCUSTOMDRAW_DRAW_STATE_FLAGS : uint
    {
        CDIS_SELECTED = 0x0001,
        CDIS_GRAYED = 0x0002,
        CDIS_DISABLED = 0x0004,
        CDIS_CHECKED = 0x0008,
        CDIS_FOCUS = 0x0010,
        CDIS_DEFAULT = 0x0020,
        CDIS_HOT = 0x0040,
        CDIS_MARKED = 0x0080,
        CDIS_INDETERMINATE = 0x0100,
        CDIS_SHOWKEYBOARDCUES = 0x0200,
        CDIS_NEARHOT = 0x0400,
        CDIS_OTHERSIDEHOT = 0x0800,
        CDIS_DROPHILITED = 0x1000,
    }

    [Flags]
    internal enum TVHITTESTINFO_FLAGS : uint
    {
        TVHT_NOWHERE = 0x0001,
        TVHT_ONITEMICON = 0x0002,
        TVHT_ONITEMLABEL = 0x0004,
        TVHT_ONITEMINDENT = 0x0008,
        TVHT_ONITEMBUTTON = 0x0010,
        TVHT_ONITEMRIGHT = 0x0020,
        TVHT_ONITEMSTATEICON = 0x0040,
        TVHT_ABOVE = 0x0100,
        TVHT_BELOW = 0x0200,
        TVHT_TORIGHT = 0x0400,
        TVHT_TOLEFT = 0x0800,
        TVHT_ONITEM = (TVHT_ONITEMICON | TVHT_ONITEMLABEL | TVHT_ONITEMSTATEICON),
    }

    [Flags]
    internal enum LVHITTESTINFO_FLAGS : uint
    {
        LVHT_NOWHERE = 0x00000001,
        LVHT_ONITEMICON = 0x00000002,
        LVHT_ONITEMLABEL = 0x00000004,
        LVHT_ONITEMSTATEICON = 0x00000008,
        LVHT_ONITEM = (LVHT_ONITEMICON | LVHT_ONITEMLABEL | LVHT_ONITEMSTATEICON),
        LVHT_ABOVE = 0x00000008,
        LVHT_BELOW = 0x00000010,
        LVHT_TORIGHT = 0x00000020,
        LVHT_TOLEFT = 0x00000040,
        LVHT_EX_GROUP_HEADER = 0x10000000,
        LVHT_EX_GROUP_FOOTER = 0x20000000,
        LVHT_EX_GROUP_COLLAPSE = 0x40000000,
        LVHT_EX_GROUP_BACKGROUND = 0x80000000,
        LVHT_EX_GROUP_STATEICON = 0x01000000,
        LVHT_EX_GROUP_SUBSETLINK = 0x02000000,
    }

    [Flags]
    internal enum LVGROUP_MASK : uint
    {
        LVGF_NONE = 0x00000000,
        LVGF_HEADER = 0x00000001,
        LVGF_FOOTER = 0x00000002,
        LVGF_STATE = 0x00000004,
        LVGF_ALIGN = 0x00000008,
        LVGF_GROUPID = 0x00000010,
        LVGF_SUBTITLE = 0x00000100,
        LVGF_TASK = 0x00000200,
        LVGF_DESCRIPTIONTOP = 0x00000400,
        LVGF_DESCRIPTIONBOTTOM = 0x00000800,
        LVGF_TITLEIMAGE = 0x00001000,
        LVGF_EXTENDEDIMAGE = 0x00002000,
        LVGF_ITEMS = 0x00004000,
        LVGF_SUBSET = 0x00008000,
        LVGF_SUBSETITEMS = 0x00010000,
    }

    [Flags]
    internal enum LVCOLUMNW_MASK : uint
    {
        LVCF_FMT = 0x0001,
        LVCF_WIDTH = 0x0002,
        LVCF_TEXT = 0x0004,
        LVCF_SUBITEM = 0x0008,
        LVCF_IMAGE = 0x0010,
        LVCF_ORDER = 0x0020,
        LVCF_MINWIDTH = 0x0040,
        LVCF_DEFAULTWIDTH = 0x0080,
        LVCF_IDEALWIDTH = 0x0100,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LVHITTESTINFO
    {
        public Point pt;
        public LVHITTESTINFO_FLAGS flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct LVITEMW
    {
        public LIST_VIEW_ITEM_FLAGS mask;
        public int iItem;
        public int iSubItem;
        public LIST_VIEW_ITEM_STATE_FLAGS state;
        public LIST_VIEW_ITEM_STATE_FLAGS stateMask;
        public PWSTR pszText;
        public int cchTextMax;
        public int iImage;
        public LPARAM lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public uint* puColumns;
        public int* piColFmt;
        public int iGroup;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct COMBOBOXINFO
    {
        public uint cbSize;
        public RECT rcItem;
        public RECT rcButton;
        public uint stateButton;
        public HWND hwndCombo;
        public HWND hwndItem;
        public HWND hwndList;
    }

    internal readonly unsafe partial struct HPROPSHEETPAGE : IHandle<HPROPSHEETPAGE>, IEquatable<HPROPSHEETPAGE>
    {
        public readonly nint Value;
        public HPROPSHEETPAGE(nint value) => Value = value;
        public HPROPSHEETPAGE Handle => this;
        public static explicit operator HPROPSHEETPAGE(nint value) => new(value);
        public static implicit operator nint(HPROPSHEETPAGE value) => value.Value;
        public static explicit operator HPROPSHEETPAGE(void* value) => new((nint)value);
        public static implicit operator void*(HPROPSHEETPAGE value) => (void*)value.Value;
        public static readonly HPROPSHEETPAGE Null = default;
        public bool IsNull => Value == 0;
        public bool Equals(HPROPSHEETPAGE other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is HPROPSHEETPAGE other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(HPROPSHEETPAGE left, HPROPSHEETPAGE right) => left.Value == right.Value;
        public static bool operator !=(HPROPSHEETPAGE left, HPROPSHEETPAGE right) => !left.Equals(right);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IMAGEINFO
    {
        public HBITMAP hbmImage;
        public HBITMAP hbmMask;
        public int Unused1;
        public int Unused2;
        public RECT rcImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TTTOOLINFOW
    {
        public uint cbSize;
        public TOOLTIP_FLAGS uFlags;
        public HWND hwnd;
        public nuint uId;
        public RECT rect;
        public HINSTANCE hinst;
        public PWSTR lpszText;
        public LPARAM lParam;
        public void* lpReserved;
    }

    [Flags]
    internal enum IMAGELIST_CREATION_FLAGS : uint
    {
        ILC_MASK = 0x00000001,
        ILC_COLOR = 0x00000000,
        ILC_COLORDDB = 0x000000FE,
        ILC_COLOR4 = 0x00000004,
        ILC_COLOR8 = 0x00000008,
        ILC_COLOR16 = 0x00000010,
        ILC_COLOR24 = 0x00000018,
        ILC_COLOR32 = 0x00000020,
        ILC_PALETTE = 0x00000800,
        ILC_MIRROR = 0x00002000,
        ILC_PERITEMMIRROR = 0x00004000,
        ILC_ORIGINALSIZE = 0x00010000,
        ILC_HIGHQUALITYSCALE = 0x00020000,
    }

    [Flags]
    internal enum IMAGE_FLAGS : uint
    {
        LR_DEFAULTCOLOR = 0x00000000,
        ILIF_ALPHA = 0x00000001,
        ILIF_LOWQUALITY = 0x00000002,
    }

    internal enum IMAGE_LIST_WRITE_STREAM_FLAGS : uint
    {
        ILP_NORMAL = 0,
        ILP_DOWNLEVEL = 1,
    }

    [Flags]
    internal enum MCHITTESTINFO_HIT_FLAGS : uint
    {
        MCHT_TITLE = 0x00010000,
        MCHT_CALENDAR = 0x00020000,
        MCHT_TODAYLINK = 0x00030000,
        MCHT_CALENDARCONTROL = 0x00100000,
        MCHT_NEXT = 0x01000000,
        MCHT_PREV = 0x02000000,
        MCHT_NOWHERE = 0x00000000,
        MCHT_TITLEBK = MCHT_TITLE,
        MCHT_TITLEMONTH = MCHT_TITLE | 0x0001,
        MCHT_TITLEYEAR = MCHT_TITLE | 0x0002,
        MCHT_TITLEBTNNEXT = MCHT_TITLE | MCHT_NEXT | 0x0003,
        MCHT_TITLEBTNPREV = MCHT_TITLE | MCHT_PREV | 0x0003,
        MCHT_CALENDARBK = MCHT_CALENDAR,
        MCHT_CALENDARDATE = MCHT_CALENDAR | 0x0001,
        MCHT_CALENDARDATENEXT = MCHT_CALENDARDATE | MCHT_NEXT,
        MCHT_CALENDARDATEPREV = MCHT_CALENDARDATE | MCHT_PREV,
        MCHT_CALENDARDAY = MCHT_CALENDAR | 0x0002,
        MCHT_CALENDARWEEKNUM = MCHT_CALENDAR | 0x0003,
        MCHT_CALENDARDATEMIN = MCHT_CALENDAR | 0x0004,
        MCHT_CALENDARDATEMAX = MCHT_CALENDAR | 0x0005,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MCHITTESTINFO
    {
        public uint cbSize;
        public Point pt;
        public MCHITTESTINFO_HIT_FLAGS uHit;
        public SYSTEMTIME st;
        public RECT rc;
        public int iOffset;
        public int iRow;
        public int iCol;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HDITEMW
    {
        public HDI_MASK mask;
        public int cxy;
        public PWSTR pszText;
        public HBITMAP hbm;
        public int cchTextMax;
        public int fmt;
        public LPARAM lParam;
        public int iImage;
        public int iOrder;
        public uint type;
        public void* pvFilter;
        public uint state;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct NMHEADERW
    {
        public NMHDR hdr;
        public int iItem;
        public int iButton;
        public HDITEMW* pitem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMLVLINK
    {
        public NMHDR hdr;
        public LITEM link;
        public int iItem;
        public int iSubItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LITEM
    {
        public uint mask;
        public int iLink;
        public uint state;
        public uint stateMask;
        public fixed char szID[48];
        public fixed char szUrl[2084];
    }

    [Flags]
    internal enum STATIC_STYLES : uint
    {
        SS_LEFT = 0x00000000,
        SS_CENTER = 0x00000001,
        SS_RIGHT = 0x00000002,
        SS_ICON = 0x00000003,
        SS_BLACKRECT = 0x00000004,
        SS_GRAYRECT = 0x00000005,
        SS_WHITERECT = 0x00000006,
        SS_BLACKFRAME = 0x00000007,
        SS_GRAYFRAME = 0x00000008,
        SS_WHITEFRAME = 0x00000009,
        SS_USERITEM = 0x0000000A,
        SS_SIMPLE = 0x0000000B,
        SS_LEFTNOWORDWRAP = 0x0000000C,
        SS_OWNERDRAW = 0x0000000D,
        SS_BITMAP = 0x0000000E,
        SS_ENHMETAFILE = 0x0000000F,
        SS_ETCHEDHORZ = 0x00000010,
        SS_ETCHEDVERT = 0x00000011,
        SS_ETCHEDFRAME = 0x00000012,
        SS_TYPEMASK = 0x0000001F,
        SS_REALSIZECONTROL = 0x00000040,
        SS_NOPREFIX = 0x00000080,
        SS_NOTIFY = 0x00000100,
        SS_CENTERIMAGE = 0x00000200,
        SS_RIGHTJUST = 0x00000400,
        SS_REALSIZEIMAGE = 0x00000800,
        SS_SUNKEN = 0x00001000,
        SS_EDITCONTROL = 0x00002000,
        SS_ENDELLIPSIS = 0x00004000,
        SS_PATHELLIPSIS = 0x00008000,
        SS_WORDELLIPSIS = 0x0000C000,
        SS_ELLIPSISMASK = 0x0000C000,
    }
}
