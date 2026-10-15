// Fork-owned: DataObject partial class resolving diamond DIM implementation.

using Windows.Win32.System.Com;

namespace System.Windows.Forms;

public unsafe partial class DataObject
{
    ComInterfaceTable IManagedWrapper.GetComInterfaceTable() =>
        ((IManagedWrapper<global::Windows.Win32.System.Com.IDataObject>)this).GetComInterfaceTable();
}
