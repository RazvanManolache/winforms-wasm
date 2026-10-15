// Fork-owned: UI Automation pattern interfaces for Windows.Win32.UI.Accessibility.

using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;

namespace Windows.Win32.UI.Accessibility;

internal unsafe partial struct ILegacyIAccessibleProvider : IComIID, IVTable<ILegacyIAccessibleProvider, ILegacyIAccessibleProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("E44C350B-97FE-4533-A700-E52664182732");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Select(int flagsSelect);
        HRESULT DoDefaultAction();
        HRESULT SetValue(PCWSTR szValue);
        HRESULT GetIAccessible(IAccessible** ppAccessible);
        HRESULT get_ChildId(int* pRetVal);
        HRESULT get_Name(BSTR* pszName);
        HRESULT get_Value(BSTR* pszValue);
        HRESULT get_Description(BSTR* pszDescription);
        HRESULT get_Role(uint* pdwRole);
        HRESULT get_State(uint* pdwState);
        HRESULT get_Help(BSTR* pszHelp);
        HRESULT get_KeyboardShortcut(BSTR* pszKeyboardShortcut);
        HRESULT GetSelection(SAFEARRAY** pvarSelectedChildren);
        HRESULT get_DefaultAction(BSTR* pszDefaultAction);
    }
}

internal unsafe partial struct IInvokeProvider : IComIID, IVTable<IInvokeProvider, IInvokeProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("54FCB24B-E18E-47A2-B4D3-ECCBE77599A2");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Invoke();
    }
}

internal unsafe partial struct IValueProvider : IComIID, IVTable<IValueProvider, IValueProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("C63657F6-8850-470E-945F-79F555D20248");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT SetValue(PCWSTR val);
        HRESULT get_Value(BSTR* pRetVal);
        HRESULT get_IsReadOnly(BOOL* pRetVal);
    }
}

internal unsafe partial struct IRangeValueProvider : IComIID, IVTable<IRangeValueProvider, IRangeValueProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("36DC7A9F-04E1-4F25-A8AB-5147F756EB66");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT SetValue(double val);
        HRESULT get_Value(double* pRetVal);
        HRESULT get_IsReadOnly(BOOL* pRetVal);
        HRESULT get_Maximum(double* pRetVal);
        HRESULT get_Minimum(double* pRetVal);
        HRESULT get_LargeChange(double* pRetVal);
        HRESULT get_SmallChange(double* pRetVal);
    }
}

internal unsafe partial struct IExpandCollapseProvider : IComIID, IVTable<IExpandCollapseProvider, IExpandCollapseProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("D8473820-2799-478F-8221-37CCD232822B");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Expand();
        HRESULT Collapse();
        HRESULT get_ExpandCollapseState(ExpandCollapseState* pRetVal);
    }
}

internal unsafe partial struct IToggleProvider : IComIID, IVTable<IToggleProvider, IToggleProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("56D00BD0-C4F4-433C-A836-9A3AB2505BB2");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Toggle();
        HRESULT get_ToggleState(ToggleState* pRetVal);
    }
}

internal unsafe partial struct IScrollProvider : IComIID, IVTable<IScrollProvider, IScrollProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("B38B8077-1FF3-4ECD-9F0E-EB35DC36D192");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount);
        HRESULT SetScrollPercent(double horizontalPercent, double verticalPercent);
        double HorizontalScrollPercent { get; }
        double VerticalScrollPercent { get; }
        double HorizontalViewSize { get; }
        double VerticalViewSize { get; }
        BOOL HorizontallyScrollable { get; }
        BOOL VerticallyScrollable { get; }
    }
}

internal unsafe partial struct IScrollItemProvider : IComIID, IVTable<IScrollItemProvider, IScrollItemProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("2360C714-4BF8-4B26-BA29-7B104E8FF87F");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT ScrollIntoView();
    }
}

internal unsafe partial struct ISelectionProvider : IComIID, IVTable<ISelectionProvider, ISelectionProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("FB8EAD28-3C65-4F5F-9C20-1F10B2F9C9D4");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetSelection(SAFEARRAY** pRetVal);
        HRESULT get_CanSelectMultiple(BOOL* pRetVal);
        HRESULT get_IsSelectionRequired(BOOL* pRetVal);
    }
}

internal unsafe partial struct ISelectionItemProvider : IComIID, IVTable<ISelectionItemProvider, ISelectionItemProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("2CAD8D05-43E8-4FF4-A571-332D60216D88");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Select();
        HRESULT AddToSelection();
        HRESULT RemoveFromSelection();
        HRESULT get_IsSelected(BOOL* pRetVal);
        HRESULT get_SelectionContainer(IRawElementProviderSimple** pRetVal);
    }
}

internal unsafe partial struct IMultipleViewProvider : IComIID, IVTable<IMultipleViewProvider, IMultipleViewProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("8D253C91-1756-4EAB-933D-503E42940E80");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetSupportedViews(SAFEARRAY** pRetVal);
        HRESULT GetViewName(int viewId, BSTR* pRetVal);
        HRESULT SetCurrentView(int viewId);
        HRESULT get_CurrentView(int* pRetVal);
    }
}
