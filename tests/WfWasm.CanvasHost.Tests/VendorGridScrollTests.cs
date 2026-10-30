using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Vendor grid metric repair gate: the host recomputes a Telerik
// RadGridView row-scroller range only when headless metrics degenerate
// (Maximum - LargeChange + 1 <= Minimum) while rows provably overflow.
// Healthy metrics are never touched. The live vendor behavior (repair
// value, native scroll, pixel movement) is proven by browser probes.
public sealed class VendorGridScrollTests
{
    [Fact]
    public void Degenerate_ZeroUsableRange()
    {
        // bugsGrid headless: Maximum=250, LargeChange=251.
        Assert.True(WinFormsAppHost.GridScrollDegenerate(0, 250, 251));
    }

    [Fact]
    public void Healthy_PositiveUsableRange()
    {
        Assert.False(WinFormsAppHost.GridScrollDegenerate(0, 442, 251));
        Assert.False(WinFormsAppHost.GridScrollDegenerate(0, 442, 10));
    }

    [Fact]
    public void Degenerate_ExactFitIsNotScrollable()
    {
        // Maximum - LargeChange + 1 == Minimum: a single position.
        Assert.True(WinFormsAppHost.GridScrollDegenerate(0, 250, 251));
        Assert.False(WinFormsAppHost.GridScrollDegenerate(0, 251, 251));
    }
}
