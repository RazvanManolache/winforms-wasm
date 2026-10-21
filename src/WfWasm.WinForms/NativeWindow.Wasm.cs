// Fork-owned: synchronous WM_SHOWWINDOW delivery for canvas-hosted windows.
// Native ShowWindow sends WM_SHOWWINDOW to the window before returning, and
// Control.WmShowWindow is what sets the visible bit and runs CreateControl
// over the visible subtree (Form.Load then fires from Form.OnCreateControl).
// The fork's ShowWindow stub used to swallow the call, so the whole show
// lifecycle was skipped: child controls were never "created" and load-gated
// frameworks (Telerik RadControl.IsLoaded) stayed unloaded forever, which
// left e.g. RadDock unable to activate its first document until a click
// forced activation through a path that bypasses the load gates.

using System.Collections.Concurrent;
using System.Reflection;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace System.Windows.Forms;

public partial class NativeWindow
{
    /// <summary>
    ///  Delivers WM_SHOWWINDOW to the window registered for <paramref name="hWnd"/>,
    ///  mirroring the synchronous notification native ShowWindow performs.
    ///  No-op when the handle is not one of our windows.
    /// </summary>
    internal static void WasmNotifyShowWindow(HWND hWnd, bool shown)
    {
        if (FromHandle((IntPtr)hWnd.Value) is not { } window)
        {
            return;
        }

        Message m = Message.Create((IntPtr)hWnd.Value, (int)PInvokeCore.WM_SHOWWINDOW, shown ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
        try
        {
            window.WndProc(ref m);
        }
        catch (Exception e)
        {
            // Native Callback routes WndProc failures to the thread-exception
            // handler instead of letting them escape out of ShowWindow. The
            // fork's exception-dialog path is not browser-safe (its icon load
            // fails), so surface the failure on the console and keep showing.
            Console.WriteLine($"[WasmNotifyShowWindow] {e}");
        }
        // Top-level popups (Telerik menus, combo dropdowns) are parentless
        // non-Form controls: track them for the host paint layer, which
        // otherwise only knows the active form, tracked dialogs and
        // ToolStrip dropdowns. WndProc above already flipped the visible
        // bit, so visibility reads are current here.
        WasmPopupHost.NoteShowWindow((IntPtr)hWnd.Value, shown);
    }

    /// <summary>
    ///  Delivers WM_DESTROY to the window registered for <paramref name="hWnd"/>,
    ///  mirroring the synchronous notification native DestroyWindow performs.
    ///  No-op when the handle is not one of our windows.
    ///  Control.WmDestroy is what stashes Text into _text on the way down
    ///  ("see wmDestroy" in the fork); without this delivery, any handle
    ///  recreation re-seeds an empty caption and the text is silently lost
    ///  (BugTracker dock captions: Projects/Bugs/Details).
    /// </summary>
    /// <summary>
    ///  Delivers a synthetic WM_PAINT to the NativeWindow registered for
    ///  <paramref name="hWnd"/> (no-op when none is attached). Desktop
    ///  painters that subclass a control (Telerik's TextBoxAdaptor, which
    ///  paints spell squiggles via Graphics.FromHwnd in its AdapterPaint
    ///  handler) rely on the EDIT window receiving WM_PAINT; headless
    ///  controls never produce one. The host calls this for dirty controls
    ///  just before compositing their overlay, so the subclass over-paints
    ///  the same frame the control rastered (desktop order: control first,
    ///  subclass second).
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> s_wndProcOverride = new();

    private static bool OverridesWndProc(Type type)
        => s_wndProcOverride.GetOrAdd(type, t =>
            t.GetMethod("WndProc", BindingFlags.NonPublic | BindingFlags.Instance)?.DeclaringType != typeof(Control));

    public static void WasmNotifyPaint(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || FromHandle(hWnd) is not { } window)
        {
            return;
        }

        // Plain windows with no WndProc override need no synthetic dispatch:
        // the host already rastered them through OnPaint this frame, and the
        // dispatch would just re-run the same chain (double OnPaint per frame
        // for every dirty control). Subclassed windows (adaptor NativeWindows,
        // nested in their own types) and WndProc-overriding controls keep the
        // dispatch: their chain over-paints the overlay layer.
        if (window.GetType().DeclaringType == typeof(Control)
            && Control.FromHandle(hWnd) is { } control
            && !OverridesWndProc(control.GetType()))
        {
            return;
        }

        Message m = Message.Create(hWnd, (int)PInvokeCore.WM_PAINT, IntPtr.Zero, IntPtr.Zero);
        try
        {
            window.WndProc(ref m);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[WasmNotifyPaint] {e}");
        }
    }

    internal static void WasmNotifyDestroyWindow(HWND hWnd)
    {
        if (FromHandle((IntPtr)hWnd.Value) is not { } window)
        {
            return;
        }

        Message m = Message.Create((IntPtr)hWnd.Value, (int)PInvokeCore.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
        try
        {
            window.WndProc(ref m);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[WasmNotifyDestroyWindow] {e}");
        }
    }
}
