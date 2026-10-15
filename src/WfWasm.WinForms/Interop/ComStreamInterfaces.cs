// Fork-owned: Stream and storage COM interfaces for Windows.Win32.System.Com.

using System.IO;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.System.Com;

internal unsafe partial struct ISequentialStream : IComIID, IVTable<ISequentialStream, ISequentialStream.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0C733A30-2A1C-11CE-ADE5-00AA0044773D");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Read(void* pv, uint cb, uint* pcbRead);
        HRESULT Write(void* pv, uint cb, uint* pcbWritten);
    }
}

internal unsafe partial struct IStream : IComIID, IVTable<IStream, IStream.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000000C-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT Read(void* pv, uint cb, uint* pcbRead) => HRESULT.E_NOTIMPL;
    public HRESULT Write(void* pv, uint cb, uint* pcbWritten) => HRESULT.E_NOTIMPL;
    public HRESULT Seek(long dlibMove, SeekOrigin dwOrigin, ulong* plibNewPosition) => HRESULT.E_NOTIMPL;
    public HRESULT SetSize(ulong libNewSize) => HRESULT.E_NOTIMPL;
    public HRESULT CopyTo(IStream* pstm, ulong cb, ulong* pcbRead, ulong* pcbWritten) => HRESULT.E_NOTIMPL;
    public HRESULT Commit(uint grfCommitFlags) => HRESULT.E_NOTIMPL;
    public HRESULT Revert() => HRESULT.E_NOTIMPL;
    public HRESULT LockRegion(ulong libOffset, ulong cb, uint dwLockType) => HRESULT.E_NOTIMPL;
    public HRESULT UnlockRegion(ulong libOffset, ulong cb, uint dwLockType) => HRESULT.E_NOTIMPL;
    public HRESULT Stat(STATSTG* pstatstg, uint grfStatFlag) => HRESULT.E_NOTIMPL;
    public HRESULT Stat(out STATSTG pstatstg, uint grfStatFlag) { pstatstg = default; return HRESULT.S_OK; }
    public HRESULT Clone(IStream** ppstm) => HRESULT.E_NOTIMPL;

    public interface Interface : ISequentialStream.Interface
    {
        new HRESULT Read(void* pv, uint cb, uint* pcbRead);
        new HRESULT Write(void* pv, uint cb, uint* pcbWritten);
        HRESULT Seek(long dlibMove, SeekOrigin dwOrigin, ulong* plibNewPosition);
        HRESULT SetSize(ulong libNewSize);
        HRESULT CopyTo(IStream* pstm, ulong cb, ulong* pcbRead, ulong* pcbWritten);
        HRESULT Commit(uint grfCommitFlags);
        HRESULT Revert();
        HRESULT LockRegion(ulong libOffset, ulong cb, uint dwLockType);
        HRESULT UnlockRegion(ulong libOffset, ulong cb, uint dwLockType);
        HRESULT Stat(STATSTG* pstatstg, uint grfStatFlag);
        HRESULT Clone(IStream** ppstm);
    }
}

internal unsafe partial struct IStorage : IComIID, IVTable<IStorage, IStorage.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000000B-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public uint AddRef() => 1;
    public uint Release() => 0;
    public HRESULT CreateStream(PCWSTR pwcsName, STGM grfMode, uint reserved1, uint reserved2, IStream** ppstm) => HRESULT.S_OK;
    public HRESULT CreateStream(string? pwcsName, STGM grfMode, uint reserved1, uint reserved2, IStream** ppstm) => HRESULT.S_OK;
    public HRESULT OpenStream(PCWSTR pwcsName, STGM grfMode, uint reserved2, IStream** ppstm) => HRESULT.S_OK;
        public HRESULT OpenStream(string? pwcsName, STGM grfMode, uint reserved2, IStream** ppstm) => HRESULT.S_OK;
        public HRESULT OpenStream(PCWSTR pwcsName, void* reserved1, STGM grfMode, uint reserved2, IStream** ppstm) => HRESULT.S_OK;
    public HRESULT OpenStream(string? pwcsName, void* reserved1, STGM grfMode, uint reserved2, IStream** ppstm) => HRESULT.S_OK;
    public HRESULT Commit(uint grfCommitFlags) => HRESULT.S_OK;

    public interface Interface { }
}

internal unsafe partial struct IMoniker : IComIID, IVTable<IMoniker, IMoniker.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000000F-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface { }
}

internal unsafe partial struct IBindCtx : IComIID, IVTable<IBindCtx, IBindCtx.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000000E-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface { }
}

internal unsafe partial struct IPersist : IComIID, IVTable<IPersist, IPersist.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000010C-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetClassID(Guid* pClassID);
    }
}

internal unsafe partial struct IPersistStreamInit : IComIID, IVTable<IPersistStreamInit, IPersistStreamInit.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("7FD52380-4E07-101B-AE2D-08002B2EC713");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetClassID(Guid* pClassID) => HRESULT.S_OK;
    public HRESULT IsDirty() => HRESULT.S_FALSE;
    public HRESULT Load(IStream* pStm) => HRESULT.S_OK;
    public HRESULT Save(IStream* pStm, BOOL fClearDirty) => HRESULT.S_OK;
    public HRESULT GetSizeMax(ulong* pCbSize) => HRESULT.S_OK;
    public HRESULT InitNew() => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetClassID(Guid* pClassID);
        HRESULT IsDirty();
        HRESULT Load(IStream* pStm);
        HRESULT Save(IStream* pStm, BOOL fClearDirty);
        HRESULT GetSizeMax(ulong* pCbSize);
        HRESULT InitNew();
    }
}

internal unsafe partial struct IPersistStorage : IComIID, IVTable<IPersistStorage, IPersistStorage.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0000010A-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetClassID(Guid* pClassID) => HRESULT.S_OK;
    public HRESULT IsDirty() => HRESULT.S_FALSE;
    public HRESULT InitNew(IStorage* pStg) => HRESULT.S_OK;
    public HRESULT Load(IStorage* pStg) => HRESULT.S_OK;
    public HRESULT Save(IStorage* pStgSave, BOOL fSameAsLoad) => HRESULT.S_OK;
    public HRESULT SaveCompleted(IStorage* pStgNew) => HRESULT.S_OK;
    public HRESULT HandsOffStorage() => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetClassID(Guid* pClassID);
        HRESULT IsDirty();
        HRESULT InitNew(IStorage* pStg);
        HRESULT Load(IStorage* pStg);
        HRESULT Save(IStorage* pStgSave, BOOL fSameAsLoad);
        HRESULT SaveCompleted(IStorage* pStgNew);
        HRESULT HandsOffStorage();
    }
}

internal unsafe partial struct IPersistPropertyBag : IComIID, IVTable<IPersistPropertyBag, IPersistPropertyBag.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("37D84F60-42CB-11CE-8135-00AA004BB851");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetClassID(Guid* pClassID) => HRESULT.S_OK;
    public HRESULT InitNew() => HRESULT.S_OK;
    public HRESULT Load(IPropertyBag* pPropBag, IErrorLog* pErrorLog) => HRESULT.S_OK;
    public HRESULT Save(IPropertyBag* pPropBag, BOOL fClearDirty, BOOL fSaveAllProperties) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT GetClassID(Guid* pClassID);
        HRESULT InitNew();
        HRESULT Load(IPropertyBag* pPropBag, IErrorLog* pErrorLog);
        HRESULT Save(IPropertyBag* pPropBag, BOOL fClearDirty, BOOL fSaveAllProperties);
    }
}

internal unsafe partial struct IPropertyBag : IComIID, IVTable<IPropertyBag, IPropertyBag.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("55272A00-42CB-11CE-8135-00AA004BB851");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT Read(PCWSTR pszPropName, VARIANT* pVar, IErrorLog* pErrorLog) => HRESULT.S_OK;
    public HRESULT Write(PCWSTR pszPropName, VARIANT* pVar) => HRESULT.S_OK;
    public HRESULT Write(string pszPropName, VARIANT pVar) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT Read(PCWSTR pszPropName, VARIANT* pVar, IErrorLog* pErrorLog);
        HRESULT Write(PCWSTR pszPropName, VARIANT* pVar);
    }
}

internal unsafe partial struct IEnumUnknown : IComIID, IVTable<IEnumUnknown, IEnumUnknown.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00000100-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Next(uint celt, IUnknown** rgelt, uint* pceltFetched);
        HRESULT Skip(uint celt);
        HRESULT Reset();
        HRESULT Clone(IEnumUnknown** ppenum);
    }
}

internal unsafe partial struct IEnumVARIANT : IComIID, IVTable<IEnumVARIANT, IEnumVARIANT.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00020404-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT Next(uint celt, VARIANT* rgVar, uint* pCeltFetched = null) => HRESULT.S_FALSE;
    public HRESULT Skip(uint celt) => HRESULT.S_OK;
    public HRESULT Reset() => HRESULT.S_OK;
    public HRESULT Clone(IEnumVARIANT** ppEnum) => HRESULT.E_NOTIMPL;

    public interface Interface
    {
        HRESULT Next(uint celt, VARIANT* rgVar, uint* pCeltFetched);
        HRESULT Skip(uint celt);
        HRESULT Reset();
        HRESULT Clone(IEnumVARIANT** ppEnum);
    }
}
