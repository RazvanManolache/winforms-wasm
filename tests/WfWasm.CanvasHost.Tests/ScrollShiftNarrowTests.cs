using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Scroll-strip narrowing math for retained scroll reuse: the host shifts
// the retained surface by the consumed (dx,dy) and repaints only the
// disjoint pieces that need it — the fresh exposed strip plus the prior
// dirty rect and its shifted position (edit rects are ambiguous-frame,
// pre- or post-scroll, so both repaint; a superset repaint is always
// exact). Pieces merge only when the merge adds no gap fill, so Y-disjoint
// dirty/strip scrolls carry as small rects instead of bounding-boxing to
// the full frame; the 90%-gate runs on summed piece areas. Anything this
// cannot narrow (zero/diagonal/oversize shifts, near-full unions) returns
// null and the caller runs the full path.
public sealed class ScrollShiftNarrowTests
{
    private const int W = 800;
    private const int H = 600;

    private static System.Drawing.Rectangle Single(int w, int h, int dx, int dy, System.Drawing.Rectangle dirty)
    {
        System.Drawing.Rectangle[]? narrow = WinFormsAppHost.ComputeScrollNarrow(w, h, dx, dy, dirty);
        Assert.NotNull(narrow);
        return Assert.Single(narrow);
    }

    [Fact]
    public void RightScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, 100, H);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 200, H), Single(W, H, 100, 0, dirty));
    }

    [Fact]
    public void LeftScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(W - 40, 0, 40, H);
        Assert.Equal(new System.Drawing.Rectangle(W - 80, 0, 80, H), Single(W, H, -40, 0, dirty));
    }

    [Fact]
    public void DownScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, W, 30);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, W, 60), Single(W, H, 0, 30, dirty));
    }

    [Fact]
    public void UpScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(0, H - 25, W, 25);
        Assert.Equal(new System.Drawing.Rectangle(0, H - 50, W, 50), Single(W, H, 0, -25, dirty));
    }

    [Fact]
    public void NearbyEdit_StaysNarrow()
    {
        // An edit overlapping the scroll region widens the union but the
        // repaint (300x600 of 800x600) still beats the full path.
        var dirty = new System.Drawing.Rectangle(0, 0, 200, H);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 300, H), Single(W, H, 100, 0, dirty));
    }

    [Fact]
    public void WholeControlDirty_FallsBackToFull()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, W, H);
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty));
    }

    [Fact]
    public void NearFullStrip_FallsBackToFull()
    {
        // A legal-but-huge shift whose strip alone covers >= 90% of the
        // frame saves nothing: skip it.
        var dirty = System.Drawing.Rectangle.Empty;
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(1066, 590, 0, 540, dirty));
    }

    [Fact]
    public void OracleDisjointDirtyStrip_Carries()
    {
        // WF-D-052 oracle: w=1066 h=590 dy=-240, dirty=(1048,16,19,103)
        // (scrollbar strip). The dirty band (rows 16-119) and the exposed
        // strip (rows 350-590) are Y-disjoint: the old bounding-box union
        // covered the full frame and dropped every such scroll. The narrow
        // set must carry as exactly these two pieces (strip + dirty =
        // 257,797 px of 628,940 = 41%, under the 90% gate); the shifted
        // dirty band falls fully off-viewport and contributes nothing.
        var dirty = new System.Drawing.Rectangle(1048, 16, 19, 103);
        System.Drawing.Rectangle[]? narrow = WinFormsAppHost.ComputeScrollNarrow(1066, 590, 0, -240, dirty);
        Assert.NotNull(narrow);
        Assert.Equal(2, narrow.Length);
        Assert.Equal(new System.Drawing.Rectangle(0, 350, 1066, 240), narrow[0]);
        // The oracle dirty band overhangs the 1066px viewport by 1px
        // (1048+19=1067): correctly clipped to 18 wide.
        Assert.Equal(new System.Drawing.Rectangle(1048, 16, 18, 103), narrow[1]);
    }

    [Fact]
    public void FarEdit_CarriesAsDisjointSet()
    {
        // Y-disjoint far edit + strip must carry as disjoint pieces, not
        // bounding-box to full: strip (100x600) + edit (100x100) = 70k px
        // of 480k (14.6%) << 90% gate. The shifted edit band falls
        // fully off-surface and contributes nothing.
        var dirty = new System.Drawing.Rectangle(700, 500, 100, 100);
        System.Drawing.Rectangle[]? narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty);
        Assert.NotNull(narrow);
        Assert.Equal(2, narrow.Length);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 100, H), narrow[0]);
        Assert.Equal(new System.Drawing.Rectangle(700, 500, 100, 100), narrow[1]);
    }

    [Fact]
    public void ZeroShift_ReturnsNull()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, 100, H);
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, 0, 0, dirty));
    }

    [Fact]
    public void DiagonalShift_ReturnsNull()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, 100, H);
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, 10, 10, dirty));
    }

    [Fact]
    public void OversizeShift_ReturnsNull()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, 100, H);
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, W, 0, dirty));
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, 0, -H, dirty));
    }

    [Fact]
    public void OutOfBoundsDirty_ClampsToSurface()
    {
        // A stale rect fully outside the surface contributes nothing;
        // the set narrows to the strip alone.
        var dirty = new System.Drawing.Rectangle(-500, -500, 10, 10);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 100, H), Single(W, H, 100, 0, dirty));
    }
}
