extern alias WasmDraw;

using WfRect = WasmDraw::System.Drawing.Rectangle;
using WfRegion = WasmDraw::System.Drawing.Region;

namespace WfWasm.WinForms.Tests;

// Headless dirty tracking: Invalidate redirects record their source
// control so the host can skip CPU re-raster for clean controls.
// Regression: ~850ms photo frames (full re-raster + re-hash of every
// layer per repaint made photo drag lag seconds behind the cursor).
// Region pass: rect overloads union per control (null = whole control)
// so the host re-rasters only the dirty region of self-painted layers.
public sealed class WasmRepaintGateTests
{
    [Fact]
    public void RequestRepaint_Marks_Control_Drain_Returns_It()
    {
        try
        {
            using var control = new System.Windows.Forms.Control();
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control);
            Assert.True(System.Windows.Forms.WasmRepaintGate.HasDirty);
            bool global = System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.False(global);
            Assert.True(dirty.ContainsKey(control));
            Assert.Null(dirty[control]);
            Assert.False(System.Windows.Forms.WasmRepaintGate.HasDirty);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void RequestRepaint_InvalidateChildren_Marks_Subtree()
    {
        try
        {
            using var parent = new System.Windows.Forms.Control();
            using var child = new System.Windows.Forms.Control();
            using var grandchild = new System.Windows.Forms.Control();
            parent.Controls.Add(child);
            child.Controls.Add(grandchild);
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(parent, true);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.True(dirty.ContainsKey(parent));
            Assert.True(dirty.ContainsKey(child));
            Assert.True(dirty.ContainsKey(grandchild));
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void RequestRepaint_WithoutChildren_Marks_Self_Only()
    {
        try
        {
            using var parent = new System.Windows.Forms.Control();
            using var child = new System.Windows.Forms.Control();
            parent.Controls.Add(child);
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(parent, false);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.True(dirty.ContainsKey(parent));
            Assert.False(dirty.ContainsKey(child));
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void MarkDirtyChain_Marks_Self_And_Ancestors()
    {
        try
        {
            using var parent = new System.Windows.Forms.Control();
            using var child = new System.Windows.Forms.Control();
            using var other = new System.Windows.Forms.Control();
            parent.Controls.Add(child);
            System.Windows.Forms.WasmRepaintGate.MarkDirtyChain(child);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.True(dirty.ContainsKey(child));
            Assert.True(dirty.ContainsKey(parent));
            Assert.False(dirty.ContainsKey(other));
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void MarkGlobalDirty_Drain_Returns_Global_Once()
    {
        try
        {
            System.Windows.Forms.WasmRepaintGate.MarkGlobalDirty();
            Assert.True(System.Windows.Forms.WasmRepaintGate.HasDirty);
            Assert.True(System.Windows.Forms.WasmRepaintGate.DrainDirty(out _));
            Assert.False(System.Windows.Forms.WasmRepaintGate.DrainDirty(out var again));
            Assert.Empty(again);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void RequestRepaint_Null_Receiver_Still_Throws()
    {
        Assert.Throws<System.NullReferenceException>(() =>
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(null));
    }

    [Fact]
    public void RequestRepaint_Rect_Overloads_Union_Per_Control()
    {
        try
        {
            using var control = new System.Windows.Forms.Control();
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, new WfRect(10, 10, 20, 20));
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, new WfRect(30, 30, 20, 20), false);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.Equal(new WfRect(10, 10, 40, 40), dirty[control]);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void RequestRepaint_Full_Wins_Over_Rect()
    {
        try
        {
            using var control = new System.Windows.Forms.Control();
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, new WfRect(10, 10, 20, 20));
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control);
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, new WfRect(30, 30, 20, 20));
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.Null(dirty[control]);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void RequestRepaint_Rect_With_Children_Marks_Descendants_Full()
    {
        try
        {
            using var parent = new System.Windows.Forms.Control();
            using var child = new System.Windows.Forms.Control();
            parent.Controls.Add(child);
            var rc = new WfRect(10, 10, 20, 20);
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(parent, rc, true);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.Equal(rc, dirty[parent]);
            Assert.True(dirty.ContainsKey(child));
            Assert.Null(dirty[child]);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void RequestRepaint_Region_Overload_Scopes_Dirty_Rect()
    {
        try
        {
            using var control = new System.Windows.Forms.Control();
            // Rect regions record their bounds (the rect-list Region
            // model is exact for finite operands): the host repaints
            // the patch instead of the whole layer.
            using var region = new WfRegion(new WfRect(4, 4, 8, 8));
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, region);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.Equal(new WfRect(4, 4, 8, 8), dirty[control]);

            // Infinite (and null) regions still mark whole-control.
            using var infinite = new WfRegion();
            Assert.True(infinite.IsInfinite(null));
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, infinite);
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, (WfRegion?)null);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var whole);
            Assert.Null(whole[control]);

            // Empty regions keep their empty rect (desktop paints
            // nothing: the host intersects to empty and re-blits).
            using var empty = new WfRegion(new WfRect(4, 4, 8, 8));
            empty.MakeEmpty();
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control, empty);
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var none);
            Assert.Equal(new WfRect(0, 0, 0, 0), none[control]);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void TakeMarkCaller_Samples_Markers_And_Drains()
    {
        bool was = System.Windows.Forms.WasmFrameCounters.PerfEnabled;
        System.Windows.Forms.WasmFrameCounters.PerfEnabled = true;
        try
        {
            using var control = new System.Windows.Forms.Control();
            System.Windows.Forms.WasmRepaintGate.TakeMarkCaller();
            // 1-in-8 sampling: 16 marks guarantee a sample either way.
            for (int i = 0; i < 16; i++)
                System.Windows.Forms.WasmRepaintGate.RequestRepaint(control);
            var census = System.Windows.Forms.WasmRepaintGate.TakeMarkCaller();
            Assert.NotEmpty(census);
            Assert.True(census.Values.Sum() >= 1);
            Assert.Empty(System.Windows.Forms.WasmRepaintGate.TakeMarkCaller());
            // First-marker capture names the direct marker + originator.
            System.Windows.Forms.WasmRepaintGate.TakeFirstMarkCaller();
            System.Windows.Forms.WasmRepaintGate.RequestRepaint(control);
            string? first = System.Windows.Forms.WasmRepaintGate.TakeFirstMarkCaller();
            Assert.NotNull(first);
            Assert.Contains("TakeMarkCaller", first);
            Assert.Null(System.Windows.Forms.WasmRepaintGate.TakeFirstMarkCaller());
        }
        finally
        {
            System.Windows.Forms.WasmFrameCounters.PerfEnabled = was;
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
            // RequestRepaint above accumulates live frame marks; reset so
            // mark-snapshot tests keep their clean-slate assumption.
            System.Windows.Forms.WasmFrameCounters.ResetFrame();
            System.Windows.Forms.WasmFrameCounters.TakeMarks();
        }
    }

    [Fact]
    public void Wake_Without_Subscribers_Neither_Throws_Nor_Dirties()
    {
        bool was = System.Windows.Forms.WasmFrameCounters.PerfEnabled;
        System.Windows.Forms.WasmFrameCounters.PerfEnabled = true;
        try
        {
            System.Windows.Forms.WasmRepaintGate.Wake();
            Assert.False(System.Windows.Forms.WasmRepaintGate.HasDirty);
            System.Windows.Forms.WasmRepaintGate.TakeMarkCaller();
        }
        finally
        {
            System.Windows.Forms.WasmFrameCounters.PerfEnabled = was;
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }

    [Fact]
    public void MarkDirtyChainRect_Translates_Up_And_Stops_Outside()
    {
        try
        {
            using var parent = new System.Windows.Forms.Control { Width = 100, Height = 100 };
            using var child = new System.Windows.Forms.Control { Left = 10, Top = 20, Width = 50, Height = 50 };
            parent.Controls.Add(child);
            System.Windows.Forms.WasmRepaintGate.MarkDirtyChainRect(child, new WfRect(5, 5, 10, 10));
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var dirty);
            Assert.Equal(new WfRect(5, 5, 10, 10), dirty[child]);
            Assert.Equal(new WfRect(15, 25, 10, 10), dirty[parent]);

            // A rect outside the child surface stops the walk: the leaf
            // unions it, but ancestors keep their prior region.
            System.Windows.Forms.WasmRepaintGate.MarkDirtyChainRect(child, new WfRect(1000, 1000, 10, 10));
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out var again);
            Assert.Equal(new WfRect(1000, 1000, 10, 10), again[child]);
            Assert.False(again.ContainsKey(parent));
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.DrainDirty(out _);
        }
    }
}
