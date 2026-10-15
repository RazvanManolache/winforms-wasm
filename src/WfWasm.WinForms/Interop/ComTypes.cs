// Fork-owned: Common COM types for Windows.Win32.System.Com.

using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.System.Com;

[Flags]
internal enum DISPATCH_FLAGS : ushort
{
    DISPATCH_METHOD = 0x0001,
    DISPATCH_PROPERTYGET = 0x0002,
    DISPATCH_PROPERTYPUT = 0x0004,
    DISPATCH_PROPERTYPUTREF = 0x0008
}

internal unsafe struct DISPPARAMS
{
    public VARIANT* rgvarg;
    public int* rgdispidNamedArgs;
    public uint cArgs;
    public uint cNamedArgs;
}

internal unsafe struct EXCEPINFO
{
    public ushort wCode;
    public ushort wReserved;
    public BSTR bstrSource;
    public BSTR bstrDescription;
    public BSTR bstrHelpFile;
    public uint dwHelpContext;
    public void* pvReserved;
    public void* pfnDeferredFillIn;
    public int scode;
}

internal unsafe struct STATSTG
{
    public PWSTR pwcsName;
    public uint type;
    public ulong cbSize;
    public FILETIME mtime;
    public FILETIME ctime;
    public FILETIME atime;
    public STGM grfMode;
    public uint grfLocksSupported;
    public Guid clsid;
    public uint grfStateBits;
    public uint reserved;
}

internal unsafe partial struct IErrorLog : IComIID, IVTable<IErrorLog, IErrorLog.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("3127CA40-446E-11CE-8135-00AA004BB851");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT AddError(PCWSTR pszPropName, in EXCEPINFO pExcepInfo) => HRESULT.S_OK;
    public HRESULT AddError(string? pszPropName, in EXCEPINFO pExcepInfo) => HRESULT.S_OK;
    public HRESULT AddError(PCWSTR pszPropName, EXCEPINFO* pExcepInfo) => HRESULT.S_OK;
    public HRESULT AddError(string? pszPropName, EXCEPINFO* pExcepInfo) => HRESULT.S_OK;

    public interface Interface
    {
        HRESULT AddError(PCWSTR pszPropName, EXCEPINFO* pExcepInfo);
    }
}
