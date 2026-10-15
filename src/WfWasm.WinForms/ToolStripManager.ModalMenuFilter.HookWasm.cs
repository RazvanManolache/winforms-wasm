// Browser overlay for the menu-modal message hook (replaces the upstream
// ToolStripManager.ModalMenuFilter.HostedWindowsFormsMessageHook, excluded
// in the project file).

namespace System.Windows.Forms;

public static partial class ToolStripManager
{
    internal partial class ModalMenuFilter
    {
        // Browser WASM has no OS message queue: installing a WH_GETMESSAGE
        // hook (GetFunctionPointerForDelegate + SetWindowsHookEx) aborts the
        // runtime with a wasm out-of-bounds trap (observed opening the File
        // menu). The canvas host drives menu input directly, so the hook is
        // a permanent no-op here; only the settable flag remains.
        private class HostedWindowsFormsMessageHook
        {
            public bool HookMessages { get; set; }
        }
    }
}
