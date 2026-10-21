using System.Numerics;
using WfWasm.Rendering;
using WfWasm.Rendering.Gles;

namespace WfWasm.Rendering.Gles.Tests;

// Milestone-1 protocol tests against the fake driver. They prove resource
// ownership, command translation, disposal and context-loss behavior. They
// prove no GPU execution, pixel correctness, or performance.
public sealed class GlesMilestoneTests
{
    private static readonly PixelLayout RgbaStraight = new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);

    private static SurfaceDescriptor Desc(int w = 32, int h = 24, float dpi = 1) => new(w, h, dpi, RgbaStraight);

    private static RenderDeviceOptions Opt(long budget = 4 * 1024 * 1024, int pw = 64, int ph = 48) =>
        new("wfcanvas-test", new SurfaceDescriptor(pw, ph, 1, RgbaStraight), new(budget));

    private static async Task<(GlesRenderDevice Device, FakeGlesContext Gl)> CreateAsync(
        FakeGlesContextFactory? drivers = null, RenderDeviceOptions? options = null)
    {
        drivers ??= new FakeGlesContextFactory();
        var device = (GlesRenderDevice)await new GlesRenderDeviceFactory(drivers).CreateAsync(options ?? Opt());
        return (device, drivers.Created[^1]);
    }

    private static PathSnapshot RectPath(float x, float y, float w, float h) => new(new[]
    {
        new PathSegment(PathVerb.Move, new Vector2(x, y)),
        new PathSegment(PathVerb.Line, new Vector2(x + w, y)),
        new PathSegment(PathVerb.Line, new Vector2(x + w, y + h)),
        new PathSegment(PathVerb.Line, new Vector2(x, y + h)),
        new PathSegment(PathVerb.Close)
    }, PathFillRule.Winding);

    private static DrawingBatch Batch(SurfaceHandle target, long version, params DrawingCommand[] commands) =>
        new(target, version, commands);

    private static CpuImageSnapshot CpuImage(string id, long version, int w, int h, byte fill = 7, PixelLayout? layout = null)
    {
        var descriptor = new SurfaceDescriptor(w, h, 1, layout ?? RgbaStraight);
        return new CpuImageSnapshot(id, version, descriptor, w * 4, Enumerable.Repeat(fill, w * h * 4).ToArray());
    }

    [Fact]
    public async Task Probe_ReportsHonestMilestoneSubset_WithoutCreatingAContext()
    {
        var drivers = new FakeGlesContextFactory();
        var probe = await new GlesRenderDeviceFactory(drivers).ProbeAsync();
        Assert.Equal(BackendKind.CanvasKitWebGl2, probe.Kind);
        Assert.Equal(BackendAvailability.Available, probe.Availability);
        Assert.Equal(
            DrawingFeatures.Images | DrawingFeatures.SourceCopy |
            DrawingFeatures.CpuPixelAccess | DrawingFeatures.RetainedComposition,
            probe.Features);
        Assert.NotEqual(DrawingFeatures.Display, probe.Features & DrawingFeatures.Display);
        Assert.Contains("milestone", probe.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, drivers.SupportQueries);
        Assert.Empty(drivers.Created);
    }

    [Fact]
    public async Task Probe_And_Create_Fail_Explicitly_WhenDriverIsMissing()
    {
        var drivers = new FakeGlesContextFactory { Supported = false, Detail = "no WebGL2" };
        var factory = new GlesRenderDeviceFactory(drivers);
        var probe = await factory.ProbeAsync();
        Assert.Equal(BackendAvailability.Unsupported, probe.Availability);
        Assert.Equal(DrawingFeatures.None, probe.Features);
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => factory.CreateAsync(Opt()).AsTask());
        Assert.Contains("no WebGL2", ex.Message);
        Assert.Empty(drivers.Created);
    }

    [Fact]
    public async Task RequireGpu_RejectsMilestoneDevice_BeforeAnyCreation()
    {
        var drivers = new FakeGlesContextFactory();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.RequireGpu, Opt(), new GlesRenderDeviceFactory(drivers), null).AsTask());
        Assert.Contains("unavailable", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(drivers.Created);
    }

    [Fact]
    public async Task PreferGpu_NeverSilentlySelectsTheMilestoneSubset()
    {
        var drivers = new FakeGlesContextFactory();
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            RenderSessionFactory.CreateAsync(RenderPolicy.PreferGpu, Opt(), new GlesRenderDeviceFactory(drivers), null).AsTask());
        Assert.Contains("no software adapter", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(drivers.Created);
    }

    [Fact]
    public async Task Create_InitializesPresentationTarget_AndReadyState()
    {
        var drivers = new FakeGlesContextFactory();
        var factory = new GlesRenderDeviceFactory(drivers);
        var device = (GlesRenderDevice)await factory.CreateAsync(Opt());
        Assert.Equal(BackendKind.CanvasKitWebGl2, device.Kind);
        Assert.Equal(RenderDeviceState.Ready, device.State);
        Assert.Equal(GlesRenderDeviceFactory.MilestoneFeatures, device.Features);
        Assert.Equal(factory.SessionId, device.Identity.SessionId);
        Assert.Equal(1, device.Identity.Generation);
        var gl = Assert.Single(drivers.Created);
        Assert.Equal("wfcanvas-test", gl.PresentationTargetId);
        Assert.Equal(64, gl.Presentation.PixelWidth);
    }

    [Fact]
    public async Task Surface_CreateDescribeRelease_Lifecycle()
    {
        var (device, _) = await CreateAsync();
        SurfaceHandle? invalidated = null;
        device.Surfaces.Invalidated += h => invalidated = h;
        var snap = device.Surfaces.Create(Desc());
        Assert.True(device.Surfaces.IsValid(snap.Handle));
        var described = device.Surfaces.Describe(snap.Handle);
        Assert.Equal(snap.Handle, described.Handle);
        Assert.Equal(0, described.ContentVersion);
        Assert.Equal(PixelAuthority.Device, described.Authority);
        device.Surfaces.Release(snap.Handle);
        Assert.True(invalidated.HasValue);
        Assert.Equal(snap.Handle, invalidated.Value);
        Assert.False(device.Surfaces.IsValid(snap.Handle));
        Assert.Throws<InvalidOperationException>(() => device.Surfaces.Describe(snap.Handle));
        Assert.Throws<InvalidOperationException>(() => device.Surfaces.Release(snap.Handle));
    }

    [Fact]
    public async Task Resize_InvalidatesOldHandle_AndDiscardsPixels()
    {
        var (device, _) = await CreateAsync();
        var first = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(first.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        SurfaceHandle? invalidated = null;
        device.Surfaces.Invalidated += h => invalidated = h;
        var resized = device.Surfaces.Resize(first.Handle, Desc(8, 8));
        Assert.Equal(first.Handle, invalidated);
        Assert.False(device.Surfaces.IsValid(first.Handle));
        Assert.Equal(first.Handle.Id, resized.Handle.Id);
        Assert.NotEqual(first.Handle.Generation, resized.Handle.Generation);
        Assert.Equal(0, resized.ContentVersion);
        Assert.True(device.Surfaces.IsValid(resized.Handle));
        using var lease = device.CpuAccess.Acquire(resized.Handle, new PixelRect(0, 0, 8, 8), CpuPixelAccess.Read, CpuAccessReason.Export);
        Assert.Equal(new byte[8 * 8 * 4], lease.ReadablePixels.ToArray());
        Assert.Throws<InvalidOperationException>(() =>
            device.Enqueue(Batch(first.Handle, 1, new ClearSurface(new RenderColor(0xFF0000FF)))));
    }

    [Fact]
    public async Task SurfaceIds_AreNeverRecycled()
    {
        var (device, _) = await CreateAsync();
        var a = device.Surfaces.Create(Desc());
        device.Surfaces.Release(a.Handle);
        var b = device.Surfaces.Create(Desc());
        Assert.NotEqual(a.Handle.Id, b.Handle.Id);
        Assert.False(device.Surfaces.IsValid(a.Handle));
    }

    [Fact]
    public async Task Surface_RejectsNonRgbaStraightLayouts()
    {
        var (device, _) = await CreateAsync();
        var bgra = new SurfaceDescriptor(16, 16, 1, new(PixelChannels.Bgra8, PixelAlpha.Straight, PixelColorSpace.Srgb));
        Assert.Throws<NotSupportedException>(() => device.Surfaces.Create(bgra));
        var premultiplied = new SurfaceDescriptor(16, 16, 1, new(PixelChannels.Rgba8, PixelAlpha.Premultiplied, PixelColorSpace.Srgb));
        Assert.Throws<NotSupportedException>(() => device.Surfaces.Create(premultiplied));
    }

    [Fact]
    public async Task Enqueue_ViaCoordinator_AdvancesVersion_AndDefersExecution()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        var coordinator = new DisplayPaintCoordinator(device);
        bool queued = coordinator.Paint(snap.Handle,
            recorder => recorder.Record(new ClearSurface(new RenderColor(0x00FF00FF))));
        Assert.True(queued);
        Assert.Equal(1, device.Surfaces.Describe(snap.Handle).ContentVersion);
        Assert.DoesNotContain(gl.Log, c => c.Kind == "Clear");
        await device.SubmitAsync();
        Assert.Contains(gl.Log, c => c.Kind == "Clear");
        Assert.Equal(1, device.Counters.Submissions);
        Assert.Equal(1, device.Counters.ExecutedDraws);
        bool empty = coordinator.Paint(snap.Handle, _ => { });
        Assert.False(empty);
        Assert.Equal(1, device.Surfaces.Describe(snap.Handle).ContentVersion);
    }

    [Fact]
    public async Task Enqueue_RejectsStaleContentVersion()
    {
        var (device, _) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF)))));
        Assert.Contains("Stale", ex.Message);
    }

    [Fact]
    public async Task Enqueue_RejectsCrossDeviceHandles()
    {
        var (a, _) = await CreateAsync();
        var (b, _) = await CreateAsync();
        var snap = a.Surfaces.Create(Desc());
        Assert.Throws<InvalidOperationException>(() =>
            b.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF)))));
    }

    [Fact]
    public async Task Enqueue_ReportsUnsupportedFeatures_ByName()
    {
        var (device, _) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        var stroke = new StrokeSnapshot(new SolidBrushSnapshot(new RenderColor(0xFFFFFFFF)), 2, StrokeCap.Round, StrokeJoin.Round, 4);
        Assert.Contains("StrokePath", Assert.Throws<NotSupportedException>(() =>
            device.Enqueue(Batch(snap.Handle, 0, new StrokePath(RectPath(1, 1, 4, 4), stroke)))).Message);
        var gradient = new LinearGradientSnapshot(new Vector2(0, 0), new Vector2(8, 8),
            new[] { new GradientStop(0, new RenderColor(0xFF0000FF)), new GradientStop(1, new RenderColor(0x0000FFFF)) },
            GradientSpread.Clamp, Matrix3x2.Identity);
        Assert.Contains("LinearGradientSnapshot", Assert.Throws<NotSupportedException>(() =>
            device.Enqueue(Batch(snap.Handle, 0, new FillPath(RectPath(1, 1, 4, 4), gradient)))).Message);
        var zigzag = new PathSnapshot(new[]
        {
            new PathSegment(PathVerb.Move, new Vector2(0, 0)),
            new PathSegment(PathVerb.Line, new Vector2(9, 1)),
            new PathSegment(PathVerb.Line, new Vector2(1, 9)),
            new PathSegment(PathVerb.Line, new Vector2(9, 9)),
            new PathSegment(PathVerb.Close)
        }, PathFillRule.Winding);
        Assert.Contains("rectang", Assert.Throws<NotSupportedException>(() =>
            device.Enqueue(Batch(snap.Handle, 0, new FillPath(zigzag, new SolidBrushSnapshot(new RenderColor(0xFFFFFFFF)))))).Message);
        var glyphs = new GlyphRunSnapshot(new FontHandle(device.Identity, 1), 12,
            new[] { new GlyphPlacement(7, Vector2.Zero, new Vector2(8, 0), 0) });
        Assert.Contains("text", Assert.Throws<NotSupportedException>(() =>
            device.Enqueue(Batch(snap.Handle, 0, new DrawGlyphRun(glyphs, Vector2.Zero, new SolidBrushSnapshot(new RenderColor(0xFFFFFFFF)))))).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exclude", Assert.Throws<NotSupportedException>(() =>
            device.Enqueue(Batch(snap.Handle, 0, new SetClip(RectPath(0, 0, 8, 8), ClipOperation.Exclude)))).Message);
        Assert.Equal(0, device.Surfaces.Describe(snap.Handle).ContentVersion);
    }

    [Fact]
    public async Task Import_CachesByVersion_AndConvertsBgraExplicitly()
    {
        var (device, gl) = await CreateAsync();
        var first = device.Images.Import(CpuImage("tile", 3, 4, 2));
        Assert.Equal(4 * 2 * 4, device.Counters.UploadedBytes);
        var cached = device.Images.Import(CpuImage("tile", 3, 4, 2, fill: 9));
        Assert.Equal(first, cached);
        Assert.Equal(4 * 2 * 4, device.Counters.UploadedBytes);
        var next = device.Images.Import(CpuImage("tile", 4, 4, 2));
        Assert.NotEqual(first, next);
        var bgraLayout = new PixelLayout(PixelChannels.Bgra8, PixelAlpha.Straight, PixelColorSpace.Srgb);
        var bgra = new CpuImageSnapshot("swizzle", 1, new SurfaceDescriptor(1, 1, 1, bgraLayout), 4, new byte[] { 10, 20, 30, 40 });
        device.Images.Import(bgra);
        Assert.NotNull(gl.LastUploadBytes);
        Assert.True(new byte[] { 30, 20, 10, 40 }.SequenceEqual(gl.LastUploadBytes));
        var premultiplied = new PixelLayout(PixelChannels.Rgba8, PixelAlpha.Premultiplied, PixelColorSpace.Srgb);
        Assert.Throws<NotSupportedException>(() => device.Images.Import(
            new CpuImageSnapshot("pm", 1, new SurfaceDescriptor(1, 1, 1, premultiplied), 4, new byte[] { 1, 2, 3, 4 })));
    }

    [Fact]
    public async Task Release_OfPinnedImage_IsDeferredUntilSubmit()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        var image = device.Images.Import(CpuImage("sprite", 1, 8, 8));
        int textures = gl.LiveTextureCount;
        var blit = new DrawSurfaceImage(image, new RenderRect(0, 0, 8, 8), new RenderRect(0, 0, 8, 8), ImageSampling.Nearest);
        device.Enqueue(Batch(snap.Handle, 0, blit));
        device.Images.Release(image);
        Assert.Equal(textures, gl.LiveTextureCount);
        var ex = Assert.Throws<InvalidOperationException>(() => device.Enqueue(Batch(snap.Handle, 1, blit)));
        Assert.Contains("Re-import", ex.Message);
        await device.SubmitAsync();
        Assert.Equal(textures - 1, gl.LiveTextureCount);
        Assert.Equal(1, device.Counters.DeferredReleases);
    }

    [Fact]
    public async Task CpuRead_ExecutesAfterAnOrderedFlush()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 8));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        using var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 16, 8), CpuPixelAccess.Read, CpuAccessReason.GetPixel);
        Assert.Equal(new PixelRect(0, 0, 16, 8), lease.Region);
        Assert.Equal(RgbaStraight, lease.Layout);
        Assert.Equal(64, lease.RowStride);
        var expected = new byte[16 * 8 * 4];
        for (int i = 0; i < expected.Length; i += 4)
        {
            expected[i] = 0xFF;
            expected[i + 1] = 0x00;
            expected[i + 2] = 0x00;
            expected[i + 3] = 0xFF;
        }
        Assert.Equal(expected, lease.ReadablePixels.ToArray());
        Assert.Throws<InvalidOperationException>(() => lease.WritablePixels);
        Assert.True(gl.LastIndexOf("Flush") < gl.LastIndexOf("ReadPixels"));
        Assert.Equal(16 * 8 * 4, device.Counters.ReadbackBytes);
    }

    [Fact]
    public async Task CpuWrite_CommitsOnlyItsRegion_ExactlyOnce()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(2, 3, 4, 4), CpuPixelAccess.Write, CpuAccessReason.LockBits))
        {
            Assert.Throws<InvalidOperationException>(() => lease.ReadablePixels);
            var span = lease.WritablePixels.Span;
            for (int i = 0; i < span.Length; i++) span[i] = (byte)(i % 251);
            lease.Commit();
            Assert.Throws<InvalidOperationException>(() => lease.Commit());
        }
        Assert.Equal(1, device.Surfaces.Describe(snap.Handle).ContentVersion);
        var upload = gl.CallsOf("Upload")[^1];
        Assert.Contains("X = 2", upload.Detail);
        Assert.Contains("Width = 4", upload.Detail);
        using var reread = device.CpuAccess.Acquire(snap.Handle, new PixelRect(2, 3, 4, 4), CpuPixelAccess.Read, CpuAccessReason.Export);
        var expected = new byte[4 * 4 * 4];
        for (int i = 0; i < expected.Length; i++) expected[i] = (byte)(i % 251);
        Assert.Equal(expected, reread.ReadablePixels.ToArray());
    }

    [Fact]
    public async Task CpuWrite_Uncommitted_DiscardsStagedBytes()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        int uploads = gl.CallsOf("Upload").Count;
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 4, 4), CpuPixelAccess.Write, CpuAccessReason.LockBits))
        {
            lease.WritablePixels.Span.Fill(0xAB);
        }
        Assert.Equal(uploads, gl.CallsOf("Upload").Count);
        Assert.Equal(0, device.Surfaces.Describe(snap.Handle).ContentVersion);
    }

    [Fact]
    public async Task CpuAccess_RejectsOutOfBoundsRegions_AndLostDevices()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            device.CpuAccess.Acquire(snap.Handle, new PixelRect(14, 14, 4, 4), CpuPixelAccess.Read, CpuAccessReason.GetPixel));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 0, 4), CpuPixelAccess.Read, CpuAccessReason.GetPixel));
        gl.SimulateContextLoss();
        Assert.Throws<InvalidOperationException>(() =>
            device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 4, 4), CpuPixelAccess.Read, CpuAccessReason.GetPixel));
    }

    [Fact]
    public async Task Compositor_DrainsQueuedDrawing_ThenPresentsLayersInOrder()
    {
        var (device, gl) = await CreateAsync();
        var back = device.Surfaces.Create(Desc(16, 16));
        var front = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(back.Handle, 0, new ClearSurface(new RenderColor(0x0000FFFF))));
        device.Enqueue(Batch(front.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("front", device.Surfaces.Describe(front.Handle), Matrix3x2.Identity, null, 1, 1));
        scene.SetLayer(new LayerVisual("back", device.Surfaces.Describe(back.Handle), Matrix3x2.Identity, null, 0.5f, 0));
        var presenter = new RetainedFramePresenter(device, scene);
        var receipt = await presenter.PresentIfChangedAsync();
        Assert.NotNull(receipt);
        Assert.Equal(1, receipt.Value.Sequence);
        // 2 surface clears + the presentation-base clear: every present
        // erases framebuffer 0 to transparent black first, so moved/removed
        // layers leave no stale pixels (placement-only recompose contract).
        Assert.Equal(3, gl.CallsOf("Clear").Count);
        Assert.Contains(gl.CallsOf("Clear"), c => c.Detail == "0,0,0,0");
        var blits = gl.CallsOf("Blit");
        Assert.Equal(2, blits.Count);
        Assert.True(gl.LastIndexOf("Clear") < gl.Log.FindIndex(c => c.Kind == "Blit"));
        Assert.Contains("opacity=0.5", blits[0].Detail);
        Assert.Contains("opacity=1", blits[1].Detail);
        int logAfterFirst = gl.Log.Count;
        var idle = await presenter.PresentIfChangedAsync();
        Assert.Null(idle);
        Assert.Equal(logAfterFirst, gl.Log.Count);
    }

    [Fact]
    public async Task Compositor_RejectsStaleRetainedContent()
    {
        var (device, _) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("control", device.Surfaces.Describe(snap.Handle), Matrix3x2.Identity, null, 1, 0));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        var presenter = new RetainedFramePresenter(device, scene);
        await Assert.ThrowsAsync<InvalidOperationException>(() => presenter.PresentIfChangedAsync().AsTask());
        scene.SetPlacement("control", Matrix3x2.Identity, null, 1, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => presenter.PresentIfChangedAsync().AsTask());
        scene.SetLayer(new LayerVisual("control", device.Surfaces.Describe(snap.Handle), Matrix3x2.Identity, null, 1, 0));
        var receipt = await presenter.PresentIfChangedAsync();
        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task ContextLoss_InvalidatesEverything_AndPublishesTheRecoveryIdentity()
    {
        var (device, gl) = await CreateAsync();
        var a = device.Surfaces.Create(Desc());
        var b = device.Surfaces.Create(Desc());
        device.Images.Import(CpuImage("tile", 1, 4, 4));
        device.Enqueue(Batch(a.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        var invalidated = new List<SurfaceHandle>();
        device.Surfaces.Invalidated += invalidated.Add;
        DeviceIdentity? next = null;
        device.GenerationChanged += id => next = id;
        int deletesBefore = gl.CallsOf("DeleteTexture").Count + gl.CallsOf("DeleteFramebuffer").Count;
        gl.SimulateContextLoss();
        Assert.Equal(RenderDeviceState.Lost, device.State);
        Assert.Equal(2, invalidated.Count);
        Assert.NotNull(next);
        Assert.Equal(device.Identity.SessionId, next!.Value.SessionId);
        Assert.Equal(device.Identity.Generation + 1, next.Value.Generation);
        Assert.False(device.Surfaces.IsValid(a.Handle));
        Assert.False(device.Surfaces.IsValid(b.Handle));
        Assert.Equal(deletesBefore, gl.CallsOf("DeleteTexture").Count + gl.CallsOf("DeleteFramebuffer").Count);
        Assert.Throws<InvalidOperationException>(() => device.Surfaces.Create(Desc()));
        Assert.Throws<InvalidOperationException>(() =>
            device.Enqueue(Batch(a.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF)))));
        Assert.Throws<InvalidOperationException>(() => device.Images.Import(CpuImage("tile", 2, 4, 4)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => device.SubmitAsync().AsTask());
        var scene = new RetainedScene(device.Identity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => device.Compositor.PresentAsync(scene.Capture()).AsTask());
        Assert.Equal(1, device.Counters.ContextLosses);
    }

    [Fact]
    public async Task Recovery_CreatesTheNextGeneration_InTheSameSession()
    {
        var drivers = new FakeGlesContextFactory();
        var factory = new GlesRenderDeviceFactory(drivers);
        var lost = (GlesRenderDevice)await factory.CreateAsync(Opt());
        var old = lost.Surfaces.Create(Desc());
        drivers.Created[^1].SimulateContextLoss();
        var recovered = (GlesRenderDevice)await factory.CreateAsync(Opt());
        Assert.Equal(RenderDeviceState.Ready, recovered.State);
        Assert.Equal(lost.Identity.SessionId, recovered.Identity.SessionId);
        Assert.Equal(lost.Identity.Generation + 1, recovered.Identity.Generation);
        Assert.False(recovered.Surfaces.IsValid(old.Handle));
        Assert.Throws<InvalidOperationException>(() => recovered.Surfaces.Describe(old.Handle));
        var fresh = recovered.Surfaces.Create(Desc());
        recovered.Enqueue(Batch(fresh.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        var receipt = await recovered.SubmitAsync();
        Assert.Equal(1, receipt.Sequence);
    }

    [Fact]
    public async Task Dispose_DeletesAllGpuResources_AndIsIdempotent()
    {
        var (device, gl) = await CreateAsync();
        var a = device.Surfaces.Create(Desc());
        var b = device.Surfaces.Create(Desc());
        device.Images.Import(CpuImage("tile", 1, 4, 4));
        device.Enqueue(Batch(a.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        await device.DisposeAsync();
        Assert.Equal(RenderDeviceState.Disposed, device.State);
        Assert.Equal(0, gl.LiveTextureCount);
        Assert.Equal(0, gl.LiveFboCount);
        Assert.True(gl.IsDisposed);
        Assert.False(device.Surfaces.IsValid(a.Handle));
        Assert.False(device.Surfaces.IsValid(b.Handle));
        Assert.Throws<ObjectDisposedException>(() => device.Surfaces.Create(Desc()));
        Assert.Throws<ObjectDisposedException>(() =>
            device.Enqueue(Batch(a.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF)))));
        await device.DisposeAsync();
        await device.DrainDisposalsAsync();
    }

    [Fact]
    public async Task TrimToBudget_EvictsOldestUnprotectedSurfaces()
    {
        var options = Opt(budget: 4096 + 256, pw: 8, ph: 8); // Presentation 256 B; room for one 32x32 surface.
        var (device, _) = await CreateAsync(options: options);
        var kept = device.Surfaces.Create(Desc(32, 32));
        device.Enqueue(Batch(kept.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        var newer = device.Surfaces.Create(Desc(32, 32));
        var newest = device.Surfaces.Create(Desc(32, 32));
        var evicted = device.Surfaces.TrimToBudget(new ResourceBudget(4096 + 256));
        Assert.Equal(2, evicted.Count);
        Assert.DoesNotContain(kept.Handle, evicted);
        Assert.True(device.Surfaces.IsValid(kept.Handle));
        Assert.False(device.Surfaces.IsValid(newer.Handle));
        Assert.False(device.Surfaces.IsValid(newest.Handle));
        await device.SubmitAsync();
        Assert.Empty(device.Surfaces.TrimToBudget(new ResourceBudget(4096 + 256)));
    }

    [Fact]
    public async Task TrimToBudget_EvictsImagesBeforeSurfaces()
    {
        var options = Opt(budget: 4096 + 256, pw: 8, ph: 8);
        var (device, _) = await CreateAsync(options: options);
        var surface = device.Surfaces.Create(Desc(32, 32));
        var image = device.Images.Import(CpuImage("tile", 1, 32, 32));
        var evicted = device.Surfaces.TrimToBudget(new ResourceBudget(4096 + 256));
        Assert.Empty(evicted);
        Assert.True(device.Surfaces.IsValid(surface.Handle));
        var blit = new DrawSurfaceImage(image, new RenderRect(0, 0, 32, 32), new RenderRect(0, 0, 32, 32), ImageSampling.Nearest);
        var ex = Assert.Throws<InvalidOperationException>(() => device.Enqueue(Batch(surface.Handle, 0, blit)));
        Assert.Contains("evicted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Counters_MeasureActualTransfers_NeverLabels()
    {
        var (device, _) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(8, 8));
        device.Images.Import(CpuImage("tile", 1, 4, 4));
        device.Enqueue(Batch(snap.Handle, 0,
            new ClearSurface(new RenderColor(0xFFFFFFFF)),
            new FillPath(RectPath(0, 0, 4, 4), new SolidBrushSnapshot(new RenderColor(0xFF0000FF)))));
        await device.SubmitAsync();
        using (device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 8, 8), CpuPixelAccess.Read, CpuAccessReason.Export)) { }
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 2, 2), CpuPixelAccess.Write, CpuAccessReason.LockBits)) lease.Commit();
        var counters = device.Counters;
        Assert.Equal(1, counters.Submissions);
        Assert.Equal(2, counters.ExecutedDraws);
        Assert.Equal(0, counters.DiscardedBatches);
        Assert.Equal(64 + 16, counters.UploadedBytes);
        Assert.Equal(256, counters.ReadbackBytes);
        Assert.Equal(256 + 64 + 64 * 48 * 4, counters.ResidentBytes);
    }

    [Fact]
    public async Task TextService_ReportsItselfAsUnimplemented()
    {
        var (device, _) = await CreateAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            device.Text.RegisterFontAsync("Arial", new byte[] { 0, 1, 2, 3 }, CancellationToken.None).AsTask());
        Assert.Throws<NotSupportedException>(() =>
            device.Text.Shape(new TextRequest("hi", new FontHandle(device.Identity, 1), 12, 100, false)));
        Assert.Throws<NotSupportedException>(() => device.Text.Release(new FontHandle(device.Identity, 1)));
    }

    [Fact]
    public async Task Clip_IsFrozenInDeviceSpace_AndClearIgnoresIt()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(32, 32));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetTransform(Matrix3x2.CreateTranslation(10, 20)),
            new SetClip(RectPath(0, 0, 8, 8), ClipOperation.Replace),
            new SetTransform(Matrix3x2.Identity),
            new FillPath(RectPath(0, 0, 32, 32), new SolidBrushSnapshot(new RenderColor(0xFFFFFFFF))),
            new ClearSurface(new RenderColor(0x000000FF))));
        await device.SubmitAsync();
        var scissors = gl.CallsOf("Scissor").Select(c => c.Detail).ToList();
        Assert.Equal(3, scissors.Count);
        Assert.Contains("X = 10", scissors[1]);
        Assert.Contains("Y = 20", scissors[1]);
        Assert.Contains("Width = 8", scissors[1]);
        Assert.Contains("Height = 8", scissors[1]);
        int clearIndex = gl.Log.FindIndex(c => c.Kind == "Clear");
        Assert.Equal("Scissor", gl.Log[clearIndex - 1].Kind);
        Assert.Equal("null", gl.Log[clearIndex - 1].Detail);
    }

    [Fact]
    public async Task Transform_MapsLogicalUnitsToDevicePixels_WithoutDpiRescale()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(64, 64, dpi: 2));
        device.Enqueue(Batch(snap.Handle, 0,
            new FillPath(RectPath(4, 6, 10, 12), new SolidBrushSnapshot(new RenderColor(0xFFFFFFFF)))));
        await device.SubmitAsync();
        var fill = Assert.Single(gl.CallsOf("Fill"));
        Assert.Contains("(4,6)", fill.Detail);
        Assert.Contains("(14,6)", fill.Detail);
        Assert.Contains("(14,18)", fill.Detail);
        Assert.Contains("(4,18)", fill.Detail);
    }
}
