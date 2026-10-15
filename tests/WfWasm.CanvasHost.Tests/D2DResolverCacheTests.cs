using WfWasm.CanvasHost;

public sealed class D2DResolverCacheTests
{
    // The D2D paint path resolves leaf device-lifecycle overrides per
    // control per frame; the resolvers cache per type-name (misses
    // included) so steady-state frames do no base-chain walks.
    [Fact]
    public void ResolveD2DCreated_Caches_Per_Type()
    {
        var first = WinFormsAppHost.ResolveD2DCreated(typeof(FakeD2DControl));
        Assert.NotNull(first);
        Assert.Equal("OnDeviceContextCreated", first.Name);
        Assert.Same(first, WinFormsAppHost.ResolveD2DCreated(typeof(FakeD2DControl)));
    }

    [Fact]
    public void ResolveD2DInit_Caches_Per_Type()
    {
        var first = WinFormsAppHost.ResolveD2DInit(typeof(FakeD2DControl));
        Assert.NotNull(first);
        Assert.Equal("OnInvalidateDeviceResources", first.Name);
        Assert.Same(first, WinFormsAppHost.ResolveD2DInit(typeof(FakeD2DControl)));
    }

    [Fact]
    public void Resolvers_Cache_Misses_As_Null()
    {
        Assert.Null(WinFormsAppHost.ResolveD2DCreated(typeof(object)));
        Assert.Null(WinFormsAppHost.ResolveD2DCreated(typeof(object)));
        Assert.Null(WinFormsAppHost.ResolveD2DInit(typeof(object)));
        Assert.Null(WinFormsAppHost.ResolveD2DInit(typeof(object)));
    }

    private sealed class FakeD2DControl
    {
        public void OnDeviceContextCreated() { }
        private void OnInvalidateDeviceResources() { }
    }
}
