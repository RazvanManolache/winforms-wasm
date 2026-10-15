// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32.UI.Controls;

internal readonly unsafe partial struct HTHEME : IHandle<HTHEME>, IEquatable<HTHEME>
{
    public readonly nint Value;
    public HTHEME(nint value) => Value = value;
    public HTHEME Handle => this;
    public static explicit operator HTHEME(nint value) => new(value);
    public static implicit operator nint(HTHEME value) => value.Value;
    public static explicit operator HTHEME(void* value) => new((nint)value);
    public static implicit operator void*(HTHEME value) => (void*)value.Value;
    public static readonly HTHEME Null = default;
    public bool IsNull => Value == 0;
    public bool Equals(HTHEME other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is HTHEME other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(HTHEME left, HTHEME right) => left.Value == right.Value;
    public static bool operator !=(HTHEME left, HTHEME right) => !left.Equals(right);
}

[Flags]
internal enum TASKDIALOG_FLAGS : uint
{
    TDF_ENABLE_HYPERLINKS = 0x0001,
    TDF_USE_HICON_MAIN = 0x0002,
    TDF_USE_HICON_FOOTER = 0x0004,
    TDF_ALLOW_DIALOG_CANCELLATION = 0x0008,
    TDF_USE_COMMAND_LINKS = 0x0010,
    TDF_USE_COMMAND_LINKS_NO_ICON = 0x0020,
    TDF_EXPAND_FOOTER_AREA = 0x0040,
    TDF_EXPANDED_BY_DEFAULT = 0x0080,
    TDF_VERIFICATION_FLAG_CHECKED = 0x0100,
    TDF_SHOW_PROGRESS_BAR = 0x0200,
    TDF_SHOW_MARQUEE_PROGRESS_BAR = 0x0400,
    TDF_CALLBACK_TIMER = 0x0800,
    TDF_POSITION_RELATIVE_TO_WINDOW = 0x1000,
    TDF_RTL_LAYOUT = 0x2000,
    TDF_NO_DEFAULT_RADIO_BUTTON = 0x4000,
    TDF_CAN_BE_MINIMIZED = 0x8000,
    TDF_NO_SET_FOREGROUND = 0x00010000,
    TDF_SIZE_TO_CONTENT = 0x01000000,
}

[Flags]
internal enum TASKDIALOG_COMMON_BUTTON_FLAGS : uint
{
    TDCBF_OK_BUTTON = 0x0001,
    TDCBF_YES_BUTTON = 0x0002,
    TDCBF_NO_BUTTON = 0x0004,
    TDCBF_CANCEL_BUTTON = 0x0008,
    TDCBF_RETRY_BUTTON = 0x0010,
    TDCBF_CLOSE_BUTTON = 0x0020,
    TDCBF_ABORT_BUTTON = 0x00010000,
    TDCBF_IGNORE_BUTTON = 0x00020000,
    TDCBF_TRYAGAIN_BUTTON = 0x00040000,
    TDCBF_CONTINUE_BUTTON = 0x00080000,
    TDCBF_HELP_BUTTON = 0x00100000,
}

internal enum TASKDIALOG_NOTIFICATIONS : uint
{
    TDN_CREATED = 0,
    TDN_NAVIGATED = 1,
    TDN_BUTTON_CLICKED = 2,
    TDN_HYPERLINK_CLICKED = 3,
    TDN_TIMER = 4,
    TDN_DESTROYED = 5,
    TDN_RADIO_BUTTON_CLICKED = 6,
    TDN_CONSTRUCTED = 7,
    TDN_VERIFICATION_CLICKED = 8,
    TDN_HELP = 9,
    TDN_EXPANDO_BUTTON_CLICKED = 10,
}

internal enum TASKDIALOG_MESSAGES : uint
{
    TDM_NAVIGATE_PAGE = 0x0465,
    TDM_CLICK_BUTTON = 0x0466,
    TDM_SET_MARQUEE_PROGRESS_BAR = 0x0467,
    TDM_SET_PROGRESS_BAR_STATE = 0x0468,
    TDM_SET_PROGRESS_BAR_RANGE = 0x0469,
    TDM_SET_PROGRESS_BAR_POS = 0x046A,
    TDM_SET_PROGRESS_BAR_MARQUEE = 0x046B,
    TDM_SET_ELEMENT_TEXT = 0x046C,
    TDM_CLICK_RADIO_BUTTON = 0x046E,
    TDM_ENABLE_BUTTON = 0x046F,
    TDM_ENABLE_RADIO_BUTTON = 0x0470,
    TDM_CLICK_VERIFICATION = 0x0471,
    TDM_UPDATE_ELEMENT_TEXT = 0x0472,
    TDM_SET_BUTTON_ELEVATION_REQUIRED_STATE = 0x0473,
    TDM_UPDATE_ICON = 0x0474,
}

internal enum TASKDIALOG_ELEMENTS : uint
{
    TDE_CONTENT = 0,
    TDE_EXPANDED_INFORMATION = 1,
    TDE_FOOTER = 2,
    TDE_MAIN_INSTRUCTION = 3,
}

internal enum TASKDIALOG_ICON_ELEMENTS : uint
{
    TDIE_ICON_MAIN = 0,
    TDIE_ICON_FOOTER = 1,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TASKDIALOG_BUTTON
{
    public int nButtonID;
    public PCWSTR pszButtonText;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe partial struct TASKDIALOGCONFIG
{
    public uint cbSize;
    public HWND hwndParent;
    public HINSTANCE hInstance;
    public TASKDIALOG_FLAGS dwFlags;
    public TASKDIALOG_COMMON_BUTTON_FLAGS dwCommonButtons;
    public PCWSTR pszWindowTitle;
    public _Anonymous1_e__Union Anonymous1;
    public PCWSTR pszMainInstruction;
    public PCWSTR pszContent;
    public uint cButtons;
    public TASKDIALOG_BUTTON* pButtons;
    public int nDefaultButton;
    public uint cRadioButtons;
    public TASKDIALOG_BUTTON* pRadioButtons;
    public int nDefaultRadioButton;
    public PCWSTR pszVerificationText;
    public PCWSTR pszExpandedInformation;
    public PCWSTR pszExpandedControlText;
    public PCWSTR pszCollapsedControlText;
    public _Anonymous2_e__Union Anonymous2;
    public PCWSTR pszFooter;
    public delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, nint, HRESULT> pfCallback;
    public nint lpCallbackData;
    public uint cxWidth;

    [StructLayout(LayoutKind.Explicit)]
    public struct _Anonymous1_e__Union
    {
        [FieldOffset(0)] public HICON hMainIcon;
        [FieldOffset(0)] public PCWSTR pszMainIcon;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct _Anonymous2_e__Union
    {
        [FieldOffset(0)] public HICON hFooterIcon;
        [FieldOffset(0)] public PCWSTR pszFooterIcon;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CHARRANGE
{
    public int cpMin;
    public int cpMax;
}

[Flags]
internal enum LIST_VIEW_ITEM_STATE_FLAGS : uint
{
    LVIS_FOCUSED = 0x0001,
    LVIS_SELECTED = 0x0002,
    LVIS_CUT = 0x0004,
    LVIS_DROPHILITED = 0x0008,
    LVIS_GLOW = 0x0010,
    LVIS_ACTIVATING = 0x0020,
    LVIS_OVERLAYMASK = 0x0F00,
    LVIS_STATEIMAGEMASK = 0xF000,
}

[Flags]
internal enum TOOLTIP_FLAGS : uint
{
    TTF_IDISHWND = 0x0001,
    TTF_CENTERTIP = 0x0002,
    TTF_RTLREADING = 0x0004,
    TTF_SUBCLASS = 0x0010,
    TTF_TRACK = 0x0020,
    TTF_ABSOLUTE = 0x0080,
    TTF_TRANSPARENT = 0x0100,
    TTF_PARSELINKS = 0x1000,
    TTF_DI_SETITEM = 0x8000,
}

[Flags]
internal enum TVITEM_MASK : uint
{
    TVIF_TEXT = 0x0001,
    TVIF_IMAGE = 0x0002,
    TVIF_PARAM = 0x0004,
    TVIF_STATE = 0x0008,
    TVIF_HANDLE = 0x0010,
    TVIF_SELECTEDIMAGE = 0x0020,
    TVIF_CHILDREN = 0x0040,
    TVIF_INTEGRAL = 0x0080,
    TVIF_STATEEX = 0x0100,
    TVIF_EXPANDEDIMAGE = 0x0200,
}

[Flags]
internal enum TREE_VIEW_ITEM_STATE_FLAGS : uint
{
    TVIS_SELECTED = 0x0002,
    TVIS_CUT = 0x0004,
    TVIS_DROPHILITED = 0x0008,
    TVIS_BOLD = 0x0010,
    TVIS_EXPANDED = 0x0020,
    TVIS_EXPANDEDONCE = 0x0040,
    TVIS_EXPANDPARTIAL = 0x0080,
    TVIS_OVERLAYMASK = 0x0F00,
    TVIS_STATEIMAGEMASK = 0xF000,
    TVIS_USERMASK = 0xF000,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TVITEMW
{
    public TVITEM_MASK mask;
    public HTREEITEM hItem;
    public TREE_VIEW_ITEM_STATE_FLAGS state;
    public TREE_VIEW_ITEM_STATE_FLAGS stateMask;
    public PWSTR pszText;
    public int cchTextMax;
    public int iImage;
    public int iSelectedImage;
    public int cChildren;
    public LPARAM lParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NMTREEVIEWW
{
    public NMHDR hdr;
    public NM_TREEVIEW_ACTION action;
    public TVITEMW itemOld;
    public TVITEMW itemNew;
    public Point ptDrag;
}

internal partial struct TVINSERTSTRUCTW
{
    public HTREEITEM hParent;
    public HTREEITEM hInsertAfter;
    public _Anonymous_e__Union Anonymous;

    [StructLayout(LayoutKind.Explicit)]
    public struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public TVITEMW item;
    }
}
