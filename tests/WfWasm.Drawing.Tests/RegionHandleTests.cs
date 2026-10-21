extern alias WfDraw;

using WfRegion = WfDraw::System.Drawing.Region;

public sealed class RegionHandleTests
{
    [Fact]
    public void FromHrgn_Zero_Returns_Infinite_Region()
    {
        // GDI-region P/Invokes are stubbed to placeholder handles; a
        // zero handle must degrade to no-clipping, not fault the input
        // that built the shape (RTE Ctrl+Home, eval9).
        using var region = WfRegion.FromHrgn(IntPtr.Zero);
        Assert.True(region.IsInfinite(null));
        Assert.False(region.IsEmpty(null));
    }

    [Fact]
    public void FromHrgn_Nonzero_Returns_Infinite_Region()
    {
        // Opaque (fake) handles carry no recoverable shape; same deal.
        using var region = WfRegion.FromHrgn(new IntPtr(1));
        Assert.True(region.IsInfinite(null));
    }

    [Fact]
    public void FromHrgn_Result_Supports_Combine_Ops()
    {
        // The degraded region must behave like an ordinary infinite one.
        using var region = WfRegion.FromHrgn(IntPtr.Zero);
        using var clone = region.Clone();
        Assert.True(clone.IsInfinite(null));
        region.Intersect(new WfDraw::System.Drawing.Rectangle(0, 0, 10, 10));
        Assert.False(region.IsInfinite(null));
    }
}
