extern alias WfDraw;

using System;
using System.Resources;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfHelper = WfDraw::WfWasm.Drawing.ResourceHelper;
using Xunit;

public sealed class ResourceMissHealTests
{
    private sealed class FlipFlopManager : ResourceManager
    {
        public Func<object?>? Behavior;
        public override object? GetObject(string name) =>
            Behavior != null ? Behavior() : throw new MissingManifestResourceException();
    }

    [Fact]
    public void Miss_Heals_After_Assembly_Set_Grows()
    {
        // Misses are versioned by the loaded-assembly generation: a probe
        // that runs before the owning assembly arrives (FilteringIcon
        // before GridView) must re-probe once the set grows instead of
        // serving the stale miss forever.
        var rm = new FlipFlopManager { Behavior = () => throw new MissingManifestResourceException() };
        var before = Assert.IsType<WfBitmap>(WfHelper.SafeGetObject(rm, "FilteringIcon"));
        Assert.Equal(32, before.Width);
        WfHelper.NoteAssemblyLoaded();
        var expected = new object();
        rm.Behavior = () => expected;
        Assert.Same(expected, WfHelper.SafeGetObject(rm, "FilteringIcon"));
    }

    [Fact]
    public void Miss_Sticks_While_Assembly_Set_Stable()
    {
        // Without a load, the miss still skips the doomed re-throw: the
        // second lookup must not call back into the manager (placeholders
        // stay uncached, so fresh instances each time).
        int calls = 0;
        var rm = new FlipFlopManager
        {
            Behavior = () => { calls++; throw new MissingManifestResourceException(); },
        };
        var first = Assert.IsType<WfBitmap>(WfHelper.SafeGetObject(rm, "FilteringIcon"));
        var second = Assert.IsType<WfBitmap>(WfHelper.SafeGetObject(rm, "FilteringIcon"));
        Assert.NotSame(first, second);
        Assert.Equal(1, calls);
    }
}
