// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

global using static Interop.SHDocVw;

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Windows.Win32.System.Ole
{
    public enum READYSTATE
    {
        READYSTATE_UNINITIALIZED = 0,
        READYSTATE_LOADING = 1,
        READYSTATE_LOADED = 2,
        READYSTATE_INTERACTIVE = 3,
        READYSTATE_COMPLETE = 4,
    }
}

namespace Windows.Win32.Web.MsHtml
{
    using Windows.Win32.System.Ole;
    using Windows.Win32.System.Variant;

    [Guid("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E")]
    internal unsafe partial struct IWebBrowser2 : IComIID, IVTable<IWebBrowser2, IWebBrowser2.Vtbl>
    {
        private static readonly Guid s_guid = new("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT get_ReadyState(READYSTATE* plReadyState) => HRESULT.E_NOTIMPL;
        public HRESULT get_Document(IDispatch** ppDisp) => HRESULT.E_NOTIMPL;
        public HRESULT get_Silent(VARIANT_BOOL* pbSilent) => HRESULT.E_NOTIMPL;
        public HRESULT put_Silent(VARIANT_BOOL bSilent) => HRESULT.S_OK;
        public HRESULT get_RegisterAsDropTarget(VARIANT_BOOL* pbRegister) => HRESULT.E_NOTIMPL;
        public HRESULT put_RegisterAsDropTarget(VARIANT_BOOL bRegister) => HRESULT.S_OK;
        public HRESULT get_LocationName(BSTR* pbstrLocationName) => HRESULT.E_NOTIMPL;
        public HRESULT get_Busy(VARIANT_BOOL* pbBusy) => HRESULT.E_NOTIMPL;
        public HRESULT get_Offline(VARIANT_BOOL* pbOffline) => HRESULT.E_NOTIMPL;
        public HRESULT get_LocationURL(BSTR* pbstrLocationURL) => HRESULT.E_NOTIMPL;
        public HRESULT GoBack() => HRESULT.S_OK;
        public HRESULT GoForward() => HRESULT.S_OK;
        public HRESULT GoHome() => HRESULT.S_OK;
        public HRESULT GoSearch() => HRESULT.S_OK;
        public HRESULT ExecWB(OLECMDID cmdID, OLECMDEXECOPT cmdexecopt, VARIANT* pvaIn, VARIANT* pvaOut) => HRESULT.S_OK;
        public HRESULT Refresh() => HRESULT.S_OK;
        public HRESULT Refresh2(VARIANT* plevel) => HRESULT.S_OK;
        public HRESULT Stop() => HRESULT.S_OK;
        public HRESULT Navigate2(VARIANT* URL, VARIANT* Flags, VARIANT* TargetFrameName, VARIANT* PostData, VARIANT* Headers) => HRESULT.S_OK;

        public interface Interface { }
    }

    public enum DOCHOSTUIDBLCLK : uint
    {
        DOCHOSTUIDBLCLK_DEFAULT = 0,
        DOCHOSTUIDBLCLK_SHOWPROPERTIES = 1,
        DOCHOSTUIDBLCLK_SHOWCODE = 2,
    }

    [Flags]
    public enum DOCHOSTUIFLAG : uint
    {
        DOCHOSTUIFLAG_DIALOG = 0x00000001,
        DOCHOSTUIFLAG_DISABLE_HELP_MENU = 0x00000002,
        DOCHOSTUIFLAG_NO3DBORDER = 0x00000004,
        DOCHOSTUIFLAG_SCROLL_NO = 0x00000008,
        DOCHOSTUIFLAG_DISABLE_SCRIPT_INACTIVE = 0x00000010,
        DOCHOSTUIFLAG_OPENNEWWIN = 0x00000020,
        DOCHOSTUIFLAG_DISABLE_OFFSCREEN = 0x00000040,
        DOCHOSTUIFLAG_FLAT_SCROLLBAR = 0x00000080,
        DOCHOSTUIFLAG_DIV_BLOCKDEFAULT = 0x00000100,
        DOCHOSTUIFLAG_ACTIVATE_CLIENT_HIT_ONLY = 0x00000200,
        DOCHOSTUIFLAG_OVERRIDE_ADDRESS = 0x00000400,
        DOCHOSTUIFLAG_CODEPAGELINKED = 0x00000800,
        DOCHOSTUIFLAG_URL_ENCODING_DISABLE_UTF8 = 0x00001000,
        DOCHOSTUIFLAG_URL_ENCODING_ENABLE_UTF8 = 0x00002000,
        DOCHOSTUIFLAG_ENABLE_FORMS_AUTOCOMPLETE = 0x00004000,
        DOCHOSTUIFLAG_ENABLE_INTRANET_HOVER = 0x00008000,
        DOCHOSTUIFLAG_THEME = 0x00040000,
        DOCHOSTUIFLAG_NOTHEME = 0x00080000,
        DOCHOSTUIFLAG_NOPICS = 0x00100000,
        DOCHOSTUIFLAG_NO3DOUTERBORDER = 0x00200000,
        DOCHOSTUIFLAG_DISABLE_EDIT_NS_FIXUPS = 0x00400000,
        DOCHOSTUIFLAG_LOCAL_MACHINE_ACCESS_CHECK = 0x00800000,
        DOCHOSTUIFLAG_DISABLE_UNTRUSTEDPROTOCOL = 0x01000000,
        DOCHOSTUIFLAG_HOST_NAVIGATES = 0x02000000,
        DOCHOSTUIFLAG_ENABLE_REDIRECT_NOTIFICATION = 0x04000000,
        DOCHOSTUIFLAG_USE_WINDOWLESS_SELECTCONTROL = 0x08000000,
        DOCHOSTUIFLAG_USE_WINDOWED_SELECTCONTROL = 0x10000000,
        DOCHOSTUIFLAG_ENABLE_ACTIVEX_INACTIVATE_MODE = 0x20000000,
        DOCHOSTUIFLAG_DPI_AWARE = 0x40000000,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct DOCHOSTUIINFO
    {
        public uint cbSize;
        public DOCHOSTUIFLAG dwFlags;
        public DOCHOSTUIDBLCLK dwDoubleClick;
        public PWSTR pchHostCss;
        public PWSTR pchHostNS;
    }
}

namespace Windows.Win32.UI.Shell
{
    internal readonly unsafe partial struct HDROP : IHandle<HDROP>, IEquatable<HDROP>
    {
        public readonly nint Value;
        public HDROP(nint value) => Value = value;
        public HDROP Handle => this;
        public static explicit operator HDROP(nint value) => new(value);
        public static implicit operator nint(HDROP value) => value.Value;
        public static explicit operator HDROP(void* value) => new((nint)value);
        public static implicit operator void*(HDROP value) => (void*)value.Value;
        public static readonly HDROP Null = default;
        public bool IsNull => Value == 0;
        public bool Equals(HDROP other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is HDROP other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(HDROP left, HDROP right) => left.Value == right.Value;
        public static bool operator !=(HDROP left, HDROP right) => !left.Equals(right);
    }

    internal enum NOTIFY_ICON_MESSAGE : uint
    {
        NIM_ADD = 0x00000000,
        NIM_MODIFY = 0x00000001,
        NIM_DELETE = 0x00000002,
        NIM_SETFOCUS = 0x00000003,
        NIM_SETVERSION = 0x00000004,
    }

    [Flags]
    internal enum NOTIFY_ICON_DATA_FLAGS : uint
    {
        NIF_MESSAGE = 0x00000001,
        NIF_ICON = 0x00000002,
        NIF_TIP = 0x00000004,
        NIF_STATE = 0x00000008,
        NIF_INFO = 0x00000010,
        NIF_GUID = 0x00000020,
        NIF_REALTIME = 0x00000040,
        NIF_SHOWTIP = 0x00000080,
    }

    internal enum NOTIFY_ICON_INFOTIP_FLAGS : uint
    {
        NIIF_NONE = 0x00000000,
        NIIF_INFO = 0x00000001,
        NIIF_WARNING = 0x00000002,
        NIIF_ERROR = 0x00000003,
        NIIF_USER = 0x00000004,
        NIIF_NOSOUND = 0x00000010,
        NIIF_LARGE_ICON = 0x00000020,
        NIIF_RESPECT_QUIET_TIME = 0x00000080,
    }
}

namespace Windows.Win32.UI.Controls.Dialogs
{
    [Flags]
    internal enum CHOOSECOLOR_FLAGS : uint
    {
        CC_RGBINIT = 0x00000001,
        CC_FULLOPEN = 0x00000002,
        CC_PREVENTFULLOPEN = 0x00000004,
        CC_SHOWHELP = 0x00000008,
        CC_ENABLEHOOK = 0x00000010,
        CC_ENABLETEMPLATE = 0x00000020,
        CC_ENABLETEMPLATEHANDLE = 0x00000040,
        CC_SOLIDCOLOR = 0x00000080,
        CC_ANYCOLOR = 0x00010000,
    }

    [Flags]
    internal enum OPEN_FILENAME_FLAGS_EX : uint
    {
        OFN_EX_NONE = 0x00000000,
        OFN_EX_NOPLACESBAR = 0x00000001,
    }
}

namespace Windows.Win32.UI.HiDpi
{
    internal enum DPI_AWARENESS
    {
        DPI_AWARENESS_INVALID = -1,
        DPI_AWARENESS_UNAWARE = 0,
        DPI_AWARENESS_SYSTEM_AWARE = 1,
        DPI_AWARENESS_PER_MONITOR_AWARE = 2,
    }
}

namespace Windows.Win32.UI.Shell.Common
{
    // Placeholder to satisfy GlobalUsings
}

internal partial class Interop
{
    internal static partial class SHDocVw
    {
        public enum CommandStateChangeConstants
        {
            CSC_UPDATECOMMANDS = -1,
            CSC_NAVIGATEFORWARD = 1,
            CSC_NAVIGATEBACK = 2,
        }

        public enum CSC
        {
            UPDATECOMMANDS = -1,
            NAVIGATEFORWARD = 1,
            NAVIGATEBACK = 2,
        }
    }
}
