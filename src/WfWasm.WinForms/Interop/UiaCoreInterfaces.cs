// Fork-owned: UI Automation core interfaces for Windows.Win32.UI.Accessibility.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.UI.Accessibility;

internal unsafe partial struct IAccessible : IComIID, IVTable<IAccessible, IAccessible.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("618736E0-3C3D-11CF-810C-00AA00389B71");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public HRESULT QueryInterface(Guid* riid, void** ppvObject) => HRESULT.E_NOTIMPL;
    public HRESULT QueryInterface(in Guid riid, out void* ppvObject) { fixed (void** p = &ppvObject) fixed (Guid* g = &riid) return QueryInterface(g, p); }
    public HRESULT QueryInterface(in Guid riid, void** ppvObject) { fixed (Guid* g = &riid) return QueryInterface(g, ppvObject); }
    public uint AddRef() => 1;
    public uint Release() => 1;

    public HRESULT get_accParent(IDispatch** ppdispParent) => HRESULT.E_NOTIMPL;
    public HRESULT get_accChildCount(int* pcountChildren) => HRESULT.E_NOTIMPL;
    public HRESULT get_accChildCount(out int pcountChildren) { fixed (int* p = &pcountChildren) return get_accChildCount(p); }
    public HRESULT get_accChild(VARIANT varChild, IDispatch** ppdispChild) => HRESULT.E_NOTIMPL;
    public HRESULT get_accName(VARIANT varChild, BSTR* pszName) => HRESULT.E_NOTIMPL;
    public HRESULT put_accName(VARIANT varChild, BSTR szName) => HRESULT.E_NOTIMPL;
    public HRESULT get_accValue(VARIANT varChild, BSTR* pszValue) => HRESULT.E_NOTIMPL;
    public HRESULT put_accValue(VARIANT varChild, BSTR szValue) => HRESULT.E_NOTIMPL;
    public HRESULT get_accDescription(VARIANT varChild, BSTR* pszDescription) => HRESULT.E_NOTIMPL;
    public HRESULT get_accRole(VARIANT varChild, VARIANT* pvarRole) => HRESULT.E_NOTIMPL;
    public HRESULT get_accState(VARIANT varChild, VARIANT* pvarState) => HRESULT.E_NOTIMPL;
    public HRESULT get_accHelp(VARIANT varChild, BSTR* pszHelp) => HRESULT.E_NOTIMPL;
    public HRESULT get_accHelpTopic(BSTR* pszHelpFile, VARIANT varChild, int* pidTopic) => HRESULT.E_NOTIMPL;
    public HRESULT get_accHelpTopic(out BSTR pszHelpFile, VARIANT varChild, out int pidTopic) { pszHelpFile = default; pidTopic = 0; return HRESULT.E_NOTIMPL; }
    public HRESULT get_accKeyboardShortcut(VARIANT varChild, BSTR* pszKeyboardShortcut) => HRESULT.E_NOTIMPL;
    public HRESULT get_accFocus(VARIANT* pvarChild) => HRESULT.E_NOTIMPL;
    public HRESULT get_accFocus(out VARIANT pvarChild) { fixed (VARIANT* p = &pvarChild) return get_accFocus(p); }
    public HRESULT get_accSelection(VARIANT* pvarChildren) => HRESULT.E_NOTIMPL;
    public HRESULT get_accSelection(out VARIANT pvarChildren) { fixed (VARIANT* p = &pvarChildren) return get_accSelection(p); }
    public HRESULT get_accDefaultAction(VARIANT varChild, BSTR* pszDefaultAction) => HRESULT.E_NOTIMPL;
    public HRESULT accSelect(int flagsSelect, VARIANT varChild) => HRESULT.E_NOTIMPL;
    public HRESULT accLocation(int* pxLeft, int* pyTop, int* pcxWidth, int* pcyHeight, VARIANT varChild) => HRESULT.E_NOTIMPL;
    public HRESULT accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, VARIANT varChild)
    {
        fixed (int* l = &pxLeft, t = &pyTop, w = &pcxWidth, h = &pcyHeight)
            return accLocation(l, t, w, h, varChild);
    }
    public HRESULT accNavigate(int navDir, VARIANT varStart, VARIANT* pvarEndUpAt) => HRESULT.E_NOTIMPL;
    public HRESULT accNavigate(int navDir, VARIANT varStart, out VARIANT pvarEndUpAt) { fixed (VARIANT* p = &pvarEndUpAt) return accNavigate(navDir, varStart, p); }
    public HRESULT accHitTest(int xLeft, int yTop, VARIANT* pvarChild) => HRESULT.E_NOTIMPL;
    public HRESULT accHitTest(int xLeft, int yTop, out VARIANT pvarChild) { fixed (VARIANT* p = &pvarChild) return accHitTest(xLeft, yTop, p); }
    public HRESULT accDoDefaultAction(VARIANT varChild) => HRESULT.E_NOTIMPL;

    public interface Interface
    {
        HRESULT get_accParent(IDispatch** ppdispParent);
        HRESULT get_accChildCount(int* pcountChildren);
        HRESULT get_accChild(VARIANT varChild, IDispatch** ppdispChild);
        HRESULT get_accName(VARIANT varChild, BSTR* pszName);
        HRESULT get_accValue(VARIANT varChild, BSTR* pszValue);
        HRESULT get_accDescription(VARIANT varChild, BSTR* pszDescription);
        HRESULT get_accRole(VARIANT varChild, VARIANT* pvarRole);
        HRESULT get_accState(VARIANT varChild, VARIANT* pvarState);
        HRESULT get_accHelp(VARIANT varChild, BSTR* pszHelp);
        HRESULT get_accHelpTopic(BSTR* pszHelpFile, VARIANT varChild, int* pidTopic);
        HRESULT get_accKeyboardShortcut(VARIANT varChild, BSTR* pszKeyboardShortcut);
        HRESULT get_accFocus(VARIANT* pvarChild);
        HRESULT get_accSelection(VARIANT* pvarChildren);
        HRESULT get_accDefaultAction(VARIANT varChild, BSTR* pszDefaultAction);
        HRESULT accSelect(int flagsSelect, VARIANT varChild);
        HRESULT accLocation(int* pxLeft, int* pyTop, int* pcxWidth, int* pcyHeight, VARIANT varChild);
        HRESULT accNavigate(int navDir, VARIANT varStart, VARIANT* pvarEndUpAt);
        HRESULT accHitTest(int xLeft, int yTop, VARIANT* pvarChild);
        HRESULT accDoDefaultAction(VARIANT varChild);
        HRESULT put_accName(VARIANT varChild, BSTR szName);
        HRESULT put_accValue(VARIANT varChild, BSTR szValue);
    }
}

internal unsafe partial struct IAccessibleEx : IComIID, IVTable<IAccessibleEx, IAccessibleEx.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("F67262CD-35E3-4E8A-AB07-A1654B0E881F");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetObjectForChild(int idChild, IAccessibleEx** pRetVal);
        HRESULT GetIAccessiblePair(IAccessible** ppAcc, int* pidChild);
        HRESULT GetRuntimeId(SAFEARRAY** pRetVal);
        HRESULT ConvertReturnedElement(IRawElementProviderSimple* pIn, IAccessibleEx** ppRetValOut);
    }
}

internal unsafe partial struct IRawElementProviderSimple : IComIID, IVTable<IRawElementProviderSimple, IRawElementProviderSimple.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("D6DD68D1-86FD-4332-8666-9ABEDF2DED2E");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT get_ProviderOptions(ProviderOptions* pRetVal);
        HRESULT GetPatternProvider(UIA_PATTERN_ID patternId, IUnknown** pRetVal);
        HRESULT GetPropertyValue(UIA_PROPERTY_ID propertyId, VARIANT* pRetVal);
        HRESULT get_HostRawElementProvider(IRawElementProviderSimple** pRetVal);
    }
}

internal unsafe partial struct IRawElementProviderFragment : IComIID, IVTable<IRawElementProviderFragment, IRawElementProviderFragment.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("F7063DA8-8359-439C-A072-E3E0DE29209E");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Navigate(NavigateDirection direction, IRawElementProviderFragment** pRetVal);
        HRESULT GetRuntimeId(SAFEARRAY** pRetVal);
        HRESULT get_BoundingRectangle(UiaRect* pRetVal);
        HRESULT GetEmbeddedFragmentRoots(SAFEARRAY** pRetVal);
        HRESULT SetFocus();
        HRESULT get_FragmentRoot(IRawElementProviderFragmentRoot** pRetVal);
    }
}

internal unsafe partial struct IRawElementProviderFragmentRoot : IComIID, IVTable<IRawElementProviderFragmentRoot, IRawElementProviderFragmentRoot.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("620E691C-EA96-4710-A011-EBEB4A5430E9");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT ElementProviderFromPoint(double x, double y, IRawElementProviderFragment** pRetVal);
        HRESULT GetFocus(IRawElementProviderFragment** pRetVal);
    }
}

internal unsafe partial struct IRawElementProviderHwndOverride : IComIID, IVTable<IRawElementProviderHwndOverride, IRawElementProviderHwndOverride.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("1DC7B570-1F54-4ABB-B482-49F44DE42974");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetOverrideProviderForHwnd(HWND hwnd, IRawElementProviderSimple** pRetVal);
    }
}
