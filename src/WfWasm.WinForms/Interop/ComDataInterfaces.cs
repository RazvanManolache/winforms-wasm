// Fork-owned: COM data transfer interfaces and structs for Windows.Win32.System.Com.

using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Com;
using ComUnknown = Windows.Win32.System.Com.IUnknown;

namespace Windows.Win32.System.Com
{
[Flags]
internal enum DVASPECT : uint
{
    DVASPECT_CONTENT = 1,
    DVASPECT_THUMBNAIL = 2,
    DVASPECT_ICON = 4,
    DVASPECT_DOCPRINT = 8,
    DVASPECT_OPAQUE = 16,
    DVASPECT_TRANSPARENT = 32,
}

[Flags]
internal enum TYMED : uint
{
    TYMED_HGLOBAL = 1,
    TYMED_FILE = 2,
    TYMED_ISTREAM = 4,
    TYMED_ISTORAGE = 8,
    TYMED_GDI = 16,
    TYMED_MFPICT = 32,
    TYMED_ENHMF = 64,
    TYMED_NULL = 0
}

internal unsafe struct DVTARGETDEVICE
{
    public uint tdSize;
    public ushort tdDriverNameOffset;
    public ushort tdDeviceNameOffset;
    public ushort tdPortNameOffset;
    public ushort tdExtDevmodeOffset;
}

internal unsafe struct FORMATETC
{
    public ushort cfFormat;
    public DVTARGETDEVICE* ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe partial struct STGMEDIUM
{
    public TYMED tymed;
    public _u_union u;
    public IUnknown* pUnkForRelease;

    [StructLayout(LayoutKind.Explicit)]
    public struct _u_union
    {
        [FieldOffset(0)]
        public HBITMAP hBitmap;
        [FieldOffset(0)]
        public void* hMetaFilePict;
        [FieldOffset(0)]
        public HENHMETAFILE hEnhMetaFile;
        [FieldOffset(0)]
        public HGLOBAL hGlobal;
        [FieldOffset(0)]
        public PWSTR lpszFileName;
        [FieldOffset(0)]
        public IStream* pstm;
        [FieldOffset(0)]
        public IStorage* pstg;
    }
}

internal unsafe struct STATDATA
{
    public FORMATETC formatetc;
    public uint advf;
    public IAdviseSink* pAdvSink;
    public uint dwConnection;
}

internal unsafe partial struct IAdviseSink : IComIID, IVTable<IAdviseSink, IAdviseSink.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000010F-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public uint AddRef() => 1;
    public uint Release() => 0;
    public void OnViewChange(int dwAspect, int lindex) { }
    public void OnViewChange(uint dwAspect, int lindex) { }

    public interface Interface
    {
        void OnDataChange(FORMATETC* pFormatetc, STGMEDIUM* pStgmed);
        void OnViewChange(uint dwAspect, int lindex);
        void OnRename(IMoniker* pmk);
        void OnSave();
        void OnClose();
    }
}

internal unsafe partial struct IEnumSTATDATA : IComIID, IVTable<IEnumSTATDATA, IEnumSTATDATA.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000105-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Next(uint celt, STATDATA* rgelt, uint* pceltFetched);
        HRESULT Skip(uint celt);
        HRESULT Reset();
        HRESULT Clone(IEnumSTATDATA** ppenum);
    }
}

internal unsafe partial struct IEnumFORMATETC : IComIID, IVTable<IEnumFORMATETC, IEnumFORMATETC.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000103-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT Next(uint celt, FORMATETC* rgelt, uint* pceltFetched = null) => HRESULT.S_FALSE;
    public HRESULT Skip(uint celt) => HRESULT.S_OK;
    public HRESULT Reset() => HRESULT.S_OK;
    public HRESULT Clone(IEnumFORMATETC** ppenum) => HRESULT.E_NOTIMPL;

    public interface Interface
    {
        HRESULT Next(uint celt, FORMATETC* rgelt, uint* pceltFetched);
        HRESULT Skip(uint celt);
        HRESULT Reset();
        HRESULT Clone(IEnumFORMATETC** ppenum);
    }
}

internal unsafe partial struct IDataObject : IComIID, IVTable<IDataObject, IDataObject.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000010E-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
    public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
    public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
    public HRESULT QueryInterface<T>(out T* ppvObject) where T : unmanaged, IComIID { ppvObject = null; return HRESULT.E_NOTIMPL; }
    public uint AddRef() => 1;
    public uint Release() => 1;

    public HRESULT GetData(FORMATETC* pformatetcIn, STGMEDIUM* pmedium) => HRESULT.E_NOTIMPL;
    public HRESULT GetData(FORMATETC* pformatetcIn, out STGMEDIUM pmedium) { fixed (STGMEDIUM* pm = &pmedium) return GetData(pformatetcIn, pm); }
    public HRESULT GetData(in FORMATETC pformatetcIn, out STGMEDIUM pmedium) { fixed (FORMATETC* pf = &pformatetcIn) fixed (STGMEDIUM* pm = &pmedium) return GetData(pf, pm); }
    public HRESULT GetDataHere(FORMATETC* pformatetc, STGMEDIUM* pmedium) => HRESULT.E_NOTIMPL;
    public HRESULT QueryGetData(FORMATETC* pformatetc) => HRESULT.E_NOTIMPL;
    public HRESULT QueryGetData(in FORMATETC pformatetc) => HRESULT.E_NOTIMPL;
    public HRESULT GetCanonicalFormatEtc(FORMATETC* pformatectIn, FORMATETC* pformatetcOut) => HRESULT.E_NOTIMPL;
    public HRESULT GetCanonicalFormatEtc(in FORMATETC pformatetcIn, out FORMATETC pformatetcOut) { pformatetcOut = default; return HRESULT.E_NOTIMPL; }
    public HRESULT SetData(FORMATETC* pformatetc, STGMEDIUM* pmedium, BOOL fRelease) => HRESULT.E_NOTIMPL;
    public HRESULT EnumFormatEtc(uint dwDirection, IEnumFORMATETC** ppenumFormatEtc) => HRESULT.E_NOTIMPL;
    public HRESULT DAdvise(FORMATETC* pformatetc, uint advf, IAdviseSink* pAdvSink, uint* pdwConnection) => HRESULT.E_NOTIMPL;
    public HRESULT DUnadvise(uint dwConnection) => HRESULT.E_NOTIMPL;
    public HRESULT EnumDAdvise(IEnumSTATDATA** ppenumAdvise) => HRESULT.E_NOTIMPL;

    public interface Interface
    {
        HRESULT GetData(FORMATETC* pformatetcIn, STGMEDIUM* pmedium);
        HRESULT GetDataHere(FORMATETC* pformatetc, STGMEDIUM* pmedium);
        HRESULT QueryGetData(FORMATETC* pformatetc);
        HRESULT GetCanonicalFormatEtc(FORMATETC* pformatectIn, FORMATETC* pformatetcOut);
        HRESULT SetData(FORMATETC* pformatetc, STGMEDIUM* pmedium, BOOL fRelease);
        HRESULT EnumFormatEtc(uint dwDirection, IEnumFORMATETC** ppenumFormatEtc);
        HRESULT DAdvise(FORMATETC* pformatetc, uint advf, IAdviseSink* pAdvSink, uint* pdwConnection);
        HRESULT DUnadvise(uint dwConnection);
        HRESULT EnumDAdvise(IEnumSTATDATA** ppenumAdvise);
    }
}

[Flags]
internal enum STGC : uint
{
    STGC_DEFAULT = 0,
    STGC_OVERWRITE = 1,
    STGC_ONLYIFCURRENT = 2,
    STGC_DANGEROUSLYCOMMITMERELYTODISKCACHE = 4,
    STGC_CONSOLIDATE = 8,
}
}

namespace Windows.Win32
{
    using Windows.Win32.System.Com;
    using ComUnknown = Windows.Win32.System.Com.IUnknown;

    internal static unsafe partial class ComHelpers
    {
        static partial void PopulateIUnknownImpl<TComInterface>(ComUnknown.Vtbl* vtable) where TComInterface : unmanaged;
    }
}
