using System.Numerics;
using WfWasm.Rendering;
using WfWasm.Rendering.Gles;

namespace WfWasm.Rendering.Gles.Tests;

// Device-side correctness for the straight-alpha vertical slice: CPU-lease
// coherence (drain before read/commit), presentation clearing on retained
// compose (no stale pixels on move/remove), and the scissor/blend protocol
// for translucent fills. Protocol-level against the fake driver: the fake
// simulates Clear fills and transfer memory but does NOT rasterize fills or
// blits, so pixel truth on real hardware still needs the browser
// alpha/compose/clip/move scenarios.
public sealed class GlesCompositionCorrectnessTests
{
    private static readonly PixelLayout RgbaStraight = new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);

    private static SurfaceDescriptor Desc(int w = 32, int h = 24) => new(w, h, 1, RgbaStraight);

    private static RenderDeviceOptions Opt(int pw = 64, int ph = 48, long budget = 4 * 1024 * 1024) =>
        new("wfcanvas-test", new SurfaceDescriptor(pw, ph, 1, RgbaStraight), new(budget));

    private static async Task<(GlesRenderDevice Device, FakeGlesContext Gl)> CreateAsync(RenderDeviceOptions? options = null)
    {
        var drivers = new FakeGlesContextFactory();
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

    private static byte[] Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = a;
        }
        return pixels;
    }

    [Fact]
    public async Task CpuRead_ObservesQueuedDrawing_WithoutExplicitSubmit()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 8));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        // No Submit: the lease must still observe program-order pixels.
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 16, 8), CpuPixelAccess.Read, CpuAccessReason.GetPixel))
        {
            Assert.Equal(Solid(16, 8, 0xFF, 0x00, 0x00, 0xFF), lease.ReadablePixels.ToArray());
        }
        Assert.Equal(1, device.Surfaces.Describe(snap.Handle).ContentVersion);
        int clears = gl.CallsOf("Clear").Count;
        Assert.Equal(1, clears);
        await device.SubmitAsync();
        Assert.Equal(clears, gl.CallsOf("Clear").Count);
    }

    [Fact]
    public async Task CpuWrite_Commit_LandsOnTopOfEarlierQueuedDrawing()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(8, 8));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(2, 2, 4, 4), CpuPixelAccess.Write, CpuAccessReason.LockBits))
        {
            var span = lease.WritablePixels.Span;
            for (int i = 0; i < span.Length; i++) span[i] = (byte)(i % 251);
            lease.Commit();
        }
        // Program order: the queued clear executes before the committed upload.
        Assert.Equal(1, gl.CallsOf("Clear").Count);
        Assert.Equal(1, gl.CallsOf("Upload").Count);
        Assert.True(gl.LastIndexOf("Clear") < gl.LastIndexOf("Upload"));
        Assert.Equal(2, device.Surfaces.Describe(snap.Handle).ContentVersion);
        var expected = Solid(8, 8, 0xFF, 0xFF, 0xFF, 0xFF);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 16; x++)
                expected[((y + 2) * 8 + 2) * 4 + x] = (byte)((y * 16 + x) % 251);
        using (var reread = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 8, 8), CpuPixelAccess.Read, CpuAccessReason.Export))
        {
            Assert.Equal(expected, reread.ReadablePixels.ToArray());
        }
        int clears = gl.CallsOf("Clear").Count;
        await device.SubmitAsync();
        Assert.Equal(clears, gl.CallsOf("Clear").Count);
    }

    [Fact]
    public async Task Compositor_ClearsPresentation_BeforeLayers_NoStalePixels()
    {
        var (device, gl) = await CreateAsync(Opt(pw: 16, ph: 16));
        var layer = device.Surfaces.Create(Desc(8, 8));
        device.Enqueue(Batch(layer.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("box", device.Surfaces.Describe(layer.Handle), Matrix3x2.Identity, null, 1, 0));
        await device.Compositor.PresentAsync(scene.Capture());
        // One clear for the queued layer paint, one for the presentation erase.
        Assert.Equal(2, gl.CallsOf("Clear").Count);
        Assert.True(gl.LastIndexOf("Clear") < gl.Log.FindIndex(c => c.Kind == "Blit"));
        // Removing the only layer and recomposing must erase it: no stale pixels.
        await device.Compositor.PresentAsync(new RetainedScene(device.Identity).Capture());
        Assert.Equal(3, gl.CallsOf("Clear").Count);
        var pixels = new byte[16 * 16 * 4];
        gl.ReadPixels(0, new PixelRect(0, 0, 16, 16), pixels.AsSpan(), 16 * 4);
        Assert.Equal(new byte[16 * 16 * 4], pixels);
    }

    [Fact]
    public async Task PlacementOnly_Recompose_TransfersNoPixels_KeepsVersion()
    {
        var (device, gl) = await CreateAsync();
        var layer = device.Surfaces.Create(Desc(8, 8));
        device.Enqueue(Batch(layer.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("box", device.Surfaces.Describe(layer.Handle), Matrix3x2.Identity, null, 1, 0));
        var presenter = new RetainedFramePresenter(device, scene);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        long version = device.Surfaces.Describe(layer.Handle).ContentVersion;
        int logAfterFirst = gl.Log.Count;
        scene.SetPlacement("box", Matrix3x2.CreateTranslation(4, 4), null, 1, 0);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        Assert.Equal(version, device.Surfaces.Describe(layer.Handle).ContentVersion);
        Assert.Empty(gl.CallsOf("Upload"));
        Assert.Empty(gl.CallsOf("ReadPixels"));
        Assert.True(gl.Log.Count > logAfterFirst); // Recomposition still draws.
        int logAfterMove = gl.Log.Count;
        Assert.Null(await presenter.PresentIfChangedAsync());
        Assert.Equal(logAfterMove, gl.Log.Count); // Idle presents nothing.
    }

    [Fact]
    public async Task ScissoredTranslucentFill_EmitsScissorUnderSourceOver()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(32, 32));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetClip(RectPath(4, 4, 8, 8), ClipOperation.Replace),
            new FillPath(RectPath(0, 0, 32, 32), new SolidBrushSnapshot(new RenderColor(0xFF000080)))));
        await device.SubmitAsync();
        var scissors = gl.CallsOf("Scissor").Select(c => c.Detail).ToList();
        Assert.Equal(2, scissors.Count);
        Assert.Contains("X = 4", scissors[1]);
        Assert.Contains("Y = 4", scissors[1]);
        Assert.Contains("Width = 8", scissors[1]);
        Assert.Contains("Height = 8", scissors[1]);
        Assert.All(gl.CallsOf("Blend"), c => Assert.Equal("over", c.Detail));
        var fill = Assert.Single(gl.CallsOf("Fill"));
        Assert.Contains("color=FF000080", fill.Detail);
    }

    [Fact]
    public async Task SourceCopyFill_DisablesBlend_WritesExactBits()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetComposite(CompositeOperation.SourceCopy),
            new FillPath(RectPath(0, 0, 16, 16), new SolidBrushSnapshot(new RenderColor(0x12345678)))));
        await device.SubmitAsync();
        var blends = gl.CallsOf("Blend").Select(c => c.Detail).ToList();
        Assert.Equal(2, blends.Count);
        Assert.Equal("over", blends[0]);
        Assert.Equal("copy", blends[1]);
        var fill = Assert.Single(gl.CallsOf("Fill"));
        Assert.Contains("color=12345678", fill.Detail);
        Assert.True(gl.LastIndexOf("Blend") < gl.LastIndexOf("Fill"));
    }

    [Fact]
    public async Task Resize_DiscardsQueuedBatch_WithoutTouchingDeadTarget()
    {
        var (device, gl) = await CreateAsync();
        var first = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(first.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        var resized = device.Surfaces.Resize(first.Handle, Desc(8, 8));
        await device.SubmitAsync(); // Must not bind the deleted framebuffer.
        Assert.Equal(1, device.Counters.DiscardedBatches);
        Assert.Empty(gl.CallsOf("Clear"));
        Assert.Equal(0, resized.ContentVersion);
        using var lease = device.CpuAccess.Acquire(resized.Handle, new PixelRect(0, 0, 8, 8), CpuPixelAccess.Read, CpuAccessReason.Export);
        Assert.Equal(new byte[8 * 8 * 4], lease.ReadablePixels.ToArray());
    }
}
