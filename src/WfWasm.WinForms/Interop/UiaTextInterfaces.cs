// Fork-owned: UI Automation Text pattern interfaces.

using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.Variant;

namespace Windows.Win32.UI.Accessibility;

internal unsafe partial struct ITextProvider : IComIID, IVTable<ITextProvider, ITextProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("3589100E-203F-4247-9E81-BB3656BC645C");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        ITextRangeProvider* DocumentRange { get; }
        SupportedTextSelection SupportedTextSelection { get; }
        HRESULT GetSelection(SAFEARRAY** pRetVal);
        HRESULT GetVisibleRanges(SAFEARRAY** pRetVal);
        HRESULT RangeFromChild(IRawElementProviderSimple* childElement, ITextRangeProvider** pRetVal);
        HRESULT RangeFromPoint(UiaPoint point, ITextRangeProvider** pRetVal);
    }
}

internal unsafe partial struct ITextProvider2 : IComIID, IVTable<ITextProvider2, ITextProvider2.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("0DC63A4B-387C-43E7-995E-05001F3E56E6");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        ITextRangeProvider* DocumentRange { get; }
        SupportedTextSelection SupportedTextSelection { get; }
        HRESULT GetSelection(SAFEARRAY** pRetVal);
        HRESULT GetVisibleRanges(SAFEARRAY** pRetVal);
        HRESULT RangeFromChild(IRawElementProviderSimple* childElement, ITextRangeProvider** pRetVal);
        HRESULT RangeFromPoint(UiaPoint point, ITextRangeProvider** pRetVal);
        HRESULT GetCaretRange(BOOL* isActive, ITextRangeProvider** pRetVal);
        HRESULT RangeFromAnnotation(IRawElementProviderSimple* annotationElement, ITextRangeProvider** pRetVal);
    }
}

internal unsafe partial struct ITextRangeProvider : IComIID, IVTable<ITextRangeProvider, ITextRangeProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("5347DE70-BAB3-4834-AAC2-3A01E780D3B0");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT Clone(ITextRangeProvider** pRetVal);
        HRESULT Compare(ITextRangeProvider* range, BOOL* pRetVal);
        HRESULT CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider* targetRange, TextPatternRangeEndpoint targetEndpoint, int* pRetVal);
        HRESULT ExpandToEnclosingUnit(TextUnit unit);
        HRESULT FindAttribute(UIA_TEXTATTRIBUTE_ID attributeId, VARIANT val, BOOL backward, ITextRangeProvider** pRetVal);
        HRESULT FindText(BSTR text, BOOL backward, BOOL ignoreCase, ITextRangeProvider** pRetVal);
        HRESULT GetAttributeValue(UIA_TEXTATTRIBUTE_ID attributeId, VARIANT* pRetVal);
        HRESULT GetBoundingRectangles(SAFEARRAY** pRetVal);
        HRESULT GetEnclosingElement(IRawElementProviderSimple** pRetVal);
        HRESULT GetText(int maxLength, BSTR* pRetVal);
        HRESULT Move(TextUnit unit, int count, int* pRetVal);
        HRESULT MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count, int* pRetVal);
        HRESULT MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider* targetRange, TextPatternRangeEndpoint targetEndpoint);
        HRESULT Select();
        HRESULT AddToSelection();
        HRESULT RemoveFromSelection();
        HRESULT ScrollIntoView(BOOL alignToTop);
        HRESULT GetChildren(SAFEARRAY** pRetVal);
    }
}
