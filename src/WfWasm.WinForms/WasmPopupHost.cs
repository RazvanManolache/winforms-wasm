// Host-driven top-level popup registry: parentless non-Form controls shown
// via ShowWindow (Telerik RadPopupControlBase menus, combo/date dropdowns,
// any WS_POPUP-style control). The canvas host paints tracked popups above
// dialogs and routes input to the topmost one. Pruning is by visibility,
// so ClosePopup/Hide needs no explicit pop. ToolStripDropDown is excluded
// (the host renders those through its own dropdown layer).
namespace System.Windows.Forms;

public static class WasmPopupHost
{
    private static readonly List<Control> s_popups = new();
    private static readonly object s_lock = new();

    public static void Track(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        bool added;
        lock (s_lock)
        {
            added = true;
            for (int i = 0; i < s_popups.Count; i++)
            {
                if (ReferenceEquals(s_popups[i], control))
                {
                    added = false;
                    break;
                }
            }
            UntrackLocked(control);
            s_popups.Add(control);
        }
        if (added)
        {
            try { Console.WriteLine($"[POPUP] Track {control.GetType().FullName} '{control.Name}' vis={control.Visible} at={control.Location} size={control.Width}x{control.Height} style={PopupStyleTag(control)}"); } catch { }
        }
    }

    // Creation-style tag for Track forensics (WS_CHILD phantoms vs WS_POPUP
    // reals); "nohandle" when the control has no canvas handle yet. Never
    // forces handle creation (IsHandleCreated guard).
    private static string PopupStyleTag(Control control)
    {
        try
        {
            if (control.IsHandleCreated)
                return "0x" + global::Windows.Win32.PInvokeCore.GetStyleForHwnd(control.Handle).ToString("x");
        }
        catch { }
        return "nohandle";
    }

    public static void Untrack(Control control)
    {
        lock (s_lock)
        {
            UntrackLocked(control);
        }
    }

    private static void UntrackLocked(Control control)
    {
        for (int i = s_popups.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(s_popups[i], control))
                s_popups.RemoveAt(i);
        }
    }

    // Live entries oldest-first; invisible, parented, disposed or disposing
    // are dropped here.
    public static Control[] Snapshot()
    {
        lock (s_lock)
        {
            for (int i = s_popups.Count - 1; i >= 0; i--)
            {
                Control popup = s_popups[i];
                bool vis = false, created = false, disposed = true, disposing = false;
                try { vis = popup.Visible; created = popup.IsHandleCreated; disposed = popup.IsDisposed; disposing = popup.Disposing; } catch { }
                // Visible defaults true on a never-shown control, so a live handle is required too.
                // Parented entries joined the form tree (painted via parents, e.g. PDN
                // controls created parentless then parented during boot).
                bool parented = false;
                try { parented = popup.Parent is not null; } catch { }
                // 0x0 entries were never laid out for show (pre-created
                // dropdown shells, shaped-form helpers); a 0-area window paints
                // and hit-tests nothing on desktop, so drop it. Only BOTH zero
                // counts: the real bookshelf dropdown tracks mid-show at 0x106
                // (SetBoundsCore sizes it after tracking), and pruning on
                // either dimension kills the milestone. A 0xN survivor paints
                // and hit-tests nothing until sized, so keeping it is safe.
                bool empty = true;
                try { empty = popup.Width <= 0 && popup.Height <= 0; } catch { }
                if (!vis || !created || disposed || disposing || parented || empty)
                    s_popups.RemoveAt(i);
            }
            return s_popups.ToArray();
        }
    }

    // Single popup filter shared by all funnels: parentless, not a dialog
    // Form, not a ToolStrip dropdown (own host layer), and not framework-
    // internal machinery (S.W.F-nested helpers like ToolStripScrollButton's
    // StickyLabel are parentless+visible but never popups; tracking them
    // poisons snapshots and swallows all mouse input).
    internal static bool IsPopupCandidate(Control control)
    {
        if (control.Parent is not null || control is Form || control is ToolStripDropDown)
            return false;
        Type t = control.GetType();
        if (t.IsNested && t.Assembly == typeof(Control).Assembly)
            return false;
        // WS_CHILD windows are clipped to their parent and can never be
        // top-level popups (eval8 RTE editor phantom: style 0x56010000).
        // Real popups carry WS_POPUP (bookshelf dropdown: 0x82000000).
        // Handleless controls are rejected: the style is unknowable before
        // creation, and the handle-created funnel re-evaluates with the
        // seeded style right after. Never forces handle creation.
        try
        {
            if (!control.IsHandleCreated)
                return false;
            nint style = global::Windows.Win32.PInvokeCore.GetStyleForHwnd(control.Handle);
            if (style != 0 && (style & (nint)global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE.WS_CHILD) != 0)
                return false;
        }
        catch { }
        return true;
    }

    // Visibility-transition funnel (called by the SetVisibleCore hook):
    // candidates join on show, leave on hide. A reject must UNTRACK, not
    // just skip: entries are provisional (e.g. tracked handleless before
    // the style is knowable), and skip-and-stay would strand phantoms.
    public static void NoteVisibleTransition(Control control, bool shown)
    {
        try
        {
            if (shown && IsPopupCandidate(control))
                Track(control);
            else
                Untrack(control);
        }
        catch { }
    }

    // ShowWindow funnel (called by NativeWindow.WasmNotifyShowWindow):
    // Telerik popups call ShowWindow directly on their handle rather than
    // going through Control.Show, so the registry keys off the same call.
    // Public so tests can drive it with a plain control handle.
    public static void NoteShowWindow(IntPtr handle, bool shown)
    {
        try
        {
            if (Control.FromHandle(handle) is { } control)
            {
                if (shown && IsPopupCandidate(control))
                    Track(control);
                else
                    Untrack(control);
            }
        }
        catch { }
    }

    // Handle-creation funnel (called by NativeWindow.DispatchCreateWindow):
    // popups that never call ShowWindow (WS_CHILD style, default-visible
    // newborns positioned via SetBoundsCore) still create handles. Track
    // visible parentless non-Forms here; hidden ones join through the
    // SetVisibleCore transition hook when shown.
    public static void NoteHandleCreated(IntPtr handle)
    {
        try
        {
            if (Control.FromHandle(handle) is { } control)
            {
                if (control.Visible && IsPopupCandidate(control))
                    Track(control);
                else
                    Untrack(control);
            }
        }
        catch { }
    }

    public static void ResetForNewHost()
    {
        lock (s_lock)
        {
            s_popups.Clear();
        }
    }
}
