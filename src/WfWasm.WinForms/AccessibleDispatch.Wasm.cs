// Fork-owned inert IDispatch base for IAccessible (replaces the upstream
// AccessibleDispatch, Compile-Removed above). Upstream builds its dispatch
// from oleacc's registered typelib (LoadRegTypeLib); no registry/COM
// exists in the browser, so the static TypeInfo initializer threw and
// poisoned every AccessibleObject constructor process-wide (menu/dropdown
// opens died creating bar-item accessible objects).
//
// No screen reader consumes COM dispatch headless: every dispatch entry
// answers inertly and construction always succeeds, while the managed
// accessibility surface (names/roles/states on AccessibleObject itself)
// keeps working untouched.
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.UI.Accessibility;

internal abstract unsafe class AccessibleDispatch : IDispatch.Interface, IDispatchEx.Interface
{
    public AccessibleDispatch() { }

    public AccessibleDispatch(IAccessible.Interface instance) { }

    HRESULT IDispatch.Interface.GetTypeInfoCount(uint* pctinfo) => HRESULT.E_NOTIMPL;

    HRESULT IDispatch.Interface.GetTypeInfo(uint iTInfo, uint lcid, ITypeInfo** ppTInfo) => HRESULT.E_NOTIMPL;

    HRESULT IDispatch.Interface.GetIDsOfNames(Guid* riid, PWSTR* rgszNames, uint cNames, uint lcid, int* rgDispId)
        => HRESULT.E_NOTIMPL;

    HRESULT IDispatch.Interface.Invoke(
        int dispIdMember,
        Guid* riid,
        uint lcid,
        DISPATCH_FLAGS wFlags,
        DISPPARAMS* pDispParams,
        VARIANT* pVarResult,
        EXCEPINFO* pExcepInfo,
        uint* pArgErr)
        => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.GetDispID(BSTR bstrName, uint grfdex, int* pid) => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.InvokeEx(
        int id,
        uint lcid,
        ushort wFlags,
        DISPPARAMS* pdp,
        VARIANT* pvarRes,
        EXCEPINFO* pei,
        global::Windows.Win32.System.Com.IServiceProvider* pspCaller)
        => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.DeleteMemberByName(BSTR bstrName, uint grfdex) => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.DeleteMemberByDispID(int id) => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.GetMemberProperties(int id, uint grfdexFetch, FDEX_PROP_FLAGS* pgrfdex)
        => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.GetMemberName(int id, BSTR* pbstrName) => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.GetNextDispID(uint grfdex, int id, int* pid) => HRESULT.E_NOTIMPL;

    HRESULT IDispatchEx.Interface.GetNameSpaceParent(IUnknown** ppunk) => HRESULT.E_NOTIMPL;
}
