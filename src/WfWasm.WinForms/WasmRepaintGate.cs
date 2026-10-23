using System.Collections.Concurrent;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingRegion = System.Drawing.Region;

namespace System.Windows.Forms;

// Headless repaint gate: desktop Invalidate() queues a paint message, but
// without handles every overload is a silent no-op (Control.Invalidate
// requires IsHandleCreated), so timer/animation/projector UI freezes after
// the first frame. The rewriter redirects all six Control.Invalidate
// overloads to this gate's RequestRepaint twins (same stack shapes). Each
// redirect unions its rect into the per-control dirty map (null = whole
// control); the host drains the map once per frame and re-rasters only
// the dirty region of self-painted controls (clipped OnPaint over the
// retained layer plus a patch upload). Plain Invalidate and host marks
// without geometry record null, the pre-region behavior; region
// overloads record their bounds (the rect-list Region model is exact
// for finite operands, no Graphics needed). The host
// subscribes RepaintRequested and pumps a coalesced repaint; subscribers
// must never throw (a throwing subscriber is detached so one bad listener
// cannot break every app).
public static class WasmRepaintGate
{
    public static event Action? RepaintRequested;

    // Per-control dirty regions (null value = whole control). Rect
    // overloads union their rect; everything else records null. Drain
    // intersects against current bounds (sizes may change between mark
    // and drain); an empty intersection repaints nothing.
    private static readonly ConcurrentDictionary<Control, DrawingRectangle?> s_dirty = new();
    private static int s_globalDirty;

    // Marker census: which managed caller dirties (sampled 1-in-8; a
    // StackTrace walk per mark at ~350 marks/frame would cost more than
    // the attribution is worth). Drained per frame in Home via
    // TakeMarkCaller; names who re-marks every frame in the dirt loop
    // (app Invalidates vs host backstops vs layout Wake calls).
    private static readonly ConcurrentDictionary<string, int> s_markCaller = new(StringComparer.Ordinal);
    private static int s_markSample;
    // Cheap monotonic mark counter for the layout-drain invalidation
    // linkage (item 3): gated like every other census line so default-off
    // pays one predictable branch per mark, nothing more.
    private static int s_totalMarks;

    internal static int TotalMarks => System.Threading.Volatile.Read(ref s_totalMarks);

    // First marker per drain window (unsampled: one StackTrace per
    // window is negligible). Names the steady-state single marker
    // the 1-in-8 census usually misses. Two frames, originator last
    // ("A < B"), skipping this gate's own entry frames.
    private static string? s_firstMarkCaller;

    private static void NoteMarkCaller()
    {
        try
        {
            if (s_firstMarkCaller is null)
            {
                var trace = new System.Diagnostics.StackTrace(1, false);
                string? first = null;
                foreach (var frame in trace.GetFrames())
                {
                    var m = frame.GetMethod();
                    string? type = m?.DeclaringType?.FullName;
                    if (type is null || type.StartsWith("System.Windows.Forms.Wasm", StringComparison.Ordinal))
                        continue;
                    string name = (m?.DeclaringType?.Name ?? "?") + "." + (m?.Name ?? "?");
                    if (first is null)
                        first = name;
                    else
                    {
                        Volatile.Write(ref s_firstMarkCaller, first + " < " + name);
                        break;
                    }
                }
                if (s_firstMarkCaller is null)
                    Volatile.Write(ref s_firstMarkCaller, first ?? "non-app");
            }
        }
        catch { }
        if ((Interlocked.Increment(ref s_markSample) & 7) != 0)
            return;
        try
        {
            // Frames: 0 = here, 1 = this gate's Request/Mark/Wake
            // entry, 2 = the actual managed marker.
            var trace = new System.Diagnostics.StackTrace(2, false);
            var m = trace.GetFrame(0)?.GetMethod();
            string sig = (m?.DeclaringType?.Name ?? "?") + "." + (m?.Name ?? "?");
            s_markCaller.AddOrUpdate(sig, 1, (_, v) => v + 1);
        }
        catch { }
    }

    public static Dictionary<string, int> TakeMarkCaller()
    {
        var d = new Dictionary<string, int>(s_markCaller, StringComparer.Ordinal);
        s_markCaller.Clear();
        return d;
    }

    public static string? TakeFirstMarkCaller() =>
        Interlocked.Exchange(ref s_firstMarkCaller, null);

    // Non-draining peek (the host reads the window's first marker
    // mid-frame while Home still drains it at sample time).
    public static string? PeekFirstMarkCaller() =>
        Volatile.Read(ref s_firstMarkCaller);

    public static void RequestRepaint(Control? control)
    {
        Check(control);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, -1, -1); NoteMarkCaller(); }
        Mark(control!, false, null);
        Fire();
    }

    public static void RequestRepaint(Control? control, bool invalidateChildren)
    {
        Check(control);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, -1, -1); NoteMarkCaller(); }
        Mark(control!, invalidateChildren, null);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRectangle rc)
    {
        Check(control);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, rc.Width, rc.Height); NoteMarkCaller(); }
        Mark(control!, false, rc);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRectangle rc, bool invalidateChildren)
    {
        Check(control);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, rc.Width, rc.Height); NoteMarkCaller(); }
        Mark(control!, invalidateChildren, rc);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRegion? region)
    {
        Check(control);
        DrawingRectangle? rc = RegionBounds(region);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, rc?.Width ?? -1, rc?.Height ?? -1); NoteMarkCaller(); }
        Mark(control!, false, rc);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRegion? region, bool invalidateChildren)
    {
        Check(control);
        DrawingRectangle? rc = RegionBounds(region);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, rc?.Width ?? -1, rc?.Height ?? -1); NoteMarkCaller(); }
        Mark(control!, invalidateChildren, rc);
        Fire();
    }

    // Region-to-rect for the dirty map. Our Region model is an exact
    // rect list for finite operands (rect/union/intersect/exclude/xor),
    // and GetBounds ignores the Graphics, so no scratch surface is
    // needed. Infinite (or null) maps to whole-control null; an empty
    // region keeps its empty rect (desktop paints nothing: the host
    // intersects it to empty and re-blits without invoking). Accepted
    // corner: Complement/Xor against an infinite operand approximates
    // in the Region model itself (see Paths.cs scan-conversion note),
    // so exotic regions may over- or under-scope; rect Invalidates
    // (every observed app path, incl. the RTE caret tick) are exact.
    private static DrawingRectangle? RegionBounds(DrawingRegion? region)
    {
        if (region is null || region.IsInfinite(null))
            return null;
        try
        {
            var b = region.GetBounds(null);
            int x0 = (int)System.Math.Floor(b.X);
            int y0 = (int)System.Math.Floor(b.Y);
            int x1 = (int)System.Math.Ceiling(b.Right);
            int y1 = (int)System.Math.Ceiling(b.Bottom);
            return new DrawingRectangle(x0, y0, System.Math.Max(0, x1 - x0), System.Math.Max(0, y1 - y0));
        }
        catch
        {
            return null;
        }
    }

    // Host-driven marking (hover/press/drag visuals no app Invalidate
    // covers): records without firing (the host already requested the
    // pump). The chain variant covers ancestor layers a mouse-driven
    // change may repaint through.
    public static void MarkDirty(Control? control)
    {
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, -1, -1); NoteMarkCaller(); System.Threading.Interlocked.Increment(ref s_totalMarks); }
        if (control is not null)
            s_dirty[control] = null;
    }

    public static void MarkDirtyChain(Control? control)
    {
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, -1, -1); NoteMarkCaller(); }
        while (control is not null)
        {
            if (WasmFrameCounters.PerfEnabled) System.Threading.Interlocked.Increment(ref s_totalMarks);
            s_dirty[control] = null;
            control = control.Parent;
        }
    }

    // Rect-scoped host marking (per-move pointer backstop): unions the
    // client-coords rect into the leaf; ancestors union the translated
    // rect intersected with the child bounds (the area the change may
    // repaint through). A translation that misses the child bounds stops
    // the walk: a rect outside the child surface is invisible above it.
    public static void MarkDirtyRect(Control? control, DrawingRectangle rc)
    {
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(control, rc.Width, rc.Height); NoteMarkCaller(); }
        if (control is not null)
            UnionInto(control, rc);
    }

    public static void MarkDirtyChainRect(Control? leaf, DrawingRectangle clientRect)
    {
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteMark(leaf, clientRect.Width, clientRect.Height); NoteMarkCaller(); }
        Control? c = leaf;
        DrawingRectangle rc = clientRect;
        while (c is not null)
        {
            UnionInto(c, rc);
            Control? parent = c.Parent;
            if (parent is null)
                break;
            DrawingRectangle moved = new(
                rc.X + c.Location.X, rc.Y + c.Location.Y, rc.Width, rc.Height);
            rc = DrawingRectangle.Intersect(moved, c.Bounds);
            if (rc.Width <= 0 || rc.Height <= 0)
                break;
            c = parent;
        }
    }

    public static void MarkGlobalDirty()
    {
        Interlocked.Exchange(ref s_globalDirty, 1);
        if (WasmFrameCounters.PerfEnabled) { WasmFrameCounters.NoteGlobal(); NoteMarkCaller(); System.Threading.Interlocked.Increment(ref s_totalMarks); }
    }

    // Retained scroll reuse (item 4): a scrolled control records its pixel
    // delta here (SetDisplayRectLocation and friends); the next frame shifts
    // the retained surface by (dx,dy) and repaints only the exposed strip.
    // Consumed once (Take clears); a second scroll before the frame
    // accumulates (still a pure shift). Dropped silently when the frame
    // cannot use it (resize, diagonal, rect-dirty mismatch) — the existing
    // full/region paths run unchanged.
    private static readonly ConcurrentDictionary<Control, (int dx, int dy)> s_scrollShift = new();

    public static void NoteScroll(Control? control, int dx, int dy)
    {
        if (control is null || (dx == 0 && dy == 0))
            return;
        s_scrollShift.AddOrUpdate(control, (dx, dy), (_, old) => (old.dx + dx, old.dy + dy));
    }

    public static bool TakeScrollShift(Control control, out int dx, out int dy)
    {
        if (s_scrollShift.TryRemove(control, out var shift))
        {
            dx = shift.dx;
            dy = shift.dy;
            return true;
        }
        dx = 0;
        dy = 0;
        return false;
    }

    // Peek without draining (lets the pump skip fully-idle frames).
    public static bool HasDirty => s_globalDirty != 0 || !s_dirty.IsEmpty;

    // Drains one frame: returns the global flag plus the dirty map
    // (null value = whole control). Snapshot + per-key remove, never
    // Clear: a control marked mid-drain survives for the next frame
    // instead of vanishing. A lost remove race (two concurrent drains)
    // records null: over-paint is always safe.
    public static bool DrainDirty(out Dictionary<Control, DrawingRectangle?> dirty)
    {
        dirty = new Dictionary<Control, DrawingRectangle?>();
        foreach (Control c in s_dirty.Keys.ToArray())
        {
            if (!s_dirty.TryRemove(c, out DrawingRectangle? rc))
                rc = null;
            dirty[c] = rc;
        }

        return Interlocked.Exchange(ref s_globalDirty, 0) != 0;
    }


    private static void UnionInto(Control control, DrawingRectangle? rect)
    {
        if (WasmFrameCounters.PerfEnabled) System.Threading.Interlocked.Increment(ref s_totalMarks);
        if (rect is null)
        {
            s_dirty[control] = null;
            return;
        }

        s_dirty.AddOrUpdate(control, rect, (_, old) =>
            old is null ? null : DrawingRectangle.Union(old.Value, rect.Value));
    }

    private static void Mark(Control control, bool subtree, DrawingRectangle? rect)
    {
        UnionInto(control, rect);
        if (!subtree)
            return;
        try
        {
            // Descendants take null (whole control), the pre-region
            // behavior: a translated-intersection here would need scroll
            // offsets to stay exact, and a superset never misses pixels.
            foreach (Control child in control.Controls)
                Mark(child, true, null);
        }
        catch
        {
            // Subtree walk failed (concurrent mutation): fall back to
            // global so no repaint is ever lost.
            MarkGlobalDirty();
        }
    }

    // The redirect replaces an instance callvirt: preserve desktop's
    // NullReferenceException on a null receiver instead of swallowing it.
    private static void Check(Control? control)
    {
        if (control is null)
            throw new NullReferenceException();
    }

    // Wakes the paint pump without dirtying anything (posted layout
    // work must drain next frame even from a fully idle state).
    // Census-hooked: a Wake-driven frame that repaints is the dirt
    // loop's other half, so Wake callers are counted as markers.
    public static void Wake()
    {
        if (WasmFrameCounters.PerfEnabled) NoteMarkCaller();
        Fire();
    }

    private static void Fire()
    {
        Action? handler = RepaintRequested;
        if (handler is null)
            return;
        foreach (Action single in handler.GetInvocationList())
        {
            try
            {
                single();
            }
            catch
            {
                try
                {
                    RepaintRequested -= single;
                }
                catch
                {
                }
            }
        }
    }
}
