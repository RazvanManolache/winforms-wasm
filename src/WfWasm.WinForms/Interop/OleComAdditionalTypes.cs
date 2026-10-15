// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.System.Ole
{
    internal enum PICTYPE : short
    {
        PICTYPE_UNINITIALIZED = -1,
        PICTYPE_NONE = 0,
        PICTYPE_BITMAP = 1,
        PICTYPE_METAFILE = 2,
        PICTYPE_ICON = 3,
        PICTYPE_ENHMETAFILE = 4,
    }

    [Guid("7BF80980-BF32-101A-8BBB-00AA00300CAB")]
    internal unsafe partial struct IPicture : IComIID, IVTable<IPicture, IPicture.Vtbl>
    {
        private static readonly Guid s_guid = new("7BF80980-BF32-101A-8BBB-00AA00300CAB");
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

        public HRESULT get_Handle(uint* pHandle) => HRESULT.E_NOTIMPL;
        public HRESULT get_Handle(out OLE_HANDLE pHandle) { pHandle = default; return HRESULT.E_NOTIMPL; }
        public HRESULT get_hPal(uint* phPal) => HRESULT.E_NOTIMPL;
        public HRESULT get_hPal(out OLE_HANDLE phPal) { phPal = default; return HRESULT.E_NOTIMPL; }
        public HRESULT get_Type(short* pType) => HRESULT.E_NOTIMPL;
        public HRESULT get_Type(out PICTYPE pType) { pType = default; return HRESULT.E_NOTIMPL; }
        public HRESULT get_Width(int* pWidth) => HRESULT.E_NOTIMPL;
        public HRESULT get_Width(out int pWidth) { pWidth = 0; return HRESULT.E_NOTIMPL; }
        public HRESULT get_Height(int* pHeight) => HRESULT.E_NOTIMPL;
        public HRESULT get_Height(out int pHeight) { pHeight = 0; return HRESULT.E_NOTIMPL; }
        public HRESULT Render(HDC hdc, int x, int y, int cx, int cy, int xSrc, int ySrc, int cxSrc, int cySrc, void* prcWBounds) => HRESULT.S_OK;

        public interface Interface { }
    }

    [Guid("7BF80981-BF32-101A-8BBB-00AA00300CAB")]
    internal unsafe partial struct IPictureDisp : IComIID, IVTable<IPictureDisp, IPictureDisp.Vtbl>
    {
        private static readonly Guid s_guid = new("7BF80981-BF32-101A-8BBB-00AA00300CAB");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public interface Interface { }
    }

    internal enum OLECLOSE : uint
    {
        OLECLOSE_SAVEIFDIRTY = 0,
        OLECLOSE_NOSAVE = 1,
        OLECLOSE_PROMPTSAVE = 2,
    }

    [Flags]
    internal enum OLECONTF : uint
    {
        OLECONTF_EMBEDDINGS = 1,
        OLECONTF_LINKS = 2,
        OLECONTF_OTHERS = 4,
        OLECONTF_ONLYUSER = 8,
        OLECONTF_ONLYIFRUNNING = 16,
    }

    [Guid("B722BCCB-4E68-101B-A2BC-00AA00404770")]
    internal unsafe partial struct IOleCommandTarget : IComIID, IVTable<IOleCommandTarget, IOleCommandTarget.Vtbl>
    {
        private static readonly Guid s_guid = new("B722BCCB-4E68-101B-A2BC-00AA00404770");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT QueryStatus(Guid* pguidCmdGroup, uint cCmds, void* prgCmds, void* pCmdText) => HRESULT.E_NOTIMPL;
        public HRESULT Exec(Guid* pguidCmdGroup, uint nCmdID, uint nCmdexecopt, VARIANT* pvaIn, VARIANT* pvaOut) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("00000111-0000-0000-C000-000000000046")]
    internal unsafe partial struct IOleAdviseHolder : IComIID, IVTable<IOleAdviseHolder, IOleAdviseHolder.Vtbl>
    {
        private static readonly Guid s_guid = new("00000111-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT Advise(IAdviseSink* pAdvise, uint* pdwConnection) => HRESULT.E_NOTIMPL;
        public HRESULT Unadvise(uint dwConnection) => HRESULT.S_OK;
        public HRESULT EnumAdvise(IEnumSTATDATA** ppenumAdvise) => HRESULT.E_NOTIMPL;
        public HRESULT SendOnRename(IMoniker* pmk) => HRESULT.S_OK;
        public HRESULT SendOnSave() => HRESULT.S_OK;
        public HRESULT SendOnClose() => HRESULT.S_OK;

        public interface Interface { }
    }
}

namespace Windows.Win32.System.Com
{
    [Guid("B196B286-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct IConnectionPoint : IComIID, IVTable<IConnectionPoint, IConnectionPoint.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B286-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetConnectionInterface(Guid* pIID) => HRESULT.E_NOTIMPL;
        public HRESULT GetConnectionPointContainer(void** ppCPC) => HRESULT.E_NOTIMPL;
        public HRESULT Advise(IUnknown* pUnkSink, uint* pdwCookie) => HRESULT.E_NOTIMPL;
        public HRESULT Advise(IUnknown* pUnkSink, out uint pdwCookie) { pdwCookie = 0; return HRESULT.E_NOTIMPL; }
        public HRESULT Unadvise(uint dwCookie) => HRESULT.S_OK;
        public HRESULT EnumConnections(void** ppEnum) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [Guid("B196B284-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct IConnectionPointContainer : IComIID, IVTable<IConnectionPointContainer, IConnectionPointContainer.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B284-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT EnumConnectionPoints(void** ppEnum) => HRESULT.E_NOTIMPL;
        public HRESULT FindConnectionPoint(Guid* riid, IConnectionPoint** ppCP) => HRESULT.E_NOTIMPL;
        public HRESULT FindConnectionPoint(in Guid riid, IConnectionPoint** ppCP) => HRESULT.E_NOTIMPL;

        public interface Interface
        {
            HRESULT FindConnectionPoint(Guid* riid, IConnectionPoint** ppCP);
        }
    }

    [Guid("3B0D0EA0-1153-11D2-901F-00C04FB951ED")]
    internal unsafe partial struct IDataObjectAsyncCapability : IComIID, IVTable<IDataObjectAsyncCapability, IDataObjectAsyncCapability.Vtbl>
    {
        private static readonly Guid s_guid = new("3B0D0EA0-1153-11D2-901F-00C04FB951ED");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT SetAsyncMode(BOOL fDoOpAsync) => HRESULT.S_OK;
        public HRESULT GetAsyncMode(BOOL* pfIsOpAsync) => HRESULT.E_NOTIMPL;
        public HRESULT GetAsyncMode(out BOOL pfIsOpAsync) { pfIsOpAsync = (BOOL)false; return HRESULT.E_NOTIMPL; }
        public HRESULT StartOperation(IBindCtx* pbcReserved = null) => HRESULT.S_OK;
        public HRESULT InOperation(BOOL* pfInAsyncOp) => HRESULT.E_NOTIMPL;
        public HRESULT EndOperation(HRESULT hResult, IBindCtx* pbcReserved, uint dwEffects) => HRESULT.S_OK;
        public uint AddRef() => 1;
        public uint Release() => 0;

        public interface Interface { }
    }
}

namespace Windows.Win32.System.Com
{
    [Guid("0000000A-0000-0000-C000-000000000046")]
    internal unsafe partial struct ILockBytes : IComIID, IVTable<ILockBytes, ILockBytes.Vtbl>
    {
        private static readonly Guid s_guid = new("0000000A-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT ReadAt(ulong ulOffset, void* pv, uint cb, uint* pcbRead) => HRESULT.E_NOTIMPL;
        public HRESULT WriteAt(ulong ulOffset, void* pv, uint cb, uint* pcbWritten) => HRESULT.E_NOTIMPL;
        public HRESULT Flush() => HRESULT.S_OK;
        public HRESULT SetSize(ulong cb) => HRESULT.S_OK;
        public HRESULT LockRegion(ulong libOffset, ulong cb, uint dwLockType) => HRESULT.E_NOTIMPL;
        public HRESULT UnlockRegion(ulong libOffset, ulong cb, uint dwLockType) => HRESULT.E_NOTIMPL;
        public HRESULT Stat(STATSTG* pstatstg, uint grfStatFlag) => HRESULT.E_NOTIMPL;
        public HRESULT Stat(out STATSTG pstatstg, uint grfStatFlag) { pstatstg = default; return HRESULT.S_OK; }
        public uint AddRef() => 1;
        public uint Release() => 0;

        public interface Interface { }
    }

    [Flags]
    internal enum ADVF : uint
    {
        ADVF_NODATA = 1,
        ADVF_PRIMEFIRST = 2,
        ADVF_ONLYONCE = 4,
        ADVF_DATAONSTOP = 64,
        ADVFCACHE_NOHANDLER = 8,
        ADVFCACHE_FORCEBUILTIN = 16,
        ADVFCACHE_ONSAVE = 32,
    }

    [Guid("00000146-0000-0000-C000-000000000046")]
    internal unsafe partial struct IGlobalInterfaceTable : IComIID, IVTable<IGlobalInterfaceTable, IGlobalInterfaceTable.Vtbl>
    {
        private static readonly Guid s_guid = new("00000146-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT RegisterInterfaceInGlobal(IUnknown* pUnk, Guid* riid, uint* pdwCookie) => HRESULT.E_NOTIMPL;
        public HRESULT RevokeInterfaceFromGlobal(uint dwCookie) => HRESULT.S_OK;
        public HRESULT GetInterfaceFromGlobal(uint dwCookie, Guid* riid, void** ppv) => HRESULT.E_NOTIMPL;
        public HRESULT GetInterfaceFromGlobal(uint dwCookie, in Guid riid, out void* ppv) { ppv = null; return HRESULT.E_NOTIMPL; }

        public interface Interface { }
    }

    internal enum TYPEKIND
    {
        TKIND_ENUM = 0,
        TKIND_RECORD = 1,
        TKIND_MODULE = 2,
        TKIND_INTERFACE = 3,
        TKIND_DISPATCH = 4,
        TKIND_COCLASS = 5,
        TKIND_ALIAS = 6,
        TKIND_UNION = 7,
        TKIND_MAX = 8,
    }

    [Flags]
    internal enum VARFLAGS : ushort
    {
        VARFLAG_FREADONLY = 1,
        VARFLAG_FSOURCE = 2,
        VARFLAG_FBINDABLE = 4,
        VARFLAG_FREQUESTEDIT = 8,
        VARFLAG_FDISPLAYBIND = 16,
        VARFLAG_FDEFAULTBIND = 32,
        VARFLAG_FHIDDEN = 64,
        VARFLAG_FRESTRICTED = 128,
        VARFLAG_FDEFAULTCOLLELEM = 256,
        VARFLAG_FUIDEFAULT = 512,
        VARFLAG_FNONBROWSABLE = 1024,
        VARFLAG_FREPLACEABLE = 2048,
        VARFLAG_FIMMEDIATEBIND = 4096,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TYPEDESC
    {
        public _Anonymous_e__Union Anonymous;
        public VARENUM vt;

        [StructLayout(LayoutKind.Explicit)]
        public struct _Anonymous_e__Union
        {
            [FieldOffset(0)]
            public TYPEDESC* lptdesc;
            [FieldOffset(0)]
            public void* lpadesc;
            [FieldOffset(0)]
            public uint hreftype;
        }
    }

    [Flags]
    internal enum STGM : uint
    {
        STGM_DIRECT = 0x00000000,
        STGM_TRANSACTED = 0x00010000,
        STGM_SIMPLE = 0x08000000,
        STGM_READ = 0x00000000,
        STGM_WRITE = 0x00000001,
        STGM_READWRITE = 0x00000002,
        STGM_SHARE_DENY_NONE = 0x00000040,
        STGM_SHARE_DENY_READ = 0x00000030,
        STGM_SHARE_DENY_WRITE = 0x00000020,
        STGM_SHARE_EXCLUSIVE = 0x00000010,
        STGM_PRIORITY = 0x00040000,
        STGM_DELETEONRELEASE = 0x04000000,
        STGM_CREATE = 0x00001000,
        STGM_CONVERT = 0x00020000,
        STGM_FAILIFTHERE = 0x00000000,
        STGM_NOSCRATCH = 0x00100000,
        STGM_NOSNAPSHOT = 0x00200000,
    }

    [Guid("0000002F-0000-0000-C000-000000000046")]
    internal unsafe partial struct IRecordInfo : IComIID, IVTable<IRecordInfo, IRecordInfo.Vtbl>
    {
        private static readonly Guid s_guid = new("0000002F-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT RecordInit(void* pvNew) => HRESULT.S_OK;
        public HRESULT RecordClear(void* pvExisting) => HRESULT.S_OK;
        public HRESULT RecordCopy(void* pvExisting, void* pvNew) => HRESULT.S_OK;
        public HRESULT GetGuid(Guid* pguid) => HRESULT.E_NOTIMPL;
        public HRESULT GetName(BSTR* pbstrName) => HRESULT.E_NOTIMPL;
        public HRESULT GetSize(uint* pcbSize) => HRESULT.E_NOTIMPL;

        public interface Interface { }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct CAUUID
    {
        public uint cElems;
        public Guid* pElems;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LICINFO
    {
        public int cbLicInfo;
        public BOOL fRuntimeKeyAvail;
        public BOOL fLicVerified;
    }

    [Guid("B196B28F-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct IClassFactory2 : IComIID, IVTable<IClassFactory2, IClassFactory2.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B28F-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT CreateInstance(void* pUnkOuter, Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
        public HRESULT LockServer(BOOL fLock) => HRESULT.S_OK;
        public HRESULT GetLicInfo(LICINFO* pLicInfo) => HRESULT.E_NOTIMPL;
        public HRESULT RequestLicKey(uint dwReserved, BSTR* pbstrKey) => HRESULT.E_NOTIMPL;
        public HRESULT CreateInstanceLic(void* pUnkOuter, void* pUnkReserved, Guid* riid, BSTR bstrKey, void** ppvObj) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("1CF2B230-FF06-11D0-BCEE-00A0C9034778")]
    internal unsafe partial struct IErrorInfo : IComIID, IVTable<IErrorInfo, IErrorInfo.Vtbl>
    {
        private static readonly Guid s_guid = new("1CF2B230-FF06-11D0-BCEE-00A0C9034778");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetGUID(Guid* pGUID) => HRESULT.E_NOTIMPL;
        public HRESULT GetSource(BSTR* pBstrSource) => HRESULT.E_NOTIMPL;
        public HRESULT GetDescription(BSTR* pBstrDescription) => HRESULT.E_NOTIMPL;
        public HRESULT GetHelpFile(BSTR* pBstrHelpFile) => HRESULT.E_NOTIMPL;
        public HRESULT GetHelpContext(uint* pdwHelpContext) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("DF0B3D60-548F-101B-8E65-08002B2BD119")]
    internal unsafe partial struct ISupportErrorInfo : IComIID, IVTable<ISupportErrorInfo, ISupportErrorInfo.Vtbl>
    {
        private static readonly Guid s_guid = new("DF0B3D60-548F-101B-8E65-08002B2BD119");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT InterfaceSupportsErrorInfo(Guid* riid) => HRESULT.S_OK;
        public interface Interface { }
    }

    [Guid("B196B28D-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct ISpecifyPropertyPages : IComIID, IVTable<ISpecifyPropertyPages, ISpecifyPropertyPages.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B28D-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetPages(CAUUID* pPages) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Flags]
    public enum MULTICLASSINFO_FLAGS : uint
    {
        MULTICLASSINFO_GETTYPEINFO = 0x00000001,
        MULTICLASSINFO_GETNUMRESERVED = 0x00000002,
    }

    [Guid("B196B283-BAB4-101A-B69C-00AA00341D07")]
    internal unsafe partial struct IProvideClassInfo : IComIID, IVTable<IProvideClassInfo, IProvideClassInfo.Vtbl>
    {
        private static readonly Guid s_guid = new("B196B283-BAB4-101A-B69C-00AA00341D07");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetClassInfo(ITypeInfo** ppTI) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("A7ABA9C1-8983-11CF-8F20-00805F2CD064")]
    internal unsafe partial struct IProvideMultipleClassInfo : IComIID, IVTable<IProvideMultipleClassInfo, IProvideMultipleClassInfo.Vtbl>
    {
        private static readonly Guid s_guid = new("A7ABA9C1-8983-11CF-8F20-00805F2CD064");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetClassInfo(ITypeInfo** ppTI) => HRESULT.E_NOTIMPL;
        public HRESULT GetMultiTypeInfoCount(uint* pcti) => HRESULT.E_NOTIMPL;
        public HRESULT GetInfoOfIndex(uint iti, MULTICLASSINFO_FLAGS dwFlags, ITypeInfo** pptiCoClass, uint* pdwTIFlags, uint* pcdispidReserved, Guid* piidPrimary, Guid* piidSource) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("1C2056CC-5BB4-101A-B7CE-00AA003477F8")]
    internal unsafe partial struct IOleInPlaceObjectWindowless : IComIID, IVTable<IOleInPlaceObjectWindowless, IOleInPlaceObjectWindowless.Vtbl>
    {
        private static readonly Guid s_guid = new("1C2056CC-5BB4-101A-B7CE-00AA003477F8");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT OnWindowMessage(uint msg, WPARAM wParam, LPARAM lParam, LRESULT* plResult) => HRESULT.S_OK;
        public HRESULT GetDropTarget(IDropTarget** ppDropTarget) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [Guid("00000109-0000-0000-C000-000000000046")]
    internal unsafe partial struct IPersistStream : IComIID, IVTable<IPersistStream, IPersistStream.Vtbl>
    {
        private static readonly Guid s_guid = new("00000109-0000-0000-C000-000000000046");
        public static ref readonly Guid IID_Guid => ref s_guid;
        static ref readonly Guid IComIID.Guid => ref s_guid;
        public void** lpVtbl;
        public struct Vtbl { internal void* QueryInterface; internal void* AddRef; internal void* Release; }
        public static void PopulateVTable(Vtbl* vtable) { }

        public HRESULT GetClassID(Guid* pClassID) => HRESULT.E_NOTIMPL;
        public HRESULT IsDirty() => HRESULT.S_FALSE;
        public HRESULT Load(IStream* pStm) => HRESULT.S_OK;
        public HRESULT Save(IStream* pStm, BOOL fClearDirty) => HRESULT.S_OK;
        public HRESULT GetSizeMax(ulong* pcbSize) => HRESULT.E_NOTIMPL;
        public interface Interface { }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct OCPFIPARAMS
    {
        public uint cbStructSize;
        public HWND hWndOwner;
        public int x;
        public int y;
        public PCWSTR lpszCaption;
        public uint cObjects;
        public void* lplpUnk;
        public uint cPages;
        public Guid* lpPages;
        public uint lcid;
        public int dispidInitialProperty;
    }

    public enum USERCLASSTYPE : uint
    {
        USERCLASSTYPE_FULL = 1,
        USERCLASSTYPE_SHORT = 2,
        USERCLASSTYPE_APPNAME = 3,
    }

    public enum VARFORMAT_FIRST_DAY
    {
        VAR_FORMAT_FIRST_DAY_SYSTEM = 0,
        VAR_FORMAT_FIRST_DAY_SUNDAY = 1,
        VAR_FORMAT_FIRST_DAY_MONDAY = 2,
        VAR_FORMAT_FIRST_DAY_TUESDAY = 3,
        VAR_FORMAT_FIRST_DAY_WEDNESDAY = 4,
        VAR_FORMAT_FIRST_DAY_THURSDAY = 5,
        VAR_FORMAT_FIRST_DAY_FRIDAY = 6,
        VAR_FORMAT_FIRST_DAY_SATURDAY = 7,
    }

    public enum VARFORMAT_FIRST_WEEK
    {
        VAR_FORMAT_FIRST_WEEK_SYSTEM = 0,
        VAR_FORMAT_FIRST_WEEK_1 = 1,
        VAR_FORMAT_FIRST_WEEK_2 = 2,
        VAR_FORMAT_FIRST_WEEK_3 = 3,
    }
}

namespace Windows.Win32
{
    using Windows.Win32.System.Com;

    internal static partial class PInvoke
    {
        public static unsafe HRESULT CreateILockBytesOnHGlobal(HGLOBAL hGlobal, BOOL fDeleteOnRelease, ILockBytes** ppLkbyt) => HRESULT.E_NOTIMPL;
        public static unsafe HRESULT StgCreateDocfileOnILockBytes(ILockBytes* plkbyt, STGM grfMode, uint reserved, IStorage** ppstgOpen) => HRESULT.E_NOTIMPL;
    }
}
