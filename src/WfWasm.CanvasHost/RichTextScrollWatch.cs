using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace WfWasm.CanvasHost;

// Host-side scroll watcher for Telerik RichTextEditor (stage 2, wf-perf).
//
// Vendor pipeline (decompiled from Telerik.WinControls.RichTextEditor.dll):
// every scroll-to-visible road ends at Owner.VerticalScrollBar.Value
// (caret moves, wheel, scrollbar drag, UpdateScrollOffsetFromDocumentPosition).
// No Telerik assembly imports ScrollWindowEx, so the WasmScrollWindow shim can
// never fire here; the only shift channel is WasmRepaintGate.NoteScroll, which
// the pump consumes once per frame (TryShiftRetained) with its own narrowing,
// 90%-gate, supersede-drop and kill-switch. This file only decides WHEN a
// scrollbar delta is a rigid translation of identical content worth noting.
//
// Safety layering: this watcher suppresses anything that is not provably a
// pure scroll (resize, document swap, scrollbar range change, content-token
// change, diagonal or oversize deltas, disposed or unresolvable controls all
// rebase or drop instead of emitting). Exactness of the pixels themselves stays
// with the existing downstream contracts (shift + exposed-strip repaint +
// layer-hash drop), which this file reuses without modification.
//
// No global hooks: state lives in this instance's ConditionalWeakTable and only
// for controls explicitly passed to Ensure (a later integration patch will call
// it from the RTE scene setup / wheel path and Poll from the pump next to
// PollGridScrollWatches). Nothing here observes controls that were never
// subscribed, and entries die with their control.
public sealed class RichTextScrollWatch
{
    public enum DecisionKind
    {
        NoChange,
        ShiftNoted,
        Rebased,
        Unsupported,
        Dropped,
    }

    public readonly record struct PollDecision(Control Control, int Dx, int Dy, DecisionKind Kind);

    // Snapshot of everything the pure-scroll verdict depends on. ContentToken
    // is a best-effort document-generation marker (history depth when the
    // vendor exposes it, else constant); the structural guards below are the
    // hard gates and never depend on it.
    public readonly record struct ScrollSnapshot(
        int HorizontalValue, int VerticalValue,
        int HorizontalMinimum, int HorizontalMaximum, int HorizontalLargeChange,
        int VerticalMinimum, int VerticalMaximum, int VerticalLargeChange,
        object? Document, long ContentToken, int ClientWidth, int ClientHeight,
        double ScaleX, double ScaleY);

    // All vendor knowledge lives behind this seam so the verdict logic is
    // unit-testable with fakes; production uses VendorRichTextScrollSource.
    public interface IScrollSource
    {
        bool TryResolveEditor(Control target, out Control? editor);
        bool TryGetState(Control editor, out ScrollSnapshot state);
    }

    private sealed class WatchEntry
    {
        public ScrollSnapshot Baseline;
        public long GenerationSeq;
    }

    private readonly ConditionalWeakTable<Control, WatchEntry> _watches = new();
    private readonly IScrollSource _source;
    private readonly object _sync = new();
    // Monotonic clock for baseline rewrites (every ShiftNoted/Rebased that
    // stores a new baseline, plus fresh subscriptions). Lets the post-shift
    // verify path prove the scroll generation did not move under a carried
    // shift: same seq => same Values/token/ranges/zoom meaning. Never
    // rewound; entries die with their control (missing entry reads unstable).
    private long _generationClock;

    internal bool TryGetGenerationSeq(Control control, out long seq)
    {
        seq = 0;
        try
        {
            lock (_sync)
            {
                if (_watches.TryGetValue(control, out WatchEntry? entry) && entry is not null)
                {
                    seq = entry.GenerationSeq;
                    return true;
                }
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public RichTextScrollWatch(IScrollSource? source = null)
    {
        _source = source ?? VendorRichTextScrollSource.Instance;
    }

    public int WatchedCount
    {
        get
        {
            lock (_sync)
            {
                int n = 0;
                foreach (var _ in _watches)
                    n++;
                return n;
            }
        }
    }

    // Subscribes an RTE control (or any descendant): walks up to the nearest
    // RadRichTextEditor / RadRichTextBox ancestor and records the baseline.
    // First observation never emits (there is no prior frame to shift from).
    // Returns false for non-RTE controls or unresolvable scrollbars: the
    // caller keeps the existing full path (unsupported-path fallback).
    public bool Ensure(Control? target)
    {
        if (target is null || target.IsDisposed)
            return false;
        try
        {
            if (!_source.TryResolveEditor(target, out Control? editor) || editor is null)
                return false;
            if (editor.IsDisposed)
                return false;
            if (!_source.TryGetState(editor, out ScrollSnapshot state))
                return false;
            lock (_sync)
            {
                if (_watches.TryGetValue(editor, out _))
                    return true;
                _watches.Add(editor, new WatchEntry
                {
                    Baseline = state,
                    GenerationSeq = Interlocked.Increment(ref _generationClock),
                });
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    // Diffs every subscribed control once. Pure single-axis scrollbar movement
    // with all generation guards stable emits exactly one NoteScroll with the
    // content displacement in device px: the negated scrollbar delta scaled by
    // the vendor zoom (view = (doc - offset) x ScaleFactor), rounded to whole
    // px the same way the vendor rounds Value. Scrolling down moves content
    // up, matching the RadGridView watch and ScrollWindowEx signs.
    // Anything else rebases the baseline or drops the entry; never throws.
    public IReadOnlyList<PollDecision> Poll()
    {
        List<Control> keys;
        lock (_sync)
        {
            keys = new List<Control>();
            foreach (var kv in _watches)
                keys.Add(kv.Key);
        }
        if (keys.Count == 0)
            return Array.Empty<PollDecision>();
        var decisions = new List<PollDecision>(keys.Count);
        foreach (Control control in keys)
        {
            try
            {
                decisions.Add(PollOne(control));
            }
            catch
            {
                Drop(control);
                decisions.Add(new PollDecision(control, 0, 0, DecisionKind.Dropped));
            }
        }
        return decisions;
    }

    private PollDecision PollOne(Control control)
    {
        WatchEntry? entry;
        lock (_sync)
        {
            if (!_watches.TryGetValue(control, out entry) || entry is null)
                return new PollDecision(control, 0, 0, DecisionKind.NoChange);
        }
        if (control.IsDisposed)
        {
            Drop(control);
            return new PollDecision(control, 0, 0, DecisionKind.Dropped);
        }
        if (!_source.TryGetState(control, out ScrollSnapshot now))
        {
            Drop(control);
            return new PollDecision(control, 0, 0, DecisionKind.Dropped);
        }
        ScrollSnapshot was = entry.Baseline;
        if (!GenerationStable(was, now))
        {
            entry.Baseline = now;
            entry.GenerationSeq = Interlocked.Increment(ref _generationClock);
            return new PollDecision(control, 0, 0, DecisionKind.Rebased);
        }
        int dx = (int)Math.Round(-(now.HorizontalValue - was.HorizontalValue) * now.ScaleX,
            MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round(-(now.VerticalValue - was.VerticalValue) * now.ScaleY,
            MidpointRounding.AwayFromZero);
        if (dx == 0 && dy == 0)
            return new PollDecision(control, 0, 0, DecisionKind.NoChange);
        if ((dx != 0 && dy != 0)
            || Math.Abs(dx) >= now.ClientWidth
            || Math.Abs(dy) >= now.ClientHeight)
        {
            // Diagonal and oversize shifts are not rigid-strip translations;
            // rebase so a later pure scroll starts from a known frame.
            entry.Baseline = now;
            entry.GenerationSeq = Interlocked.Increment(ref _generationClock);
            return new PollDecision(control, 0, 0, DecisionKind.Rebased);
        }
        WasmRepaintGate.NoteScroll(control, dx, dy);
        entry.Baseline = now;
        entry.GenerationSeq = Interlocked.Increment(ref _generationClock);
        if (WasmFrameCounters.PerfEnabled)
            Console.WriteLine($"[Host] RteWatch control='{control.GetType().FullName}' dx={dx} dy={dy}");
        return new PollDecision(control, dx, dy, DecisionKind.ShiftNoted);
    }

    // Generation guards: any change here means the retained pixels may no
    // longer be a rigid translation of the current document (edits, layout,
    // range repair, document swap, resize, zoom: the same Value displaces
    // different device px at a new ScaleFactor). Value deltas are compared
    // separately by the caller.
    private static bool GenerationStable(ScrollSnapshot was, ScrollSnapshot now) =>
        ReferenceEquals(was.Document, now.Document)
        && was.ContentToken == now.ContentToken
        && was.HorizontalMinimum == now.HorizontalMinimum
        && was.HorizontalMaximum == now.HorizontalMaximum
        && was.HorizontalLargeChange == now.HorizontalLargeChange
        && was.VerticalMinimum == now.VerticalMinimum
        && was.VerticalMaximum == now.VerticalMaximum
        && was.VerticalLargeChange == now.VerticalLargeChange
        && was.ClientWidth == now.ClientWidth
        && was.ClientHeight == now.ClientHeight
        && was.ScaleX == now.ScaleX
        && was.ScaleY == now.ScaleY;

    private void Drop(Control control)
    {
        lock (_sync)
        {
            _watches.Remove(control);
        }
    }

    // Production source: real vendor scrollbar values via reflection. Type
    // names are matched, never subclassed (vendor assemblies are rewritten
    // inputs, not compile references). Scrollbars are read off the hosted
    // box (editor.RichTextBoxElement), never the editor: only RadRichTextBox
    // owns Vertical/HorizontalScrollBar (verified on pristine + transformed
    // vendor dlls; presenter Owner is RadRichTextBox). Every lookup is
    // per-call and guarded; anything unresolvable reports false so the
    // watcher treats the control as unsupported instead of guessing.
    // PropertyInfo caches are keyed by vendor TYPE, never by control
    // instance: no per-control data is retained here (baselines live in the
    // watch instance that subscribed them).
    public sealed class VendorRichTextScrollSource : IScrollSource
    {
        public static readonly VendorRichTextScrollSource Instance = new();

        private const string EditorTypeName = "Telerik.WinControls.UI.RadRichTextEditor";
        private const string BoxTypeName = "Telerik.WinForms.RichTextEditor.RadRichTextBox";

        private readonly ConcurrentDictionary<Type, ResolvedShape?> _shapes = new();

        private sealed class ResolvedShape
        {
            public Func<object, object?> GetBoxElement = _ => null;
            public Func<object, object?> GetVerticalBar = _ => null;
            public Func<object, object?> GetHorizontalBar = _ => null;
            public Func<object, object?> GetDocument = _ => null;
            public Func<object, object?> GetScaleFactor = _ => null;
        }

        public bool TryResolveEditor(Control target, out Control? editor)
        {
            editor = null;
            try
            {
                for (Control? c = target; c is not null; c = c.Parent)
                {
                    string? full = c.GetType().FullName;
                    if (full == EditorTypeName || full == BoxTypeName)
                    {
                        editor = c;
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                editor = null;
                return false;
            }
        }

        public bool TryGetState(Control editor, out ScrollSnapshot state)
        {
            state = default;
            try
            {
                ResolvedShape? shape = _shapes.GetOrAdd(editor.GetType(), ResolveShape);
                if (shape is null)
                    return false;
                // Scrollbars live on the hosted box, not the editor: route
                // through RichTextBoxElement (decompiled chain: presenter
                // Owner is RadRichTextBox; editor.RichTextBoxElement returns it).
                object? box = shape.GetBoxElement(editor);
                if (box is null)
                    return false;
                object? vBar = shape.GetVerticalBar(box);
                object? hBar = shape.GetHorizontalBar(box);
                if (vBar is null || hBar is null)
                    return false;
                if (!BarInts(vBar, out int vv, out int vMin, out int vMax, out int vLarge)
                    || !BarInts(hBar, out int hv, out int hMin, out int hMax, out int hLarge))
                    return false;
                object? doc = shape.GetDocument(editor);
                System.Drawing.Size size = editor.ClientSize;
                if (size.Width <= 0 || size.Height <= 0)
                    return false;
                ReadScale(box, shape, out double sx, out double sy);
                state = new ScrollSnapshot(hv, vv, hMin, hMax, hLarge, vMin, vMax, vLarge,
                    doc, ReadContentToken(doc), size.Width, size.Height, sx, sy);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ResolvedShape? ResolveShape(Type editorType)
        {
            try
            {
                // The editor carries no scrollbar props (verified on pristine +
                // transformed Telerik.WinControls.RichTextEditor.dll: only
                // RadRichTextBox owns Vertical/HorizontalScrollBar). Resolve the
                // hosted box type statically off the element getter, then read
                // the bars off the box. Document + ClientSize stay on the editor
                // (generation guards + viewport semantics unchanged).
                var boxProp = editorType.GetProperty("RichTextBoxElement");
                var dProp = editorType.GetProperty("Document");
                if (boxProp is null || dProp is null || !boxProp.CanRead || !dProp.CanRead)
                    return null;
                Type boxType = boxProp.PropertyType;
                var vProp = boxType.GetProperty("VerticalScrollBar");
                var hProp = boxType.GetProperty("HorizontalScrollBar");
                if (vProp is null || hProp is null || !vProp.CanRead || !hProp.CanRead)
                    return null;
                // Zoom scale lives on the same hosted box (RadRichTextBox.ScaleFactor,
                // verified on the pristine vendor dll); absent/unreadable degrades to
                // the default-zoom 1.0 in ReadScale, never to an unsupported verdict.
                var sProp = boxType.GetProperty("ScaleFactor");
                Func<object, object?> getScale = sProp is not null && sProp.CanRead
                    ? o => sProp.GetValue(o)
                    : _ => null;
                return new ResolvedShape
                {
                    GetBoxElement = o => boxProp.GetValue(o),
                    GetVerticalBar = o => vProp.GetValue(o),
                    GetHorizontalBar = o => hProp.GetValue(o),
                    GetDocument = o => dProp.GetValue(o),
                    GetScaleFactor = getScale,
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool BarInts(object bar, out int value, out int min, out int max, out int large)
        {
            value = 0; min = 0; max = 0; large = 0;
            try
            {
                Type t = bar.GetType();
                object? v = t.GetProperty("Value")?.GetValue(bar);
                object? lo = t.GetProperty("Minimum")?.GetValue(bar);
                object? hi = t.GetProperty("Maximum")?.GetValue(bar);
                object? lg = t.GetProperty("LargeChange")?.GetValue(bar);
                if (v is null || lo is null || hi is null || lg is null)
                    return false;
                value = Convert.ToInt32(v);
                min = Convert.ToInt32(lo);
                max = Convert.ToInt32(hi);
                large = Convert.ToInt32(lg);
                return max >= min && large >= 0;
            }
            catch
            {
                return false;
            }
        }

        // Zoom scale off the hosted box (RadRichTextBox.ScaleFactor: UI.Size
        // with double Width/Height, verified on the pristine vendor dll).
        // Vendor view mapping is view = (doc - offset) x ScaleFactor, so one
        // scrollbar unit displaces ScaleFactor device px. Unreadable,
        // non-finite, or out of the vendor [MinScaleFactor, MaxScaleFactor] =
        // [0.25, 8.0] range degrades to the default-zoom 1.0: the scroll path
        // stays alive with today's behavior instead of going unsupported.
        private static void ReadScale(object? box, ResolvedShape shape, out double sx, out double sy)
        {
            sx = 1.0;
            sy = 1.0;
            try
            {
                object? scale = box is null ? null : shape.GetScaleFactor(box);
                if (scale is null)
                    return;
                Type st = scale.GetType();
                object? w = st.GetProperty("Width")?.GetValue(scale);
                object? h = st.GetProperty("Height")?.GetValue(scale);
                if (w is null || h is null)
                    return;
                double dw = Convert.ToDouble(w);
                double dh = Convert.ToDouble(h);
                if (double.IsFinite(dw) && double.IsFinite(dh)
                    && dw >= 0.25 && dw <= 8.0 && dh >= 0.25 && dh <= 8.0)
                {
                    sx = dw;
                    sy = dh;
                }
            }
            catch
            {
            }
        }

        // Best-effort document-generation marker: vendor undo depth when the
        // shape exposes it, else constant 0 (never triggers a rebase alone;
        // the structural guards above stay authoritative).
        private static long ReadContentToken(object? doc)
        {
            try
            {
                if (doc is null)
                    return 0;
                object? history = doc.GetType().GetProperty("History")?.GetValue(doc);
                if (history is null)
                    return 0;
                Type ht = history.GetType();
                object? depth = ht.GetProperty("UndoStack")?.GetValue(history);
                if (depth is System.Collections.ICollection c)
                    return c.Count;
                object? n = ht.GetProperty("UndoCount")?.GetValue(history)
                    ?? ht.GetProperty("Count")?.GetValue(history);
                return n is null ? 0 : Convert.ToInt64(n);
            }
            catch
            {
                return 0;
            }
        }
    }
}
