// Fork-owned replacement for the upstream generic RegisterDragDrop wrapper.
// The upstream wrapper marshals the drop target through
// ComHelpers.TryGetComScope, which throws PlatformNotSupportedException on
// WASM; Control.OnHandleCreated then fails ("DragDrop registration did not
// succeed") for every AllowDrop control whose handle gets created. OLE
// drag-drop registration has no headless equivalent, so it is a no-op
// success here; the host owns real drag sources.

using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

namespace Windows.Win32;

internal static partial class PInvokeCore
{
    /// <inheritdoc cref="RegisterDragDrop(HWND, IDropTarget*)"/>
    public static unsafe HRESULT RegisterDragDrop<T>(T hwnd, IDropTarget.Interface pDropTarget)
        where T : IHandle<HWND>
        => HRESULT.S_OK;
}
