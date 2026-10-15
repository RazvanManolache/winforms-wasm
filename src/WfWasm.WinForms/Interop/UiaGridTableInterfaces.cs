// Fork-owned: UI Automation Table and Grid pattern interfaces.

using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;

namespace Windows.Win32.UI.Accessibility;

internal unsafe partial struct ITableProvider : IComIID, IVTable<ITableProvider, ITableProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("98431626-4DC3-484B-9B7E-142F452132E6");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetRowHeaders(SAFEARRAY** pRetVal);
        HRESULT GetColumnHeaders(SAFEARRAY** pRetVal);
        HRESULT get_RowOrColumnMajor(RowOrColumnMajor* pRetVal);
    }
}

internal unsafe partial struct ITableItemProvider : IComIID, IVTable<ITableItemProvider, ITableItemProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("B9734A03-E4AA-4FF7-9EE4-141160E7327E");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetRowHeaderItems(SAFEARRAY** pRetVal);
        HRESULT GetColumnHeaderItems(SAFEARRAY** pRetVal);
    }
}

internal unsafe partial struct IGridProvider : IComIID, IVTable<IGridProvider, IGridProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("B17D6187-0907-464B-A168-0EF14FA79792");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT GetItem(int row, int column, IRawElementProviderSimple** pRetVal);
        HRESULT get_RowCount(int* pRetVal);
        HRESULT get_ColumnCount(int* pRetVal);
    }
}

internal unsafe partial struct IGridItemProvider : IComIID, IVTable<IGridItemProvider, IGridItemProvider.Vtbl>
{
    private readonly void** _vtable;
    public unsafe struct Vtbl { }
    private static readonly Guid s_guid = new("D02541F1-FB81-4D64-AE32-F520F8A6DBD1");
    public static ref readonly Guid IID_Guid => ref s_guid;
    static ref readonly Guid IComIID.Guid => ref s_guid;
    public static void PopulateVTable(Vtbl* vtable) { }

    public interface Interface
    {
        HRESULT get_Row(int* pRetVal);
        HRESULT get_Column(int* pRetVal);
        HRESULT get_RowSpan(int* pRetVal);
        HRESULT get_ColumnSpan(int* pRetVal);
        HRESULT get_ContainingGrid(IRawElementProviderSimple** pRetVal);
    }
}
