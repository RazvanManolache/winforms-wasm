// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;

namespace Windows.Win32.UI.Shell
{
    [Flags]
    internal enum FILEOPENDIALOGOPTIONS : uint
    {
        FOS_OVERWRITEPROMPT = 0x00000002,
        FOS_STRICTFILETYPES = 0x00000004,
        FOS_NOCHANGEDIR = 0x00000008,
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_ALLNONSTORAGEITEMS = 0x00000080,
        FOS_NOVALIDATE = 0x00000100,
        FOS_ALLOWMULTISELECT = 0x00000200,
        FOS_PATHMUSTEXIST = 0x00000800,
        FOS_FILEMUSTEXIST = 0x00001000,
        FOS_CREATEPROMPT = 0x00002000,
        FOS_SHAREAWARE = 0x00004000,
        FOS_NOREADONLYRETURN = 0x00008000,
        FOS_NOTESTFILECREATE = 0x00010000,
        FOS_HIDEMRUPLACES = 0x00020000,
        FOS_HIDEPINNEDPLACES = 0x00040000,
        FOS_NODEREFERENCELINKS = 0x00100000,
        FOS_OKBUTTONNEEDSINTERACTION = 0x00200000,
        FOS_DONTADDTORECENT = 0x02000000,
        FOS_FORCESHOWHIDDEN = 0x10000000,
        FOS_DEFAULTNOMINIMODE = 0x20000000,
        FOS_FORCEPREVIEWPANEON = 0x40000000,
        FOS_SUPPORTSTREAMABLEITEMS = 0x80000000,
    }

    internal enum DROPIMAGETYPE
    {
        DROPIMAGE_INVALID = -1,
        DROPIMAGE_NONE = 0,
        DROPIMAGE_COPY = 1,
        DROPIMAGE_MOVE = 2,
        DROPIMAGE_LINK = 4,
        DROPIMAGE_LABEL = 6,
        DROPIMAGE_WARNING = 7,
        DROPIMAGE_NOIMAGE = 8,
    }

    internal enum SIGDN : uint
    {
        SIGDN_NORMALDISPLAY = 0x00000000,
        SIGDN_PARENTRELATIVEPARSING = 0x80018001,
        SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,
        SIGDN_PARENTRELATIVEEDITING = 0x80031001,
        SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,
        SIGDN_FILESYSPATH = 0x80058000,
        SIGDN_URL = 0x80068000,
        SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007c001,
        SIGDN_PARENTRELATIVE = 0x80080001,
        SIGDN_PARENTRELATIVEFORUI = 0x80094001,
    }

    [Flags]
    internal enum SHELL_AUTOCOMPLETE_FLAGS : uint
    {
        SHACF_DEFAULT = 0x00000000,
        SHACF_FILESYSTEM = 0x00000001,
        SHACF_URLHISTORY = 0x00000002,
        SHACF_URLMRU = 0x00000004,
        SHACF_USETAB = 0x00000008,
        SHACF_FILESYS_ONLY = 0x00000010,
        SHACF_FILESYS_DIRS = 0x00000020,
        SHACF_VISTA_SHAUTOCOMPLETE = 0x00000040,
        SHACF_AUTOSUGGEST_FORCE_ON = 0x10000000,
        SHACF_AUTOSUGGEST_FORCE_OFF = 0x20000000,
        SHACF_AUTOAPPEND_FORCE_ON = 0x40000000,
        SHACF_AUTOAPPEND_FORCE_OFF = 0x80000000,
    }

    internal enum FDE_OVERWRITE_RESPONSE
    {
        FDEOR_DEFAULT = 0x00000000,
        FDEOR_ACCEPT = 0x00000001,
        FDEOR_REFUSE = 0x00000002,
    }

    internal enum FDE_SHAREVIOLATION_RESPONSE
    {
        FDESVR_DEFAULT = 0x00000000,
        FDESVR_ACCEPT = 0x00000001,
        FDESVR_REFUSE = 0x00000002,
    }

    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    internal unsafe partial struct IShellItem : IComIID, IVTable<IShellItem, IShellItem.Vtbl>
    {
        private static readonly Guid s_guid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetDisplayName(SIGDN sigdnName, out PWSTR ppszName)
        {
            ppszName = default;
            return HRESULT.E_NOTIMPL;
        }

        public interface Interface { }
    }

    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    internal unsafe partial struct IShellItemArray : IComIID, IVTable<IShellItemArray, IShellItemArray.Vtbl>
    {
        private static readonly Guid s_guid = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetCount(uint* pdwNumItems) => HRESULT.E_NOTIMPL;
        public HRESULT GetCount(out uint pdwNumItems) { pdwNumItems = 0; return HRESULT.E_NOTIMPL; }
        public HRESULT GetItemAt(uint dwIndex, IShellItem** ppsi) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    internal unsafe partial struct IFileDialog : IComIID, IVTable<IFileDialog, IFileDialog.Vtbl>
    {
        private static readonly Guid s_guid = new("42F85136-DB7E-439C-85F1-E4075D135FC8");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT Show(HWND hwndOwner) => HRESULT.E_NOTIMPL;
        public HRESULT SetFileTypes(uint cFileTypes, void* rgFilterSpec) => HRESULT.S_OK;
        public HRESULT SetFileTypeIndex(uint iFileType) => HRESULT.S_OK;
        public HRESULT GetFileTypeIndex(out uint piFileType) { piFileType = 0; return HRESULT.S_OK; }
        public HRESULT Advise(IFileDialogEvents* pfde, uint* pdwCookie) => HRESULT.S_OK;
        public HRESULT Advise(IFileDialogEvents* pfde, out uint pdwCookie) { pdwCookie = 0; return HRESULT.S_OK; }
        public HRESULT Unadvise(uint dwCookie) => HRESULT.S_OK;
        public HRESULT SetOptions(FILEOPENDIALOGOPTIONS internalFos) => HRESULT.S_OK;
        public HRESULT GetOptions(FILEOPENDIALOGOPTIONS* pfos) => HRESULT.S_OK;
        public HRESULT SetDefaultFolder(IShellItem* psi) => HRESULT.S_OK;
        public HRESULT SetFolder(IShellItem* psi) => HRESULT.S_OK;
        public HRESULT GetFolder(IShellItem** ppsi) => HRESULT.E_NOTIMPL;
        public HRESULT GetCurrentSelection(IShellItem** ppsi) => HRESULT.E_NOTIMPL;
        public HRESULT SetFileName(PCWSTR pszName) => HRESULT.S_OK;
        public HRESULT SetFileName(string? pszName) => HRESULT.S_OK;
        public HRESULT GetFileName(PWSTR* pszName) => HRESULT.E_NOTIMPL;
        public HRESULT SetTitle(PCWSTR pszTitle) => HRESULT.S_OK;
        public HRESULT SetTitle(string? pszTitle) => HRESULT.S_OK;
        public HRESULT SetOkButtonLabel(PCWSTR pszText) => HRESULT.S_OK;
        public HRESULT SetFileNameLabel(PCWSTR pszLabel) => HRESULT.S_OK;
        public HRESULT GetResult(IShellItem** ppsi) => HRESULT.E_NOTIMPL;
        public HRESULT AddPlace(IShellItem* psi, int fdap) => HRESULT.S_OK;
        public HRESULT SetDefaultExtension(PCWSTR pszDefaultExtension) => HRESULT.S_OK;
        public HRESULT SetDefaultExtension(string? pszDefaultExtension) => HRESULT.S_OK;
        public HRESULT Close(HRESULT hr) => HRESULT.S_OK;
        public HRESULT SetClientGuid(in Guid guid) => HRESULT.S_OK;
        public HRESULT ClearClientData() => HRESULT.S_OK;
        public HRESULT SetFilter(void* pFilter) => HRESULT.S_OK;
        public HRESULT GetResults(IShellItemArray** ppenum) => HRESULT.E_NOTIMPL;
        public uint Release() => 0;
        public HRESULT QueryInterface(in Guid riid, void* ppvObject) => HRESULT.E_NOINTERFACE;

        public interface Interface { }
    }

    [Guid("D57C72D7-A4D0-4D0E-B740-2D61CE01F439")]
    internal unsafe partial struct IFileOpenDialog : IComIID, IVTable<IFileOpenDialog, IFileOpenDialog.Vtbl>
    {
        private static readonly Guid s_guid = new("D57C72D7-A4D0-4D0E-B740-2D61CE01F439");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
        public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
        public uint AddRef() => 1;
        public uint Release() => 1;

        public HRESULT GetResults(IShellItemArray** ppenum) => HRESULT.E_NOTIMPL;
        public HRESULT GetSelectedItems(IShellItemArray** ppsai) => HRESULT.E_NOTIMPL;
        public HRESULT Show(HWND hwndOwner) => HRESULT.E_NOTIMPL;
        public HRESULT SetFileTypes(uint cFileTypes, void* rgFilterSpec) => HRESULT.S_OK;
        public HRESULT SetFileTypeIndex(uint iFileType) => HRESULT.S_OK;
        public HRESULT GetFileTypeIndex(out uint piFileType) { piFileType = 0; return HRESULT.S_OK; }
        public HRESULT Advise(IFileDialogEvents* pfde, uint* pdwCookie) => HRESULT.S_OK;
        public HRESULT Advise(IFileDialogEvents* pfde, out uint pdwCookie) { pdwCookie = 0; return HRESULT.S_OK; }
        public HRESULT Unadvise(uint dwCookie) => HRESULT.S_OK;
        public HRESULT SetOptions(FILEOPENDIALOGOPTIONS internalFos) => HRESULT.S_OK;
        public HRESULT GetOptions(FILEOPENDIALOGOPTIONS* pfos) => HRESULT.S_OK;
        public HRESULT SetDefaultFolder(IShellItem* psi) => HRESULT.S_OK;
        public HRESULT SetFolder(IShellItem* psi) => HRESULT.S_OK;
        public HRESULT GetFolder(IShellItem** ppsi) => HRESULT.E_NOTIMPL;
        public HRESULT GetCurrentSelection(IShellItem** ppsi) => HRESULT.E_NOTIMPL;
        public HRESULT SetFileName(PCWSTR pszName) => HRESULT.S_OK;
        public HRESULT SetFileName(string? pszName) => HRESULT.S_OK;
        public HRESULT GetFileName(PWSTR* pszName) => HRESULT.E_NOTIMPL;
        public HRESULT SetTitle(PCWSTR pszTitle) => HRESULT.S_OK;
        public HRESULT SetTitle(string? pszTitle) => HRESULT.S_OK;
        public HRESULT SetOkButtonLabel(PCWSTR pszText) => HRESULT.S_OK;
        public HRESULT SetFileNameLabel(PCWSTR pszLabel) => HRESULT.S_OK;
        public HRESULT GetResult(IShellItem** ppsi) => HRESULT.E_NOTIMPL;
        public HRESULT AddPlace(IShellItem* psi, int fdap) => HRESULT.S_OK;
        public HRESULT SetDefaultExtension(PCWSTR pszDefaultExtension) => HRESULT.S_OK;
        public HRESULT SetDefaultExtension(string? pszDefaultExtension) => HRESULT.S_OK;
        public HRESULT Close(HRESULT hr) => HRESULT.S_OK;
        public HRESULT SetClientGuid(in Guid guid) => HRESULT.S_OK;
        public HRESULT ClearClientData() => HRESULT.S_OK;
        public HRESULT SetFilter(void* pFilter) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("84FCC79E-DB73-4AB6-9B5D-5B640A6472F4")]
    internal unsafe partial struct IFileSaveDialog : IComIID, IVTable<IFileSaveDialog, IFileSaveDialog.Vtbl>
    {
        private static readonly Guid s_guid = new("84FCC79E-DB73-4AB6-9B5D-5B640A6472F4");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT SetSaveAsItem(IShellItem* psi) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("E6FDD21A-163F-4975-9C8C-A69F1BA37034")]
    internal unsafe partial struct IFileDialogCustomize : IComIID, IVTable<IFileDialogCustomize, IFileDialogCustomize.Vtbl>
    {
        private static readonly Guid s_guid = new("E6FDD21A-163F-4975-9C8C-A69F1BA37034");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT AddText(uint dwIDCtl, PCWSTR pszText) => HRESULT.S_OK;
        public HRESULT AddText(uint dwIDCtl, string? pszText) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("972F26D2-D2A0-4780-8725-CB0EDBE14230")]
    internal unsafe partial struct IFileDialogEvents : IComIID, IVTable<IFileDialogEvents, IFileDialogEvents.Vtbl>
    {
        private static readonly Guid s_guid = new("972F26D2-D2A0-4780-8725-CB0EDBE14230");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface
        {
            HRESULT OnFileOk(IFileDialog* pfd);
            HRESULT OnFolderChanging(IFileDialog* pfd, IShellItem* psiFolder);
            HRESULT OnFolderChange(IFileDialog* pfd);
            HRESULT OnSelectionChange(IFileDialog* pfd);
            HRESULT OnShareViolation(IFileDialog* pfd, IShellItem* psi, FDE_SHAREVIOLATION_RESPONSE* pResponse);
            HRESULT OnTypeChange(IFileDialog* pfd);
            HRESULT OnOverwrite(IFileDialog* pfd, IShellItem* psi, FDE_OVERWRITE_RESPONSE* pResponse);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct DROPDESCRIPTION
    {
        public DROPIMAGETYPE type;
        public _szMessage_e__FixedBuffer szMessage;
        public _szInsert_e__FixedBuffer szInsert;

        [global::System.Runtime.CompilerServices.InlineArray(260)]
        public struct _szMessage_e__FixedBuffer
        {
            public char e0;

            public static implicit operator _szMessage_e__FixedBuffer(string? s)
            {
                var buf = new _szMessage_e__FixedBuffer();
                if (s is not null)
                {
                    var span = global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref buf.e0, 260);
                    s.AsSpan().Slice(0, Math.Min(s.Length, 259)).CopyTo(span);
                }
                return buf;
            }
        }

        [global::System.Runtime.CompilerServices.InlineArray(260)]
        public struct _szInsert_e__FixedBuffer
        {
            public char e0;

            public static implicit operator _szInsert_e__FixedBuffer(string? s)
            {
                var buf = new _szInsert_e__FixedBuffer();
                if (s is not null)
                {
                    var span = global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref buf.e0, 260);
                    s.AsSpan().Slice(0, Math.Min(s.Length, 259)).CopyTo(span);
                }
                return buf;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DROPFILES
    {
        public uint pFiles;
        public global::System.Drawing.Point pt;
        public BOOL fNC;
        public BOOL fWide;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SHDRAGIMAGE
    {
        public SIZE sizeDragImage;
        public global::System.Drawing.Point ptOffset;
        public HBITMAP hbmpDragImage;
        public COLORREF crColorKey;
    }

    [Flags]
    public enum DSH_FLAGS : uint
    {
        DSH_ALLOWDROPDESCRIPTIONTEXT = 0x0001,
    }

    [Guid("83E076EC-6F17-464A-A94E-3F0D15350542")]
    internal unsafe partial struct IDragSourceHelper2 : IComIID, IVTable<IDragSourceHelper2, IDragSourceHelper2.Vtbl>
    {
        private static readonly Guid s_guid = new("83E076EC-6F17-464A-A94E-3F0D15350542");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT InitializeFromBitmap(SHDRAGIMAGE* pshdi, IDataObject* pDataObject) => HRESULT.S_OK;
        public HRESULT InitializeFromBitmap(in SHDRAGIMAGE pshdi, IDataObject* pDataObject) => HRESULT.S_OK;
        public HRESULT InitializeFromWindow(HWND hwnd, global::System.Drawing.Point* ppt, IDataObject* pDataObject) => HRESULT.S_OK;
        public HRESULT SetFlags(DSH_FLAGS dwFlags) => HRESULT.S_OK;
        public HRESULT SetFlags(uint dwFlags) => HRESULT.S_OK;
        public interface Interface { }
    }

    [Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
    internal unsafe partial struct IDropTargetHelper : IComIID, IVTable<IDropTargetHelper, IDropTargetHelper.Vtbl>
    {
        private static readonly Guid s_guid = new("4657278B-411B-11D2-839A-00C04FD918D0");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT DragEnter(HWND hwndTarget, IDataObject* pDataObject, global::System.Drawing.Point* ppt, uint dwEffect) => HRESULT.S_OK;
        public HRESULT DragEnter(HWND hwndTarget, IDataObject* pDataObject, in global::System.Drawing.Point ppt, uint dwEffect) => HRESULT.S_OK;
        public HRESULT DragEnter(HWND hwndTarget, IDataObject* pDataObject, in global::System.Drawing.Point ppt, DROPEFFECT dwEffect) => HRESULT.S_OK;
        public HRESULT DragLeave() => HRESULT.S_OK;
        public HRESULT DragOver(global::System.Drawing.Point* ppt, uint dwEffect) => HRESULT.S_OK;
        public HRESULT DragOver(in global::System.Drawing.Point ppt, uint dwEffect) => HRESULT.S_OK;
        public HRESULT DragOver(in global::System.Drawing.Point ppt, DROPEFFECT dwEffect) => HRESULT.S_OK;
        public HRESULT DragOver(global::System.Drawing.Point* ppt, DROPEFFECT dwEffect) => HRESULT.S_OK;
        public HRESULT Drop(IDataObject* pDataObject, global::System.Drawing.Point* ppt, uint dwEffect) => HRESULT.S_OK;
        public HRESULT Drop(IDataObject* pDataObject, in global::System.Drawing.Point ppt, uint dwEffect) => HRESULT.S_OK;
        public HRESULT Drop(IDataObject* pDataObject, in global::System.Drawing.Point ppt, DROPEFFECT dwEffect) => HRESULT.S_OK;
        public HRESULT Show(BOOL fShow) => HRESULT.S_OK;
        public interface Interface { }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ITEMIDLIST
    {
        public ushort cb;
        public byte abID;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct BROWSEINFOW
    {
        public HWND hwndOwner;
        public ITEMIDLIST* pidlRoot;
        public PWSTR pszDisplayName;
        public PCWSTR lpszTitle;
        public uint ulFlags;
        public void* lpfn;
        public LPARAM lParam;
        public int iImage;
    }

    [Flags]
    public enum GPFIDL_FLAGS : uint
    {
        GPFIDL_DEFAULT = 0x0000,
        GPFIDL_ALTNAME = 0x0001,
        GPFIDL_UNCPRINTER = 0x0002,
    }

    internal readonly struct CLIPBOARD_FORMAT : IEquatable<CLIPBOARD_FORMAT>
    {
        public readonly ushort Value;
        public CLIPBOARD_FORMAT(ushort value) => Value = value;
        public static implicit operator ushort(CLIPBOARD_FORMAT value) => value.Value;
        public static explicit operator CLIPBOARD_FORMAT(ushort value) => new(value);
        public static explicit operator int(CLIPBOARD_FORMAT value) => value.Value;
        public static explicit operator uint(CLIPBOARD_FORMAT value) => value.Value;
        public static bool operator ==(CLIPBOARD_FORMAT left, CLIPBOARD_FORMAT right) => left.Value == right.Value;
        public static bool operator !=(CLIPBOARD_FORMAT left, CLIPBOARD_FORMAT right) => left.Value != right.Value;
        public override bool Equals(object? obj) => obj is CLIPBOARD_FORMAT other && Equals(other);
        public bool Equals(CLIPBOARD_FORMAT other) => Value == other.Value;
        public override int GetHashCode() => Value.GetHashCode();

        public static readonly CLIPBOARD_FORMAT CF_TEXT = new(1);
        public static readonly CLIPBOARD_FORMAT CF_BITMAP = new(2);
        public static readonly CLIPBOARD_FORMAT CF_METAFILEPICT = new(3);
        public static readonly CLIPBOARD_FORMAT CF_SYLK = new(4);
        public static readonly CLIPBOARD_FORMAT CF_DIF = new(5);
        public static readonly CLIPBOARD_FORMAT CF_TIFF = new(6);
        public static readonly CLIPBOARD_FORMAT CF_OEMTEXT = new(7);
        public static readonly CLIPBOARD_FORMAT CF_DIB = new(8);
        public static readonly CLIPBOARD_FORMAT CF_PALETTE = new(9);
        public static readonly CLIPBOARD_FORMAT CF_PENDATA = new(10);
        public static readonly CLIPBOARD_FORMAT CF_RIFF = new(11);
        public static readonly CLIPBOARD_FORMAT CF_WAVE = new(12);
        public static readonly CLIPBOARD_FORMAT CF_UNICODETEXT = new(13);
        public static readonly CLIPBOARD_FORMAT CF_ENHMETAFILE = new(14);
        public static readonly CLIPBOARD_FORMAT CF_HDROP = new(15);
        public static readonly CLIPBOARD_FORMAT CF_LOCALE = new(16);
        public static readonly CLIPBOARD_FORMAT CF_DIBV5 = new(17);
        public static readonly CLIPBOARD_FORMAT CF_MAX = new(18);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HH_POPUP
    {
        public int cbStruct;
        public HINSTANCE hinst;
        public uint idString;
        public sbyte* pszText;
        public global::System.Drawing.Point pt;
        public COLORREF clrForeground;
        public COLORREF clrBackground;
        public RECT rcMargins;
        public sbyte* pszFont;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HH_FTS_QUERY
    {
        public int cbStruct;
        public BOOL fUniCodeStrings;
        public sbyte* pszSearchQuery;
        public int iProximity;
        public BOOL fStemmedSearch;
        public BOOL fTitleOnly;
        public BOOL fExecute;
        public PCWSTR pszWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HH_AKLINK
    {
        public int cbStruct;
        public BOOL fUniCodeStrings;
        public sbyte* pszKeywords;
        public PCWSTR pszUrl;
        public PCWSTR pszMsgText;
        public PCWSTR pszMsgTitle;
        public PCWSTR pszWindow;
        public BOOL fIndexOnFail;
        public BOOL fReserved;
    }
}
