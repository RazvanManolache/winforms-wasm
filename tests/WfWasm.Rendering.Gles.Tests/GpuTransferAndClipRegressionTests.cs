using System.Numerics;
using WfWasm.Rendering;
using WfWasm.Rendering.Gles;

namespace WfWasm.Rendering.Gles.Tests;

// GPU REGRESSION (device protocol): transfer geometry and clip/state mapping
// at the managed translator boundary. The fake driver simulates transfer
// memory (uploads/reads are byte-exact here) but does NOT rasterize
// Fill/Blit commands, so these pin the regions, strides, scissor rects,
// blend transitions and opacity values the real binding receives. Hardware
// pixel truth for the same scenarios lives in the binding pixel suite.
// Complements (never duplicates) the milestone and alpha/composition pins:
// subimage offsets, off-center partial reads, padded strides, translated
// and empty clips, layer opacity across moves, and exact translucent
// clears under SourceCopy.
public sealed class GpuTransferAndClipRegressionTests
{
    private static readonly PixelLayout RgbaStraight = new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);

    private static SurfaceDescriptor Desc(int w = 32, int h = 24) => new(w, h, 1, RgbaStraight);

    private static RenderDeviceOptions Opt(int pw = 64, int ph = 48, long budget = 4 * 1024 * 1024) =>
        new("wfcanvas-transfer-reg", new SurfaceDescriptor(pw, ph, 1, RgbaStraight), new(budget));

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
    public async Task Subimage_Write_At_Nonzero_Offset_Reads_Back_Exact()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0xFFFFFFFF))));
        byte[] pattern = new byte[4 * 4 * 4];
        for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)((i * 37 + 11) % 251);
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(2, 3, 4, 4), CpuPixelAccess.Write, CpuAccessReason.LockBits))
        {
            pattern.CopyTo(lease.WritablePixels.Span);
            lease.Commit();
        }
        // The device must hand the binding the exact subimage region, not a
        // full-surface upload: nonzero x/y offsets are the historically
        // broken axis (top-left vs bottom-left origins).
        var upload = gl.CallsOf("Upload")[^1];
        Assert.Contains("X = 2", upload.Detail);
        Assert.Contains("Y = 3", upload.Detail);
        Assert.Contains("Width = 4", upload.Detail);
        Assert.Contains("Height = 4", upload.Detail);
        using (var reread = device.CpuAccess.Acquire(snap.Handle, new PixelRect(2, 3, 4, 4), CpuPixelAccess.Read, CpuAccessReason.Export))
        {
            Assert.Equal(pattern, reread.ReadablePixels.ToArray());
        }
        // Neighbors outside the subimage are untouched white.
        using (var above = device.CpuAccess.Acquire(snap.Handle, new PixelRect(2, 2, 4, 1), CpuPixelAccess.Read, CpuAccessReason.Export))
        {
            Assert.Equal(Solid(4, 1, 0xFF, 0xFF, 0xFF, 0xFF), above.ReadablePixels.ToArray());
        }
        using (var right = device.CpuAccess.Acquire(snap.Handle, new PixelRect(6, 3, 1, 1), CpuPixelAccess.Read, CpuAccessReason.Export))
        {
            Assert.Equal(Solid(1, 1, 0xFF, 0xFF, 0xFF, 0xFF), right.ReadablePixels.ToArray());
        }
    }

    [Fact]
    public async Task OffCenter_Partial_Read_Returns_Exact_Pixel()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(snap.Handle, 0, new ClearSurface(new RenderColor(0x0000FFFF))));
        using (var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(5, 7, 4, 4), CpuPixelAccess.Write, CpuAccessReason.LockBits))
        {
            lease.WritablePixels.Span.Fill(0xAB);
            lease.Commit();
        }
        // The readback region handed to the binding must carry the nonzero
        // x/y through exactly; a dropped y offset returns the wrong row.
        using (var inside = device.CpuAccess.Acquire(snap.Handle, new PixelRect(6, 8, 1, 1), CpuPixelAccess.Read, CpuAccessReason.GetPixel))
        {
            Assert.Equal(new byte[] { 0xAB, 0xAB, 0xAB, 0xAB }, inside.ReadablePixels.ToArray());
        }
        using (var outside = device.CpuAccess.Acquire(snap.Handle, new PixelRect(4, 6, 1, 1), CpuPixelAccess.Read, CpuAccessReason.GetPixel))
        {
            Assert.Equal(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, outside.ReadablePixels.ToArray());
        }
        var reads = gl.CallsOf("ReadPixels").Select(c => c.Detail).ToList();
        Assert.Contains(reads, d => d.Contains("X = 6") && d.Contains("Y = 8"));
        Assert.Contains(reads, d => d.Contains("X = 4") && d.Contains("Y = 6"));
    }

    [Fact]
    public async Task Padded_Stride_Image_Import_Preserves_Rows()
    {
        var (device, gl) = await CreateAsync();
        // 3x2 image with 4 padding bytes per row: padding must be stripped,
        // row content must survive verbatim (stride confusion shears rows).
        byte[] pixels = new byte[16 * 2];
        for (int x = 0; x < 3; x++)
        {
            pixels[x * 4] = 9; pixels[x * 4 + 1] = 9; pixels[x * 4 + 2] = 9; pixels[x * 4 + 3] = 255;
            pixels[16 + x * 4] = 7; pixels[16 + x * 4 + 1] = 7; pixels[16 + x * 4 + 2] = 7; pixels[16 + x * 4 + 3] = 128;
        }
        var descriptor = new SurfaceDescriptor(3, 2, 1, RgbaStraight);
        device.Images.Import(new CpuImageSnapshot("padded", 1, descriptor, 16, pixels));
        // The binding receives a tight upload: stride == width * 4.
        var upload = gl.CallsOf("Upload")[^1];
        Assert.Contains("stride=12", upload.Detail);
        byte[] expected = new byte[3 * 4 * 2];
        pixels.AsSpan(0, 12).CopyTo(expected.AsSpan(0, 12));
        pixels.AsSpan(16, 12).CopyTo(expected.AsSpan(12, 12));
        Assert.Equal(expected, gl.LastUploadBytes);
    }

    [Fact]
    public async Task Translated_Clip_Maps_To_Device_Scissor()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(32, 32));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetTransform(Matrix3x2.CreateTranslation(10, 20)),
            new SetClip(RectPath(0, 0, 8, 8), ClipOperation.Replace),
            new FillPath(RectPath(0, 0, 32, 32), new SolidBrushSnapshot(new RenderColor(0xFF0000FF)))));
        await device.SubmitAsync();
        // World (0,0,8,8) under translate(10,20) must reach the binding as
        // device (10,20,8,8): an unmapped clip paints the wrong pixels.
        var scissor = gl.CallsOf("Scissor").Select(c => c.Detail).Last(d => d != "null");
        Assert.Contains("X = 10", scissor);
        Assert.Contains("Y = 20", scissor);
        Assert.Contains("Width = 8", scissor);
        Assert.Contains("Height = 8", scissor);
        Assert.Equal("over", gl.CallsOf("Blend").Last().Detail);
    }

    [Fact]
    public async Task Empty_Clip_Fill_Draws_Nothing_Via_ZeroArea_Scissor()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(32, 32));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetClip(RectPath(0, 0, 0, 0), ClipOperation.Replace),
            new FillPath(RectPath(0, 0, 32, 32), new SolidBrushSnapshot(new RenderColor(0xFF0000FF)))));
        await device.SubmitAsync();
        // Suppression mechanism, pinned: the fill may still be recorded, but
        // the scissor active at fill time must be zero-area so no pixel can
        // be touched. Dropping the scissor instead would paint the surface.
        var log = gl.Log;
        int fill = log.FindLastIndex(c => c.Kind == "Fill");
        Assert.True(fill >= 0);
        int scissor = log.FindLastIndex(fill, fill + 1, c => c.Kind == "Scissor");
        Assert.True(scissor >= 0);
        Assert.Contains("Width = 0", log[scissor].Detail);
        Assert.Contains("Height = 0", log[scissor].Detail);
    }

    [Fact]
    public async Task Layer_Opacity_Survives_Placement_Only_Move()
    {
        var (device, gl) = await CreateAsync();
        var back = device.Surfaces.Create(Desc(16, 16));
        var front = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(back.Handle, 0, new ClearSurface(new RenderColor(0x0000FFFF))));
        device.Enqueue(Batch(front.Handle, 0, new ClearSurface(new RenderColor(0xFF0000FF))));
        await device.SubmitAsync();
        var scene = new RetainedScene(device.Identity);
        scene.SetLayer(new LayerVisual("back", device.Surfaces.Describe(back.Handle), Matrix3x2.Identity, null, 1, 0));
        scene.SetLayer(new LayerVisual("front", device.Surfaces.Describe(front.Handle), Matrix3x2.Identity, null, 0.5f, 1));
        var presenter = new RetainedFramePresenter(device, scene);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        int uploads = gl.CallsOf("Upload").Count;
        scene.SetPlacement("front", Matrix3x2.CreateTranslation(8, 0), null, 0.5f, 1);
        Assert.NotNull(await presenter.PresentIfChangedAsync());
        // Placement-only: no re-upload, and the moved layer re-composites
        // with its opacity intact (a dropped opacity flashes opaque).
        Assert.Equal(uploads, gl.CallsOf("Upload").Count);
        var tail = gl.CallsOf("Blit").TakeLast(2).Select(c => c.Detail).ToList();
        Assert.Equal(2, tail.Count);
        Assert.Single(tail, d => d.Contains("opacity=0.5"));
        Assert.Single(tail, d => d.Contains("opacity=1"));
    }

    [Fact]
    public async Task ClearSurface_Writes_Exact_Bits_Under_SourceCopy()
    {
        var (device, gl) = await CreateAsync();
        var snap = device.Surfaces.Create(Desc(16, 16));
        device.Enqueue(Batch(snap.Handle, 0,
            new SetComposite(CompositeOperation.SourceCopy),
            new ClearSurface(new RenderColor(0xFF000080))));
        await device.SubmitAsync();
        // Clears bypass blending on the real binding too (glClear ignores
        // blend state): translucent clear bytes must land exactly, and the
        // SourceCopy transition must still be visible to the driver.
        Assert.Equal("copy", gl.CallsOf("Blend").Last().Detail);
        using var lease = device.CpuAccess.Acquire(snap.Handle, new PixelRect(0, 0, 16, 16), CpuPixelAccess.Read, CpuAccessReason.Export);
        Assert.Equal(Solid(16, 16, 0xFF, 0x00, 0x00, 0x80), lease.ReadablePixels.ToArray());
    }
}
