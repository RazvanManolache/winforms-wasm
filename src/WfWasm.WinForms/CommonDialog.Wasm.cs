// Headless-dialog overlay: replaces the runtimes/winforms
// (System/Windows/Forms/Dialogs/CommonDialogs/CommonDialog.cs,
// Compile-Removed in the csproj). Upstream CommonDialog..ctor calls
// Marshal.GetFunctionPointerForDelegate for the dialog hook proc;
// generating the reverse-P/Invoke thunk aborts the browser runtime
// with a wasm out-of-bounds trap (func96, no managed exception, no
// first-chance). Same trap class as the menu-hook precedent
// (ToolStripManager.ModalMenuFilter.HookWasm.cs). The overlay keeps
// the delegate reference but never marshals it: no native dialog
// exists to invoke the hook. ShowDialog keeps the modal loop and
// RunDialog dispatch (whose stub fallbacks answer Cancel) but skips
// the owner-window subclass, which needs the same thunk.
// Regression: func96 OOB <- new FolderBrowserDialog() <-
// FileExplorer.RadRibbonForm1.InitializeComponent (silent boot death;
// diagram/erp same signature).
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
///  Specifies the base class used for displaying dialog boxes on the screen.
/// </summary>
[ToolboxItemFilter("System.Windows.Forms")]
public abstract class CommonDialog : Component
{
    private static readonly object s_helpRequestEvent = new();

    private nint _priorWindowProcedure;
    private bool _inShowDialog;
    private HWND _defaultControlHwnd;
    private readonly WNDPROC _hookProc;
    private readonly unsafe delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, nuint> _functionPointer;

    /// <summary>
    ///  Initializes a new instance of the <see cref="CommonDialog"/> class.
    /// </summary>
    public unsafe CommonDialog()
    {
        // Keep the delegate in a field to avoid having it collected prematurely.
        _hookProc = HookProcInternal;
        // Upstream marshals the delegate to a native hook proc here;
        // the thunk aborts the browser runtime, and no native dialog
        // exists to call it. Null forever.
        _functionPointer = null;
        _ = _hookProc;
    }

    [SRCategory(nameof(SR.CatData))]
    [Localizable(false)]
    [Bindable(true)]
    [SRDescription(nameof(SR.ControlTagDescr))]
    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    /// <summary>
    ///  Occurs when the user clicks the Help button on a common dialog box.
    /// </summary>
    [SRDescription(nameof(SR.CommonDialogHelpRequested))]
    public event EventHandler? HelpRequest
    {
        add => Events.AddHandler(s_helpRequestEvent, value);
        remove => Events.RemoveHandler(s_helpRequestEvent, value);
    }

    internal LRESULT HookProcInternal(HWND hWnd, uint msg, WPARAM wparam, LPARAM lparam)
        => (LRESULT)HookProc(hWnd, (int)msg, (nint)wparam, lparam);

    private protected unsafe delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, nuint> HookProcFunctionPointer
        => _functionPointer;

    /// <summary>
    ///  Defines the common dialog box hook procedure that is overridden to add specific
    ///  functionality to a common dialog box.
    /// </summary>
    protected virtual IntPtr HookProc(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam)
    {
        if (msg == (int)PInvokeCore.WM_INITDIALOG)
        {
            MoveToScreenCenter((HWND)hWnd);

            // Under some circumstances, the dialog does not initially focus on any
            // control. We fix that by explicitly setting focus ourselves.
            _defaultControlHwnd = (HWND)wparam;
            PInvoke.SetFocus((HWND)wparam);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    ///  Centers the given window on the screen. This method is used by the default
    ///  common dialog hook procedure to center the dialog on the screen before it
    ///  is shown.
    /// </summary>
    private protected static void MoveToScreenCenter(HWND hwnd)
    {
        PInvokeCore.GetWindowRect(hwnd, out var r);
        Rectangle screen = Screen.GetWorkingArea(Control.MousePosition);
        int x = screen.X + (screen.Width - r.right + r.left) / 2;
        int y = screen.Y + (screen.Height - r.bottom + r.top) / 3;
        PInvoke.SetWindowPos(
            hwnd,
            HWND.HWND_TOP,
            x, y, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    ///  Raises the <see cref="HelpRequest"/> event.
    /// </summary>
    protected virtual void OnHelpRequest(EventArgs e)
    {
        EventHandler? handler = (EventHandler?)Events[s_helpRequestEvent];
        handler?.Invoke(this, e);
    }

    private LRESULT OwnerWndProcInternal(HWND hWnd, uint msg, WPARAM wparam, LPARAM lparam)
        => (LRESULT)OwnerWndProc(hWnd, (int)msg, (nint)wparam, lparam);

    /// <summary>
    ///  Defines the owner window procedure that is overridden to add specific functionality to a common dialog box.
    /// </summary>
    protected virtual unsafe IntPtr OwnerWndProc(IntPtr hWnd, int msg, IntPtr wparam, IntPtr lparam)
    {
        return PInvokeCore.CallWindowProc((void*)_priorWindowProcedure, (HWND)hWnd, (uint)msg, (nuint)wparam, lparam);
    }

    /// <summary>
    ///  When overridden in a derived class, resets the properties of a common dialog
    ///  to their default values.
    /// </summary>
    public abstract void Reset();

    /// <summary>
    ///  When overridden in a derived class, specifies a common dialog box.
    /// </summary>
    protected abstract bool RunDialog(IntPtr hwndOwner);

    /// <summary>
    ///  Runs a common dialog box.
    /// </summary>
    public DialogResult ShowDialog() => ShowDialog(owner: null);

    /// <summary>
    ///  Runs a common dialog box, parented to the given IWin32Window.
    /// </summary>
    public DialogResult ShowDialog(IWin32Window? owner)
    {
        if (!SystemInformation.UserInteractive)
        {
            throw new InvalidOperationException(SR.CantShowModalOnNonInteractive);
        }

        // When the user attempts to open the same dialog twice in a quick succession,
        // the second dialog might start its initialization before the first dialog
        // had been fully initialized and became modal. Specifically, the file open dialog
        // allows the app to re-enter the message pump while it's initializing its folders
        // view.
        if (_inShowDialog)
        {
            // The previous subclass wasn't properly cleaned up,
            // the prior WinProc is ours, we don't want to subclass ourselves.
            return DialogResult.Cancel;
        }

        _inShowDialog = true;

        DialogResult result = DialogResult.Cancel;
        try
        {
            // Upstream subclasses the owner window here (needs the same
            // fatal delegate thunk); the browser has no native dialog to
            // parent, so RunDialog runs directly under the modal loop and
            // its stub fallbacks answer Cancel.
            using ThemingScope scope = new(Application.UseVisualStyles);
            Application.BeginModalMessageLoop();
            try
            {
                result = RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;
            }
            finally
            {
                Application.EndModalMessageLoop();
            }
        }
        finally
        {
            _inShowDialog = false;
        }

        return result;
    }
}
