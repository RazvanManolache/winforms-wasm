// Fork-owned: Base COM interfaces for Windows.Win32.System.Com.

using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.System.Com;

internal unsafe partial struct IUnknown : IComIID, IVTable<IUnknown, IUnknown.Vtbl>
{
    private readonly void** _vtable;

    public unsafe struct Vtbl
    {
        internal delegate* unmanaged[Stdcall]<IUnknown*, Guid*, void**, HRESULT> QueryInterface_1;
        internal delegate* unmanaged[Stdcall]<IUnknown*, uint> AddRef_2;
        internal delegate* unmanaged[Stdcall]<IUnknown*, uint> Release_3;
    }

    private static readonly Guid s_guid = new("00000000-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT QueryInterface(Guid* riid, void** ppvObject)
    {
        // No real COM on the canvas backend: every query fails with E_NOINTERFACE.
        if (ppvObject is not null)
        {
            *ppvObject = null;
        }

        return HRESULT.E_NOINTERFACE;
    }
    public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
    public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
    public HRESULT QueryInterface<T>(out T* ppvObject) where T : unmanaged, IComIID
    {
        ppvObject = null;
        fixed (T** p = &ppvObject)
            return QueryInterface(IID.GetRef<T>(), (void**)p);
    }
    public uint AddRef() => 1;
    public uint Release() => 1;
}

internal unsafe partial struct IDispatch : IComIID
{
    private readonly void** _vtable;

    public unsafe struct Vtbl
    {
        internal delegate* unmanaged[Stdcall]<IDispatch*, Guid*, void**, HRESULT> QueryInterface_1;
        internal delegate* unmanaged[Stdcall]<IDispatch*, uint> AddRef_2;
        internal delegate* unmanaged[Stdcall]<IDispatch*, uint> Release_3;
        internal delegate* unmanaged[Stdcall]<IDispatch*, uint*, HRESULT> GetTypeInfoCount_4;
        internal delegate* unmanaged[Stdcall]<IDispatch*, uint, uint, ITypeInfo**, HRESULT> GetTypeInfo_5;
        internal delegate* unmanaged[Stdcall]<IDispatch*, Guid*, PWSTR*, uint, uint, int*, HRESULT> GetIDsOfNames_6;
        internal delegate* unmanaged[Stdcall]<IDispatch*, int, Guid*, uint, DISPATCH_FLAGS, DISPPARAMS*, VARIANT*, EXCEPINFO*, uint*, HRESULT> Invoke_7;
    }

    private static readonly Guid s_guid = new("00020400-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;

    public HRESULT QueryInterface(Guid* riid, void** ppvObject)
    {
        // No real COM on the canvas backend: every query fails with E_NOINTERFACE.
        if (ppvObject is not null)
        {
            *ppvObject = null;
        }

        return HRESULT.E_NOINTERFACE;
    }
    public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
    public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
    public uint AddRef() => 1;
    public uint Release() => 1;

    public HRESULT GetTypeInfoCount(uint* pctinfo) => HRESULT.E_NOTIMPL;
    public HRESULT GetTypeInfo(uint iTInfo, uint lcid, ITypeInfo** ppTInfo) => HRESULT.E_NOTIMPL;
    public HRESULT GetIDsOfNames(Guid* riid, PWSTR* rgszNames, uint cNames, uint lcid, int* rgDispId) => HRESULT.E_NOTIMPL;
    public HRESULT Invoke(int dispIdMember, Guid* riid, uint lcid, DISPATCH_FLAGS dwFlags, DISPPARAMS* pDispParams, VARIANT* pVarResult, EXCEPINFO* pExcepInfo, uint* puArgErr) => HRESULT.E_NOTIMPL;
    public HRESULT Invoke(int dispIdMember, Guid* riid, uint lcid, ushort dwFlags, DISPPARAMS* pDispParams, VARIANT* pVarResult, EXCEPINFO* pExcepInfo, uint* puArgErr) => HRESULT.E_NOTIMPL;
}

internal unsafe partial struct IServiceProvider : IComIID, IVTable<IServiceProvider, IServiceProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT QueryService(Guid* guidService, Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;

    public interface Interface
    {
        HRESULT QueryService(Guid* guidService, Guid* riid, void** ppvObject);
    }
}

internal unsafe partial struct IDispatchEx : IComIID, IVTable<IDispatchEx, IDispatchEx.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("A6EF9860-C720-11D0-9337-00A0C90DCAA9");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetDispID(BSTR bstrName, uint grfdex, int* pid);
        HRESULT InvokeEx(int id, uint lcid, ushort wFlags, DISPPARAMS* pdp, VARIANT* pvarRes, EXCEPINFO* pei, IServiceProvider* pspCaller);
        HRESULT DeleteMemberByName(BSTR bstrName, uint grfdex);
        HRESULT DeleteMemberByDispID(int id);
        HRESULT GetMemberProperties(int id, uint grfdexFetch, FDEX_PROP_FLAGS* pgrfdex);
        HRESULT GetMemberName(int id, BSTR* pbstrName);
        HRESULT GetNextDispID(uint grfdex, int id, int* pid);
        HRESULT GetNameSpaceParent(IUnknown** ppunk);
    }
}

internal enum INVOKEKIND
{
    INVOKE_FUNC = 1,
    INVOKE_PROPERTYGET = 2,
    INVOKE_PROPERTYPUT = 4,
    INVOKE_PROPERTYPUTREF = 8,
}

internal enum VARKIND
{
    VAR_PERINSTANCE = 0,
    VAR_STATIC = 1,
    VAR_CONST = 2,
    VAR_DISPATCH = 3,
}

internal enum STGTY : uint
{
    STGTY_STORAGE = 1,
    STGTY_STREAM = 2,
    STGTY_LOCKBYTES = 3,
    STGTY_PROPERTY = 4,
}

internal enum STATFLAG : uint
{
    STATFLAG_DEFAULT = 0,
    STATFLAG_NONAME = 1,
    STATFLAG_NOOPEN = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ELEMDESC
{
    public TYPEDESC tdesc;
    public _Anonymous_e__Union Anonymous;

    [StructLayout(LayoutKind.Explicit)]
    public struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public void* idldesc;
        [FieldOffset(0)]
        public void* paramdesc;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TYPEATTR
{
    public Guid guid;
    public uint lcid;
    public uint dwReserved;
    public int memidConstructor;
    public int memidDestructor;
    public char* lpstrSchema;
    public uint cbSizeInstance;
    public TYPEKIND typekind;
    public ushort cFuncs;
    public ushort cVars;
    public ushort cInterfaces;
    public ushort cbAlignment;
    public ushort wTypeFlags;
    public ushort wMajorVerNum;
    public ushort wMinorVerNum;
    public TYPEDESC tdescAlias;
    public void* idldescType;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FUNCDESC
{
    public int memid;
    public int* lprgscode;
    public ELEMDESC* lprgelemdescParam;
    public int funckind;
    public INVOKEKIND invkind;
    public int callconv;
    public short cParams;
    public short cParamsOpt;
    public short oVft;
    public short cScodes;
    public ELEMDESC elemdescFunc;
    public ushort wFuncFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VARDESC
{
    public int memid;
    public char* lpstrSchema;
    public _Anonymous_e__Union Anonymous;
    public ELEMDESC elemdescVar;
    public VARFLAGS wVarFlags;
    public VARKIND varkind;

    [StructLayout(LayoutKind.Explicit)]
    public struct _Anonymous_e__Union
    {
        [FieldOffset(0)]
        public uint oInst;
        [FieldOffset(0)]
        public VARIANT* lpvarValue;
    }
}

internal unsafe partial struct ITypeInfo : IComIID, IVTable<ITypeInfo, ITypeInfo.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00020401-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetIDsOfNames(PWSTR* rgszNames, uint cNames, int* pMemId) => HRESULT.E_NOTIMPL;
    public HRESULT GetTypeAttr(TYPEATTR** ppTypeAttr) => HRESULT.E_NOTIMPL;
    public HRESULT GetTypeAttr(out TYPEATTR* ppTypeAttr) { fixed (TYPEATTR** p = &ppTypeAttr) return GetTypeAttr(p); }
    public void ReleaseTypeAttr(TYPEATTR* pTypeAttr) { }
    public HRESULT GetRefTypeInfo(uint hRefType, ITypeInfo** ppTInfo) => HRESULT.E_NOTIMPL;
    public HRESULT GetDocumentation(int memid, BSTR* pBstrName, BSTR* pBstrDocString, uint* pdwHelpContext, BSTR* pBstrHelpFile) => HRESULT.E_NOTIMPL;
    public HRESULT GetFuncDesc(uint index, FUNCDESC** ppFuncDesc) => HRESULT.E_NOTIMPL;
    public void ReleaseFuncDesc(FUNCDESC* pFuncDesc) { }
    public HRESULT GetVarDesc(uint index, VARDESC** ppVarDesc) => HRESULT.E_NOTIMPL;
    public void ReleaseVarDesc(VARDESC* pVarDesc) { }
    public uint AddRef() => 1;
    public uint Release() => 1;
    public HRESULT Invoke(void* pvInstance, int memid, ushort wFlags, DISPPARAMS* pDispParams, VARIANT* pVarResult, EXCEPINFO* pExcepInfo, uint* puArgErr) => HRESULT.E_NOTIMPL;
    public HRESULT Invoke(void* pvInstance, int memid, DISPATCH_FLAGS wFlags, DISPPARAMS* pDispParams, VARIANT* pVarResult, EXCEPINFO* pExcepInfo, uint* puArgErr) => HRESULT.E_NOTIMPL;
}

internal unsafe partial struct ITypeLib : IComIID, IVTable<ITypeLib, ITypeLib.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("00020402-0000-0000-C000-000000000046");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT GetTypeInfoOfGuid(Guid* pguid, ITypeInfo** ppTInfo) => HRESULT.E_NOTIMPL;
    public HRESULT GetTypeInfoOfGuid(in Guid guid, ITypeInfo** ppTInfo) { fixed (Guid* g = &guid) return GetTypeInfoOfGuid(g, ppTInfo); }
    public HRESULT GetTypeInfoOfGuid(in Guid guid, out ITypeInfo* ppTInfo)
    {
        fixed (Guid* pg = &guid)
        fixed (ITypeInfo** pp = &ppTInfo)
            return GetTypeInfoOfGuid(pg, pp);
    }
}
