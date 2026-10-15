// Host-driven top-window registry shared by the fork's Show/ShowDialog
// WASM paths and the canvas host's paint + input routing.
//
// Browser WASM runs on one thread, so no nested RunDialog pump can ever
// receive browser events. Show/ShowDialog therefore present the form
// through the host instead of throwing: the form is shown visibly and
// tracked here, the host paints tracked windows above everything and
// captures input for the topmost modal one, and ShowDialog returns
// immediately. Pruning is by visibility: closing (or disposing) a
// tracked form drops it on the next snapshot.

namespace System.Windows.Forms;

public sealed class WasmShownWindow
{
    internal WasmShownWindow(Form form, bool modal)
    {
        Form = form;
        IsModal = modal;
    }

    public Form Form { get; }
    public bool IsModal { get; }
}

public static class WasmDialogHost
{
    // The fork never owns an OS message queue (browser canvas and
    // headless tests alike): Show/ShowDialog/Close always take the
    // host-presentation branches in Form.cs instead of nested pumps.
    // This intentionally ignores SystemInformation.UserInteractive,
    // which is true: the session IS interactive (mouse/keyboard drive
    // the app) but has no Win32 queue. Keying those branches off
    // UserInteractive broke MessageBox/CommonDialog (they must see an
    // interactive session) and would hang ShowDialog in a nested pump.
    internal static bool IsHeadlessSession => true;

    private static readonly List<WasmShownWindow> s_windows = new();
    private static readonly object s_lock = new();
    private static long s_turn;

    // Abort-deferral: ShowDialog returns None immediately on WASM, so
    // PDN's modal flows abort synchronously and dispose the dialog in
    // the same browser-event turn it was presented. Effect config
    // dialogs must survive that abort (the user then drives them, and
    // closing one replays the action with the recorded result). The
    // mark records the presenting turn; disposal defers only within
    // that same turn, so later user closes stay real. Desktop never
    // marks (NotePresented runs only in the WASM ShowDialog branch),
    // so desktop disposal is untouched.
    private sealed class PresentedMark
    {
        public long Turn;
        public bool Deferred;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, PresentedMark> s_presented = new();

    public static void AdvanceTurn() => System.Threading.Interlocked.Increment(ref s_turn);

    public static void NotePresented(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        // One live dialog per type: a re-presented effect dialog
        // supersedes its still-open orphan (desktop parity: a modal
        // blocks, so a second one is unreachable). The orphan's mark
        // is dropped first so its disposal is real even when both
        // presented in the same turn. Orphans are collected under
        // the lock but disposed outside it: disposal runs user event
        // code that may re-enter this registry.
        System.Collections.Generic.List<Form> superseded = new();
        lock (s_lock)
        {
            string? typeName = null;
            try { typeName = form.GetType().FullName; } catch { }
            if (typeName is not null)
            {
                foreach (WasmShownWindow w in s_windows.ToArray())
                {
                    try
                    {
                        if (!ReferenceEquals(w.Form, form)
                            && w.Form.GetType().FullName == typeName
                            && IsDeferred(w.Form))
                            superseded.Add(w.Form);
                    }
                    catch { }
                }
            }
        }
        foreach (Form orphan in superseded)
        {
            try { s_presented.Remove(orphan); orphan.Dispose(); } catch { }
        }
        s_presented.AddOrUpdate(form, new PresentedMark { Turn = System.Threading.Volatile.Read(ref s_turn) });
    }

    public static bool DeferAbortDispose(Form form)
    {
        if (!s_presented.TryGetValue(form, out PresentedMark? mark) || mark is null)
            return false;
        if (mark.Turn != System.Threading.Volatile.Read(ref s_turn))
            return false;
        if (!IsEffectConfigDialog(form))
            return false;
        mark.Deferred = true;
        return true;
    }

    // Sync abort-Hide: PDN's abort also calls configDialog.Hide() in the
    // presenting turn. Same gate as disposal (mark + same turn + effect
    // policy); later user closes hide for real.
    public static bool DeferAbortHide(Form form)
    {
        if (!s_presented.TryGetValue(form, out PresentedMark? mark) || mark is null)
            return false;
        if (mark.Turn != System.Threading.Volatile.Read(ref s_turn))
            return false;
        if (!IsEffectConfigDialog(form))
            return false;
        mark.Deferred = true;
        return true;
    }

    // Posted abort-Close: PDN also posts ecf.Close() to its sync context,
    // which lands in a LATER turn (past the Dispose gate above) with no
    // result chosen. A user close always carries OK/Cancel at entry (the
    // button sets it before Close runs; the X box travels the WM_CLOSE
    // message path, not Close), so None-at-entry plus a presented mark
    // is unambiguously PDN's abort: skip it and stay open.
    public static bool DeferAbortClose(Form form)
    {
        if (!s_presented.TryGetValue(form, out PresentedMark? mark) || mark is null)
            return false;
        try
        {
            if (form.DialogResult != DialogResult.None)
                return false;
        }
        catch { return false; }
        if (!IsEffectConfigDialog(form))
            return false;
        if (mark.Turn != System.Threading.Volatile.Read(ref s_turn) && !mark.Deferred)
            return false;
        mark.Deferred = true;
        return true;
    }

    public static bool IsDeferred(Form form)
        => s_presented.TryGetValue(form, out PresentedMark? mark) && mark is not null && mark.Deferred;

    private static bool IsEffectConfigDialog(Form form)
    {
        // Two parallel PDN hierarchies: legacy EffectConfigDialog(`2)
        // and GPU EffectConfigForm(`2 via EffectConfigForm2). Match the
        // non-generic root of either (generic arity suffixes never match
        // a plain full name, so the roots are the stable anchors).
        try
        {
            for (Type? t = form.GetType(); t is not null && t != typeof(object); t = t.BaseType)
            {
                if (t.FullName == "PaintDotNet.Effects.EffectConfigDialog"
                    || t.FullName == "PaintDotNet.Effects.EffectConfigForm")
                    return true;
            }
        }
        catch { }
        return false;
    }

    public static void Track(Form form, bool modal)
    {
        ArgumentNullException.ThrowIfNull(form);
        lock (s_lock)
        {
            UntrackLocked(form);
            s_windows.Add(new WasmShownWindow(form, modal));
        }
        // TEMP-DLG: trace dialog presentation (retire before final).
        try { Console.WriteLine($"[TMPDLG] Track {form.GetType().FullName} '{form.Text}' vis={form.Visible} modal={modal}"); } catch { }
    }

    public static void Untrack(Form form)
    {
        lock (s_lock)
        {
            UntrackLocked(form);
        }
    }

    private static void UntrackLocked(Form form)
    {
        for (int i = s_windows.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(s_windows[i].Form, form))
                s_windows.RemoveAt(i);
        }
    }

    // Live entries oldest-first; invisible, disposed or disposing forms
    // are dropped here so Close() needs no explicit pop.
    // TEMP-DLG: first-prune notice per form type (retire before final).
    private static readonly HashSet<string> s_pruneNoticed = new();
    public static WasmShownWindow[] Snapshot()
    {
        lock (s_lock)
        {
            for (int i = s_windows.Count - 1; i >= 0; i--)
            {
                Form form = s_windows[i].Form;
                bool vis = false, disposed = true, disposing = false;
                string tn = "";
                try { tn = form.GetType().FullName ?? ""; vis = form.Visible; disposed = form.IsDisposed; disposing = form.Disposing; } catch { }
                if (!vis || disposed || disposing)
                {
                    lock (s_pruneNoticed)
                    {
                        if (s_pruneNoticed.Add(tn + "|" + (!vis ? "inv" : "disp")))
                        {
                            try { Console.WriteLine($"[TMPDLG] Prune {tn} vis={vis} disposed={disposed} disposing={disposing}"); } catch { }
                        }
                    }
                    s_windows.RemoveAt(i);
                }
            }

            return s_windows.ToArray();
        }
    }

    public static bool TopModal(out Form? form)
    {
        lock (s_lock)
        {
            for (int i = s_windows.Count - 1; i >= 0; i--)
            {
                Form candidate = s_windows[i].Form;
                if (!candidate.Visible || candidate.IsDisposed || candidate.Disposing)
                {
                    s_windows.RemoveAt(i);
                    continue;
                }

                if (s_windows[i].IsModal)
                {
                    form = candidate;
                    return true;
                }
            }

            form = null;
            return false;
        }
    }
}
