using System.Numerics;
using WfWasm.Rendering;
using WfWasm.Rendering.Gles;

namespace WfWasm.Rendering.Gles.Tests;

// GPU REGRESSION: alpha-contract oracles + composition-protocol pins.
//
// The StraightOver / GlFixedBlend oracles encode the vertical-slice ALPHA
// CONTRACT (straight sRGB RGBA8 end-to-end) that the Playwright hardware
// driver (E:/tmp/gpu_regression.py) asserts against real WebGL2 hardware
// (NVIDIA RTX 5080, ANGLE D3D11). The protocol tests pin the managed
// translator/compositor call sequences those pixels depend on: default
// blend state, SourceCopy transitions, scissor retention under SourceCopy,
// image sampling/opacity plumbing, placement-only recompose without redraw,
// and removal emitting no blit. They run against FakeGlesContext, so they
// prove protocol, never hardware pixels: the hardware verdicts live in
// the driver's report, not here. Placement-only recompose contract
// (accepted 2026-10-19 after review): every present erases framebuffer 0
// to the transparent-black base BEFORE re-blitting, so moved/removed
// layers leave no stale pixels. The per-present clear costs one fullscreen
// erase; the alternative (smearing) is incorrect, so the protocol tests
// below pin the clear rather than forbid it.
public sealed class GpuAlphaRegressionTests
{
    private static readonly PixelLayout RgbaStraight = new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);

    private static SurfaceDescriptor Desc(int w = 32, int h = 24, float dpi = 1) => new(w, h, dpi, RgbaStraight);

    private static RenderDeviceOptions Opt(long budget = 4 * 1024 * 1024, int pw = 64, int ph = 48) =>
        new("wfcanvas-gpureg", new SurfaceDescriptor(pw, ph, 1, RgbaStraight), new(budget));

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

    // Straight-alpha SourceOver in sRGB bytes, un-premultiplied, row 0 = top.
    // Integer-stable: no .5 ties occur for the pinned scenario values.
    private static (int R, int G, int B, int A) StraightOver((int R, int G, int B, int A) s, (int R, int G, int B, int A) d)
    {
        double sa = s.A / 255.0, da = d.A / 255.0;
        double ao = sa + da * (1 - sa);
        if (ao <= 0) return (0, 0, 0, 0);
        static int Q(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return (
            Q((s.R * sa + d.R * da * (1 - sa)) / ao),
            Q((s.G * sa + d.G * da * (1 - sa)) / ao),
            Q((s.B * sa + d.B * da * (1 - sa)) / ao),
            Q(ao * 255));
    }

    // Fixed-function gl.blendFunc(SRC_ALPHA, ONE_MINUS_SRC_ALPHA) with the
    // same factors applied to alpha, as gpu-bind.js currently configures.
    // This model reproduces the observed RTX 5080 hardware bytes exactly
    // (S1a/S1b/S2 scenarios); it is the documented gap, not the contract.
    private static (int R, int G, int B, int A) GlFixedBlend((int R, int G, int B, int A) s, (int R, int G, int B, int A) d)
    {
        static int Q(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
        double sa = s.A / 255.0;
        return (
            Q(s.R * sa + d.R * (1 - sa)),
            Q(s.G * sa + d.G * (1 - sa)),
            Q(s.B * sa + d.B * (1 - sa)),
            Q(s.A * sa + d.A * (1 - sa)));
    }

    [Fact]
    public void AlphaContract_HalfRedOverTransparent_StaysStraightRed()
    {
        // Hardware scenario S1a oracle: no darkening, alpha preserved.
        Assert.Equal((255, 0, 0, 128), StraightOver((255, 0, 0, 128), (0, 0, 0, 0)));
    }

    [Fact]
    public void AlphaContract_HalfRedOverOpaqueBlue_IsMidpoint()
    {
        // Hardware scenarios S1b/S2/S3 oracle: exact 50%-of-255 split.
        Assert.Equal((128, 0, 127, 255), StraightOver((255, 0, 0, 128), (0, 0, 255, 255)));
    }

    [Fact]
    public void AlphaContract_FixedFunctionBlend_ManglesTranslucentAlpha()
    {
        // Counter-evidence, pinned analytically: the current
        // (SRC_ALPHA, ONE_MINUS_SRC_ALPHA) configuration squares the alpha
        // over transparent (S1a hardware bytes 0x80000040) and scales the
        // destination alpha even over opaque blue (S1b bytes 0x80007FBF).
        Assert.Equal((128, 0, 0, 64), GlFixedBlend((255, 0, 0, 128), (0, 0, 0, 0)));
        Assert.Equal((128, 0, 127, 191), GlFixedBlend((255, 0, 0, 128), (0, 0, 255, 255)));
        Assert.NotEqual(StraightOver((255, 0, 0, 128), (0, 0, 0, 0)),
            GlFixedBlend((255, 0, 0, 128), (0, 0, 0, 0)));
    }

    [Fact]
    public void AlphaContract_TranslucentCascade_DoubleDarkensUnderFixedBlend()
    {
        // Counter-evidence for scenario S2: composing the S1a result over
        // blue must yield the S1b midpoint under the contract, but the
        // fixed-function cascade yields the observed S2 bytes 0x2000BFCF.
        var surface = StraightOver((255, 0, 0, 128), (0, 0, 0, 0));
        Assert.Equal((128, 0, 127, 255), StraightOver(surface, (0, 0, 255, 255)));
        var gapSurface = GlFixedBlend((255, 0, 0, 128), (0, 0, 0, 0));
        Assert.Equal((32, 0, 191, 207), GlFixedBlend(gapSurface, (0, 0, 255, 255)));
    }

    [Fact]
    public async Task Translator_DefaultBatch_IsSourceOver_WithoutExtraBlendCall()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        device.Enqueue(Batch(snap.Handle, 0,
            new FillPath(RectPath(0, 0, 8, 8), new SolidBrushSnapshot(new RenderColor(0xFF0000FF)))));
        await device.SubmitAsync();
        // Only the executor's unconditional batch-start SourceOver state.
        var blends = gl.CallsOf("Blend").Select(c => c.Detail).ToList();
        var only = Assert.Single(blends);
        Assert.Equal("over", only);
    }

    [Fact]
    public async Task Translator_SourceCopy_EmitsCopy_ThenRestoresOver()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        var red = new SolidBrushSnapshot(new RenderColor(0xFF000080));
        device.Enqueue(Batch(snap.Handle, 0,
            new FillPath(RectPath(0, 0, 8, 8), red),
            new SetComposite(CompositeOperation.SourceCopy),
            new FillPath(RectPath(0, 0, 8, 8), red),
            new SetComposite(CompositeOperation.SourceOver),
            new FillPath(RectPath(0, 0, 8, 8), red)));
        await device.SubmitAsync();
        var blends = gl.CallsOf("Blend").Select(c => c.Detail).ToList();
        Assert.Equal(3, blends.Count);
        Assert.Equal("over", blends[0]);
        Assert.Equal("copy", blends[1]);
        Assert.Equal("over", blends[2]);
        Assert.Equal(3, gl.CallsOf("Fill").Count);
    }

    [Fact]
    public async Task Translator_ClippedSourceCopyFill_KeepsScissor()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(32, 32));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetTransform(Matrix3x2.CreateTranslation(10, 20)),
            new SetClip(RectPath(0, 0, 8, 8), ClipOperation.Replace),
            new SetTransform(Matrix3x2.Identity),
            new SetComposite(CompositeOperation.SourceCopy),
            new FillPath(RectPath(0, 0, 32, 32), new SolidBrushSnapshot(new RenderColor(0xFF0000FF)))));
        await device.SubmitAsync();
        int scissor = gl.Log.FindIndex(c => c.Kind == "Scissor" && c.Detail.Contains("Width = 8"));
        int blend = gl.LastIndexOf("Blend");
        int fill = gl.LastIndexOf("Fill");
        Assert.True(scissor >= 0 && scissor < blend && blend < fill);
        Assert.DoesNotContain(gl.Log.Skip(blend).Take(fill - blend),
            c => c.Kind == "Scissor" && c.Detail == "null");
        Assert.Contains("copy", gl.CallsOf("Blend").Last().Detail);
    }

    [Fact]
    public async Task Translator_ImageSampling_AndOpacity_ReachBlit()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc());
        var image = device.Images.Import(CpuImage("sprite", 1, 8, 8));
        device.Enqueue(Batch(snap.Handle, 0,
            new DrawSurfaceImage(image, new RenderRect(0, 0, 8, 8), new RenderRect(0, 0, 8, 8), ImageSampling.Nearest)));
        device.Enqueue(Batch(snap.Handle, 1,
            new DrawSurfaceImage(image, new RenderRect(0, 0, 8, 8), new RenderRect(8, 8, 8, 8), ImageSampling.Linear)));
        await device.SubmitAsync();
        var blits = gl.CallsOf("Blit").Select(c => c.Detail).ToList();
        Assert.Equal(2, blits.Count);
        Assert.Contains("Nearest", blits[0]);
        Assert.Contains("Linear", blits[1]);
        Assert.All(blits, detail => Assert.Contains("opacity=1", detail));
    }

    [Fact]
    public async Task Compositor_MoveRecompose_ReusesContent_WithoutRedraw()
    {
        var (device, gl) = await CreateAsync();
        var back = device.Surfaces.Create(Desc(16, 16));
        var front = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(back.Handle, 0, new ClearSurface(new RenderColor(0x0000FFFF))));
        device.Enqueue(Batch(front.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("back", device.Surfaces.Describe(back.Handle), Matrix3x2.Identity, null, 1, 0));
        scene.SetLayer(new LayerVisual("front", device.Surfaces.Describe(front.Handle), Matrix3x2.Identity, null, 1, 1));
        var presenter = new RetainedFramePresenter(device, scene);
        var first = await presenter.PresentIfChangedAsync();
        Assert.NotNull(first);
        long backVersion = device.Surfaces.Describe(back.Handle).ContentVersion;
        long frontVersion = device.Surfaces.Describe(front.Handle).ContentVersion;
        int clears = gl.CallsOf("Clear").Count;
        int uploads = gl.CallsOf("Upload").Count;
        int blits = gl.CallsOf("Blit").Count;
        // Placement-only move: same surface, same content version.
        scene.SetPlacement("front", Matrix3x2.CreateTranslation(8, 0), null, 1, 1);
        var second = await presenter.PresentIfChangedAsync();
        Assert.NotNull(second);
        Assert.Equal(first.Value.Sequence + 1, second.Value.Sequence);
        // Exactly one added erase (the transparent presentation base),
        // then both layers re-blit; no uploads, no redraw.
        Assert.Equal(clears + 1, gl.CallsOf("Clear").Count);
        Assert.Equal("0,0,0,0", gl.CallsOf("Clear")[^1].Detail);
        Assert.Equal(uploads, gl.CallsOf("Upload").Count);
        Assert.Equal(blits + 2, gl.CallsOf("Blit").Count);
        Assert.Equal(backVersion, device.Surfaces.Describe(back.Handle).ContentVersion);
        Assert.Equal(frontVersion, device.Surfaces.Describe(front.Handle).ContentVersion);
    }

    [Fact]
    public async Task Compositor_RemovedLayer_EmitsNoBlit_AndNeverClearsPresentation()
    {
        var (device, gl) = await CreateAsync();
        var back = device.Surfaces.Create(Desc(16, 16));
        var front = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(back.Handle, 0, new ClearSurface(new RenderColor(0x0000FFFF))));
        device.Enqueue(Batch(front.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("back", device.Surfaces.Describe(back.Handle), Matrix3x2.Identity, null, 1, 0));
        scene.SetLayer(new LayerVisual("front", device.Surfaces.Describe(front.Handle), Matrix3x2.Identity, null, 1, 1));
        var presenter = new RetainedFramePresenter(device, scene);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        Assert.Equal(2, gl.CallsOf("Blit").Count);
        int clearsAfterFirst = gl.CallsOf("Clear").Count;
        Assert.True(scene.Remove("front"));
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        // Only the surviving layer re-blits ...
        Assert.Equal(3, gl.CallsOf("Blit").Count);
        // ... over a freshly erased presentation base: each present clears
        // framebuffer 0 to transparent black BEFORE recomposing, so the
        // removed layer's old pixels cannot survive (placement-only
        // recompose contract; scenes must cover the opaque canvas).
        Assert.Contains(gl.CallsOf("Bind"), c => c.Detail == "0");
        Assert.Equal(clearsAfterFirst + 1, gl.CallsOf("Clear").Count);
        Assert.Equal("0,0,0,0", gl.CallsOf("Clear")[^1].Detail);
        Assert.True(gl.LastIndexOf("Clear") < gl.LastIndexOf("Blit"));
    }

    [Fact]
    public async Task CpuWrite_Commit_RequiresLayerRefresh_BeforePresent()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0x0000FFFF))));
        await device.SubmitAsync();
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("control", device.Surfaces.Describe(snap.Handle), Matrix3x2.Identity, null, 1, 0));
        var presenter = new RetainedFramePresenter(device, scene);
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 4, 4), CpuPixelAccess.Write, CpuAccessReason.LockBits))
        {
            lease.WritablePixels.Span.Fill(0xD6);
            lease.Commit();
        }
        Assert.Equal(2, device.Surfaces.Describe(snap.Handle).ContentVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => presenter.PresentIfChangedAsync().AsTask());
        scene.SetLayer(new LayerVisual("control", device.Surfaces.Describe(snap.Handle), Matrix3x2.Identity, null, 1, 0));
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        Assert.True(gl.LastIndexOf("Upload") < gl.LastIndexOf("Blit"));
    }
}
