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
// retained layer plus a patch upload). Plain Invalidate, region
// overloads (region detail needs a Graphics for bounds), and host marks
// without geometry record null, the pre-region behavior. The host
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

    public static void RequestRepaint(Control? control)
    {
        Check(control);
        Mark(control!, false, null);
        Fire();
    }

    public static void RequestRepaint(Control? control, bool invalidateChildren)
    {
        Check(control);
        Mark(control!, invalidateChildren, null);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRectangle rc)
    {
        Check(control);
        Mark(control!, false, rc);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRectangle rc, bool invalidateChildren)
    {
        Check(control);
        Mark(control!, invalidateChildren, rc);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRegion? region)
    {
        Check(control);
        Mark(control!, false, null);
        Fire();
    }

    public static void RequestRepaint(Control? control, DrawingRegion? region, bool invalidateChildren)
    {
        Check(control);
        Mark(control!, invalidateChildren, null);
        Fire();
    }

    // Host-driven marking (hover/press/drag visuals no app Invalidate
    // covers): records without firing (the host already requested the
    // pump). The chain variant covers ancestor layers a mouse-driven
    // change may repaint through.
    public static void MarkDirty(Control? control)
    {
        if (control is not null)
            s_dirty[control] = null;
    }

    public static void MarkDirtyChain(Control? control)
    {
        while (control is not null)
        {
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
        if (control is not null)
            UnionInto(control, rc);
    }

    public static void MarkDirtyChainRect(Control? leaf, DrawingRectangle clientRect)
    {
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

    public static void MarkGlobalDirty() => Interlocked.Exchange(ref s_globalDirty, 1);

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
