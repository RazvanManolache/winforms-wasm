using System.Windows.Forms;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Decision logic of the RTE scroll watcher: a scrollbar delta becomes a
// NoteScroll only when it is provably a rigid translation of identical
// content (subscribed control, alive, same size/document/range/token, exactly
// one axis, sub-viewport delta). Everything else rebases the baseline or drops
// the entry without emitting. Vendor reflection is faked here; the production
// source only feeds snapshots into the same verdict.
public sealed class RichTextScrollWatchTests
{
    private sealed class FakeSource : RichTextScrollWatch.IScrollSource
    {
        public readonly Dictionary<Control, RichTextScrollWatch.ScrollSnapshot> States = new();

        public bool TryResolveEditor(Control target, out Control? editor)
        {
            for (Control? c = target; c is not null; c = c.Parent)
            {
                if (States.ContainsKey(c))
                {
                    editor = c;
                    return true;
                }
            }
            editor = null;
            return false;
        }

        public bool TryGetState(Control editor, out RichTextScrollWatch.ScrollSnapshot state) =>
            States.TryGetValue(editor, out state);
    }

    private static readonly object DocA = new();
    private static readonly object DocB = new();

    private static RichTextScrollWatch.ScrollSnapshot Snap(
        int hValue = 0, int vValue = 0,
        int hMax = 2000, int vMax = 2000, int hLarge = 600, int vLarge = 600,
        object? doc = null, long token = 0, int w = 1109, int h = 598,
        double sx = 1.0, double sy = 1.0) =>
        new(hValue, vValue, 0, hMax, hLarge, 0, vMax, vLarge, doc ?? DocA, token, w, h, sx, sy);

    private static (RichTextScrollWatch watch, FakeSource src, Panel panel) Setup(
        RichTextScrollWatch.ScrollSnapshot? baseline = null)
    {
        var src = new FakeSource();
        var watch = new RichTextScrollWatch(src);
        var panel = new Panel { Size = new System.Drawing.Size(1109, 598) };
        src.States[panel] = baseline ?? Snap();
        return (watch, src, panel);
    }

    private static void Drain(Control c) =>
        WasmRepaintGate.TakeScrollShift(c, out _, out _);

    [Fact]
    public void Ensure_NonRteControl_ReturnsFalseAndWatchesNothing()
    {
        var (watch, _, panel) = Setup();
        try
        {
            using var stranger = new Panel();
            Assert.False(watch.Ensure(stranger));
            Assert.Equal(0, watch.WatchedCount);
        }
        finally
        {
            panel.Dispose();
        }
    }

    [Fact]
    public void Ensure_RteControl_SubscribesWithoutEmitting()
    {
        var (watch, _, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            Assert.Equal(1, watch.WatchedCount);
            var decisions = watch.Poll();
            Assert.Single(decisions);
            Assert.Equal(RichTextScrollWatch.DecisionKind.NoChange, decisions[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            panel.Dispose();
        }
    }

    [Fact]
    public void PureVerticalScrollDown_EmitsNegatedDy()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 120);
            var decisions = watch.Poll();
            Assert.Single(decisions);
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, decisions[0].Kind);
            Assert.Equal(0, decisions[0].Dx);
            Assert.Equal(-120, decisions[0].Dy);
            Assert.True(WasmRepaintGate.TakeScrollShift(panel, out int dx, out int dy));
            Assert.Equal(0, dx);
            Assert.Equal(-120, dy);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void ReverseScroll_EmitsPositiveDyFromNewBaseline()
    {
        var (watch, src, panel) = Setup(Snap(vValue: 120));
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 40);
            var decisions = watch.Poll();
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, decisions[0].Kind);
            Assert.Equal(80, decisions[0].Dy);
            Assert.True(WasmRepaintGate.TakeScrollShift(panel, out _, out int dy));
            Assert.Equal(80, dy);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void HorizontalScroll_EmitsNegatedDx()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(hValue: 60);
            var decisions = watch.Poll();
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, decisions[0].Kind);
            Assert.Equal(-60, decisions[0].Dx);
            Assert.Equal(0, decisions[0].Dy);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void Resize_RebasesWithoutEmitting()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 120, w: 800, h: 600);
            var decisions = watch.Poll();
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, decisions[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
            Assert.Single(watch.Poll());
            Assert.Equal(RichTextScrollWatch.DecisionKind.NoChange, watch.Poll()[0].Kind);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void ScrollbarRangeChange_Rebases()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 120, vMax: 2500);
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, watch.Poll()[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void DocumentSwap_Rebases()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 120, doc: DocB);
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, watch.Poll()[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void ContentTokenChange_Rebases()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 120, token: 7);
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, watch.Poll()[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void DiagonalChange_Rebases()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(hValue: 30, vValue: 120);
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, watch.Poll()[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void OversizeDelta_Rebases()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 598);
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, watch.Poll()[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void DisposedControl_IsDropped()
    {
        var (watch, _, panel) = Setup();
        Assert.True(watch.Ensure(panel));
        panel.Dispose();
        var decisions = watch.Poll();
        Assert.Single(decisions);
        Assert.Equal(RichTextScrollWatch.DecisionKind.Dropped, decisions[0].Kind);
        Assert.Equal(0, watch.WatchedCount);
    }

    [Fact]
    public void ChildControl_ResolvesToWatchedAncestor()
    {
        var (watch, src, panel) = Setup();
        using var child = new Panel();
        try
        {
            panel.Controls.Add(child);
            Assert.True(watch.Ensure(child));
            Assert.Equal(1, watch.WatchedCount);
            src.States[panel] = Snap(vValue: 90);
            var decisions = watch.Poll();
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, decisions[0].Kind);
            Assert.Same(panel, decisions[0].Control);
            Assert.True(WasmRepaintGate.TakeScrollShift(panel, out _, out int dy));
            Assert.Equal(-90, dy);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    // Seam test (WF-D-050): the production source must resolve scrollbars the
    // way the real vendor shapes them. A stub carrying ONLY the true editor
    // prop set (RichTextBoxElement + Document, no direct scrollbar props —
    // verified absent on pristine + transformed RadRichTextEditor) must still
    // yield a state via the hosted box. Failed pre-fix (shape null), green
    // post-fix. Uses the REAL VendorRichTextScrollSource, not FakeSource.
    private sealed class VendorShapedBarStub
    {
        public int Value { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }
        public int LargeChange { get; set; }
    }

    private sealed class VendorShapedBoxStub
    {
        public VendorShapedBarStub? VerticalScrollBar { get; set; }
        public VendorShapedBarStub? HorizontalScrollBar { get; set; }
        public VendorShapedScaleStub? ScaleFactor { get; set; }
    }

    private sealed class VendorShapedScaleStub
    {
        public double Width { get; set; } = 1.0;
        public double Height { get; set; } = 1.0;
    }

    private sealed class VendorShapedEditorStub : Control
    {
        public VendorShapedBoxStub? RichTextBoxElement { get; set; }
        public object? Document { get; set; }
    }

    [Fact]
    public void VendorSource_RealEditorShape_ResolvesScrollbarsViaHostedBox()
    {
        using var editor = new VendorShapedEditorStub
        {
            Size = new System.Drawing.Size(1109, 598),
            Document = DocA,
            RichTextBoxElement = new VendorShapedBoxStub
            {
                VerticalScrollBar = new VendorShapedBarStub
                    { Value = 120, Minimum = 0, Maximum = 2000, LargeChange = 600 },
                HorizontalScrollBar = new VendorShapedBarStub
                    { Value = 0, Minimum = 0, Maximum = 2000, LargeChange = 600 },
            },
        };
        Assert.True(
            RichTextScrollWatch.VendorRichTextScrollSource.Instance.TryGetState(
                editor, out RichTextScrollWatch.ScrollSnapshot state));
        Assert.Equal(0, state.HorizontalValue);
        Assert.Equal(120, state.VerticalValue);
        Assert.Equal(1109, state.ClientWidth);
        Assert.Equal(598, state.ClientHeight);
        Assert.Same(DocA, state.Document);
        Assert.Equal(1.0, state.ScaleX);
        Assert.Equal(1.0, state.ScaleY);
    }

    [Fact]
    public void VendorSource_ZoomedBox_ReadsScaleFactor()
    {
        // Same real-source path with a zoomed box (ScaleFactor shape mirrors
        // the verified vendor UI.Size: Width/Height doubles).
        using var editor = new VendorShapedEditorStub
        {
            Size = new System.Drawing.Size(1109, 598),
            Document = DocA,
            RichTextBoxElement = new VendorShapedBoxStub
            {
                VerticalScrollBar = new VendorShapedBarStub
                    { Value = 120, Minimum = 0, Maximum = 2000, LargeChange = 600 },
                HorizontalScrollBar = new VendorShapedBarStub
                    { Value = 0, Minimum = 0, Maximum = 2000, LargeChange = 600 },
                ScaleFactor = new VendorShapedScaleStub { Width = 1.5, Height = 1.5 },
            },
        };
        Assert.True(
            RichTextScrollWatch.VendorRichTextScrollSource.Instance.TryGetState(
                editor, out RichTextScrollWatch.ScrollSnapshot state));
        Assert.Equal(1.5, state.ScaleX);
        Assert.Equal(1.5, state.ScaleY);
    }

    [Fact]
    public void ScrollbarDelta_PassesThroughUnscaled_PixelTruth()
    {
        // Unit truth (WF-P-054a, vendor DocumentWebLayoutPresenter):
        // VerticalScrollBar.Value == round(VerticalScrollOffset) and device
        // px = offset-delta x ScaleFactor, so at default zoom one scrollbar
        // unit IS one pixel: the watcher must forward the negated delta
        // exactly, with no scale factor. A 240-unit wheel notch (raw
        // e.Delta, Owner_MouseWheel) notes dy=-240.
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 240);
            var decisions = watch.Poll();
            Assert.Single(decisions);
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, decisions[0].Kind);
            Assert.Equal(0, decisions[0].Dx);
            Assert.Equal(-240, decisions[0].Dy);
            Assert.True(WasmRepaintGate.TakeScrollShift(panel, out int dx, out int dy));
            Assert.Equal(0, dx);
            Assert.Equal(-240, dy);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void ZoomedScroll_ScalesNotedDelta()
    {
        // WF-P-058: at zoom != 1 one scrollbar unit displaces ScaleFactor
        // device px (vendor view = (doc - offset) x ScaleFactor). A 100-unit
        // Value delta at 1.5x zoom must note dy=-150, not the raw -100.
        var (watch, src, panel) = Setup(Snap(sx: 1.5, sy: 1.5));
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 100, sx: 1.5, sy: 1.5);
            var decisions = watch.Poll();
            Assert.Single(decisions);
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, decisions[0].Kind);
            Assert.Equal(0, decisions[0].Dx);
            Assert.Equal(-150, decisions[0].Dy);
            Assert.True(WasmRepaintGate.TakeScrollShift(panel, out _, out int dy));
            Assert.Equal(-150, dy);
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void ZoomChange_RebasesWithoutEmitting()
    {
        // Same scrollbar Value at a new zoom is a different pixel
        // displacement: rebase so later scrolls start from a known frame.
        var (watch, src, panel) = Setup(Snap(sx: 1.0, sy: 1.0));
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 100, sx: 1.5, sy: 1.5);
            Assert.Equal(RichTextScrollWatch.DecisionKind.Rebased, watch.Poll()[0].Kind);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }

    [Fact]
    public void SettledPoll_EmitsNothing()
    {
        var (watch, src, panel) = Setup();
        try
        {
            Assert.True(watch.Ensure(panel));
            src.States[panel] = Snap(vValue: 50);
            Assert.Equal(RichTextScrollWatch.DecisionKind.ShiftNoted, watch.Poll()[0].Kind);
            Assert.Equal(RichTextScrollWatch.DecisionKind.NoChange, watch.Poll()[0].Kind);
            Drain(panel);
            Assert.False(WasmRepaintGate.TakeScrollShift(panel, out _, out _));
        }
        finally
        {
            Drain(panel);
            panel.Dispose();
        }
    }
}
