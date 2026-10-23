using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Scroll-strip narrowing math for retained scroll reuse: the host shifts
// the retained surface by the consumed (dx,dy) and repaints only the
// union of the prior dirty rect, its shifted position, and the fresh
// exposed strip. Edit rects are ambiguous-frame (pre- or post-scroll),
// so all three repaint: a superset repaint is always exact. Anything
// this cannot narrow (zero/diagonal/oversize shifts, whole-control or
// near-full unions) returns null and the caller runs the full path.
public sealed class ScrollShiftNarrowTests
{
    private const int W = 800;
    private const int H = 600;

    [Fact]
    public void RightScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, 100, H);
        var narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 200, H), narrow);
    }

    [Fact]
    public void LeftScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(W - 40, 0, 40, H);
        var narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, -40, 0, dirty);
        Assert.Equal(new System.Drawing.Rectangle(W - 80, 0, 80, H), narrow);
    }

    [Fact]
    public void DownScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, W, 30);
        var narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, 0, 30, dirty);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, W, 60), narrow);
    }

    [Fact]
    public void UpScroll_PureStrip_NarrowsToDoubleStrip()
    {
        var dirty = new System.Drawing.Rectangle(0, H - 25, W, 25);
        var narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, 0, -25, dirty);
        Assert.Equal(new System.Drawing.Rectangle(0, H - 50, W, 50), narrow);
    }

    [Fact]
    public void NearbyEdit_StaysNarrow()
    {
        // An edit overlapping the scroll region widens the union but the
        // repaint (300x600 of 800x600) still beats the full path.
        var dirty = new System.Drawing.Rectangle(0, 0, 200, H);
        var narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 300, H), narrow);
    }

    [Fact]
    public void WholeControlDirty_FallsBackToFull()
    {
        var dirty = new System.Drawing.Rectangle(0, 0, W, H);
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty));
    }

    [Fact]
    public void FarEdit_BoundingUnionCoversAll_FallsBackToFull()
    {
        // The scrolled strip plus a far-corner edit bounding-boxes to the
        // whole surface: shifting would save nothing, so skip it.
        var dirty = new System.Drawing.Rectangle(700, 500, 100, 100);
        Assert.Null(WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty));
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
        // the union clamps to the strip alone.
        var dirty = new System.Drawing.Rectangle(-500, -500, 10, 10);
        var narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, 100, 0, dirty);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 100, H), narrow);
    }
}
