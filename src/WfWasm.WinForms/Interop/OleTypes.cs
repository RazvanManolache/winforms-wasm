// Fork-owned: Common OLE structs and enums for Windows.Win32.System.Ole.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Windows.Win32.System.SystemServices
{
    [Flags]
    internal enum MODIFIERKEYS_FLAGS : uint
    {
        MK_LBUTTON = 0x0001,
        MK_RBUTTON = 0x0002,
        MK_SHIFT = 0x0004,
        MK_CONTROL = 0x0008,
        MK_MBUTTON = 0x0010,
        MK_XBUTTON1 = 0x0020,
        MK_XBUTTON2 = 0x0040
    }
}

namespace Windows.Win32.System.Ole
{
    internal unsafe struct OLEINPLACEFRAMEINFO
    {
        public uint cb;
        public BOOL fMDIApp;
        public HWND hwndFrame;
        public HACCEL haccel;
        public uint cAccelEntries;
    }

    internal unsafe struct OLEMENUGROUPWIDTHS
    {
        public fixed int width[6];
    }

    [Flags]
    internal enum OLEMISC : uint
    {
        OLEMISC_RECOMPOSEONRESIZE = 1,
        OLEMISC_ONLYICONIC = 2,
        OLEMISC_INSERTNOTREPLACE = 4,
        OLEMISC_STATIC = 8,
        OLEMISC_CANTLINKINSIDE = 16,
        OLEMISC_CANLINKBYOLE1 = 32,
        OLEMISC_ISLINKOBJECT = 64,
        OLEMISC_INSIDEOUT = 128,
        OLEMISC_ACTIVATEWHENVISIBLE = 256,
        OLEMISC_RENDERINGISDEVICEINDEPENDENT = 512,
        OLEMISC_INVISIBLEATRUNTIME = 1024,
        OLEMISC_ALWAYSRUN = 2048,
        OLEMISC_ACTSLIKEBUTTON = 4096,
        OLEMISC_ACTSLIKELABEL = 8192,
        OLEMISC_NOUIACTIVATE = 16384,
        OLEMISC_ALIGNABLE = 32768,
        OLEMISC_SIMPLEFRAME = 65536,
        OLEMISC_SETCLIENTSITEFIRST = 131072
    }

    [Flags]
    internal enum DROPEFFECT : uint
    {
        DROPEFFECT_NONE = 0,
        DROPEFFECT_COPY = 1,
        DROPEFFECT_MOVE = 2,
        DROPEFFECT_LINK = 4,
        DROPEFFECT_SCROLL = 0x80000000
    }

    [Flags]
    internal enum KEYMODIFIERS : uint
    {
        KEYMOD_NONE = 0x0000,
        KEYMOD_SHIFT = 0x0001,
        KEYMOD_CONTROL = 0x0002,
        KEYMOD_ALT = 0x0004
    }

    [Flags]
    public enum CTRLINFO : uint
    {
        CTRLINFO_EATS_RETURN = 1,
        CTRLINFO_EATS_ESCAPE = 2,
    }

    internal unsafe struct CONTROLINFO
    {
        public uint cb;
        public HACCEL hAccel;
        public ushort cAccel;
        public CTRLINFO dwFlags;
    }

    [Flags]
    public enum QACONTAINERFLAGS : uint
    {
        QACONTAINER_SHOWHATCHING = 0x00000001,
        QACONTAINER_SHOWGRABHANDLES = 0x00000002,
        QACONTAINER_USERMODE = 0x00000004,
        QACONTAINER_DISPLAYASDEFAULT = 0x00000008,
        QACONTAINER_UIDEAD = 0x00000010,
        QACONTAINER_AUTOCLIP = 0x00000020,
        QACONTAINER_MESSAGEREFLECT = 0x00000040,
        QACONTAINER_SUPPORTSMNEMONICS = 0x00000080,
    }

    internal unsafe struct QACONTAINER
    {
        public uint cbSize;
        public IOleClientSite* pClientSite;
        public IAdviseSink* pAdviseSink;
        public IPropertyNotifySink* pPropertyNotifySink;
        public IUnknown* pUnkEventSink;
        public QACONTAINERFLAGS dwAmbientFlags;
        public COLORREF colorFore;
        public COLORREF colorBack;
        public IFont* pFont;
        public void* pUndoMgr;
        public uint dwAppearance;
        public int lcid;
        public HPALETTE hpal;
        public void* pBindHost;
        public void* pOleControlSite;
        public void* pServiceProvider;
    }

    internal unsafe struct QACONTROL
    {
        public uint cbSize;
        public OLEMISC dwMiscStatus;
        public uint dwViewStatus;
        public uint dwEventQuirks;
        public uint dwEventCookie;
        public uint dwPropNotifyCookie;
        public uint dwPointerActivationPolicy;
    }

    internal unsafe struct LOGPALETTE
    {
        public ushort palVersion;
        public ushort palNumEntries;
    }

    [Flags]
    internal enum ENUM_CONTROLS_WHICH_FLAGS : uint
    {
        GC_WCH_SIBLING = 0x00000001,
        GCW_WCH_SIBLING = 0x00000001,
        GC_WCH_CONTAINER = 0x00000002,
        GC_WCH_CONTAINED = 0x00000003,
        GC_WCH_ALL = 0x00000004,
        GC_WCH_FREVERSEDIR = 0x08000000,
        GC_WCH_FONLYAFTER = 0x10000000,
        GC_WCH_FONLYBEFORE = 0x20000000,
        GC_WCH_FSELECTED = 0x40000000,
    }

    internal unsafe partial struct IEnumOLEVERB : IComIID, IVTable<IEnumOLEVERB, IEnumOLEVERB.Vtbl>
    {
        private readonly void** _vtable;
        public unsafe struct Vtbl { }
        private static readonly Guid s_guid = new("00000104-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface { }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct OLEVERB
    {
        public OLEIVERB lVerb;
        public PWSTR lpszVerbName;
        public uint fuFlags;
        public uint grfAttribs;
    }

    internal enum OLEIVERB
    {
        OLEIVERB_PRIMARY = 0,
        OLEIVERB_SHOW = -1,
        OLEIVERB_OPEN = -2,
        OLEIVERB_HIDE = -3,
        OLEIVERB_UIACTIVATE = -4,
        OLEIVERB_INPLACEACTIVATE = -5,
        OLEIVERB_DISCARDUNDOSTATE = -6,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct CALPOLESTR
    {
        public uint cElems;
        public PWSTR* pElems;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe partial struct CADWORD
    {
        public uint cElems;
        public uint* pElems;
    }

    [Guid("376BD3E0-4248-101A-8BBA-00AA00300CAB")]
    internal unsafe partial struct IPerPropertyBrowsing : IComIID, IVTable<IPerPropertyBrowsing, IPerPropertyBrowsing.Vtbl>
    {
        private static readonly Guid s_guid = new("376BD3E0-4248-101A-8BBA-00AA00300CAB");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetDisplayString(int dispID, BSTR* pBstr) => HRESULT.E_NOTIMPL;
        public HRESULT MapPropertyToPage(int dispID, Guid* pClsid) => HRESULT.E_NOTIMPL;
        public HRESULT GetPredefinedStrings(int dispID, CALPOLESTR* pCaStringsOut, CADWORD* pCaCookiesOut) => HRESULT.E_NOTIMPL;
        public HRESULT GetPredefinedValue(int dispID, uint dwCookie, VARIANT* pVarOut) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    internal readonly partial struct OLE_HANDLE : IEquatable<OLE_HANDLE>
    {
        public readonly uint Value;
        public OLE_HANDLE(uint value) => Value = value;
        public static explicit operator OLE_HANDLE(uint value) => new(value);
        public static implicit operator uint(OLE_HANDLE value) => value.Value;
        public bool Equals(OLE_HANDLE other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is OLE_HANDLE other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public static bool operator ==(OLE_HANDLE left, OLE_HANDLE right) => left.Value == right.Value;
        public static bool operator !=(OLE_HANDLE left, OLE_HANDLE right) => left.Value != right.Value;
    }

    [Guid("9BFBBC02-EFF1-101A-84ED-00AA00341D07")]
    internal unsafe partial struct IPropertyNotifySink : IComIID, IVTable<IPropertyNotifySink, IPropertyNotifySink.Vtbl>
    {
        private static readonly Guid s_guid = new("9BFBBC02-EFF1-101A-84ED-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT OnChanged(int dispID);
            HRESULT OnRequestEdit(int dispID);
        }
    }

    internal enum OLECMDEXECOPT : uint
    {
        OLECMDEXECOPT_DODEFAULT = 0,
        OLECMDEXECOPT_PROMPTUSER = 1,
        OLECMDEXECOPT_DONTPROMPTUSER = 2,
        OLECMDEXECOPT_SHOWHELP = 3,
    }

    internal enum OLECMDID : uint
    {
        OLECMDID_OPEN = 1,
        OLECMDID_NEW = 2,
        OLECMDID_SAVE = 3,
        OLECMDID_SAVEAS = 4,
        OLECMDID_SAVECOPYAS = 5,
        OLECMDID_PRINT = 6,
        OLECMDID_PRINTPREVIEW = 7,
        OLECMDID_PAGESETUP = 8,
        OLECMDID_SPELL = 9,
        OLECMDID_PROPERTIES = 10,
        OLECMDID_CUT = 11,
        OLECMDID_COPY = 12,
        OLECMDID_PASTE = 13,
        OLECMDID_PASTESPECIAL = 14,
        OLECMDID_UNDO = 15,
        OLECMDID_REDO = 16,
        OLECMDID_SELECTALL = 17,
        OLECMDID_CLEARSELECTION = 18,
        OLECMDID_ZOOM = 19,
        OLECMDID_GETZOOMRANGE = 20,
        OLECMDID_UPDATECOMMANDS = 21,
        OLECMDID_REFRESH = 22,
        OLECMDID_STOP = 23,
        OLECMDID_HIDETOOLBARS = 24,
        OLECMDID_SETPROGRESSMAX = 25,
        OLECMDID_SETPROGRESSPOS = 26,
        OLECMDID_SETPROGRESSTEXT = 27,
        OLECMDID_SETTITLE = 28,
        OLECMDID_SETDOWNLOADSTATE = 29,
        OLECMDID_STOPDOWNLOAD = 30,
        OLECMDID_ONTOOLBARACTIVATED = 31,
        OLECMDID_FIND = 32,
        OLECMDID_DELETE = 33,
        OLECMDID_HTTPEQUIV = 34,
        OLECMDID_HTTPEQUIV_DONE = 35,
        OLECMDID_ENABLE_INTERACTION = 36,
        OLECMDID_ONUNLOAD = 37,
        OLECMDID_PROPERTYBAG2 = 38,
        OLECMDID_PREREFRESH = 39,
        OLECMDID_SHOWSCRIPTERROR = 40,
        OLECMDID_SHOWMESSAGE = 41,
        OLECMDID_SHOWFIND = 42,
        OLECMDID_SHOWPAGESETUP = 43,
        OLECMDID_SHOWPRINT = 44,
        OLECMDID_CLOSE = 45,
        OLECMDID_ALLOWUILESSSAVEAS = 46,
        OLECMDID_DONTDOWNLOADCSS = 47,
        OLECMDID_UPDATE_ALL_COMMANDS = 48,
        OLECMDID_ACTIVEVIEWCHANGED = 49,
        OLECMDID_GETPRINTTEMPLATE = 50,
        OLECMDID_SETPRINTTEMPLATE = 51,
        OLECMDID_PRINTHIDEWINDOW = 52,
    }
}

namespace Windows.Win32.System.Com
{
    [Guid("00000101-0000-0000-C000-000000000046")]
    internal unsafe partial struct IEnumString : IComIID, IVTable<IEnumString, IEnumString.Vtbl>
    {
        private static readonly Guid s_guid = new("00000101-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT Next(uint celt, PWSTR* rgelt, uint* pceltFetched) => HRESULT.E_NOTIMPL;
        public HRESULT Skip(uint celt) => HRESULT.E_NOTIMPL;
        public HRESULT Reset() => HRESULT.S_OK;
        public HRESULT Clone(IEnumString** ppenum) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }
}
