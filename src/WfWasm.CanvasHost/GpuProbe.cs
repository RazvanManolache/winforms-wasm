using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Microsoft.JSInterop;
using WfWasm.Rendering;
using WfWasm.Rendering.Gles;

namespace WfWasm.CanvasHost;

// Real WebGL2 driver binding for the GLES milestone device (IGlesContext).
// Sync [JSImport] calls into wwwroot/gpu-bind.js (raw WebGL2, no libraries),
// following the webgl-fx.js convention. Coordinates are device pixels with a
// top-left origin; transfers are straight sRGB RGBA, row 0 = top. Spans are
// copied to arrays at the boundary (milestone simplicity over zero-copy).
// Explicit opt-in only: constructed solely by the GPU probe ([JSInvokable]
// GpuProbeRun) on a driver-created probe canvas. Production painting is
// untouched; the GPU acceptance gate stays red until the full contract runs.
internal static partial class GpuBind
{
    [JSImport("globalThis.wfwasmGpuCaps")]
    internal static partial string GetCaps();

    [JSImport("globalThis.wfwasmGpuCreate")]
    internal static partial int Create(string canvasId, int pixelWidth, int pixelHeight);

    [JSImport("globalThis.wfwasmGpuDelete")]
    internal static partial void Delete(int contextId);

    [JSImport("globalThis.wfwasmGpuIsLost")]
    internal static partial bool IsLost(int contextId);

    [JSImport("globalThis.wfwasmGpuMakeCurrent")]
    internal static partial void MakeCurrent(int contextId);

    [JSImport("globalThis.wfwasmGpuCreateTexture")]
    internal static partial int CreateTexture(int contextId, int pixelWidth, int pixelHeight);

    [JSImport("globalThis.wfwasmGpuDeleteTexture")]
    internal static partial void DeleteTexture(int contextId, int texture);

    [JSImport("globalThis.wfwasmGpuCreateFramebuffer")]
    internal static partial int CreateFramebuffer(int contextId, int texture);

    [JSImport("globalThis.wfwasmGpuDeleteFramebuffer")]
    internal static partial void DeleteFramebuffer(int contextId, int framebuffer);

    [JSImport("globalThis.wfwasmGpuBindFramebuffer")]
    internal static partial void BindFramebuffer(int contextId, int framebuffer);

    [JSImport("globalThis.wfwasmGpuSetViewport")]
    internal static partial void SetViewport(int contextId, int pixelWidth, int pixelHeight);

    [JSImport("globalThis.wfwasmGpuSetScissor")]
    internal static partial void SetScissor(int contextId, bool enabled, int x, int y, int width, int height);

    [JSImport("globalThis.wfwasmGpuSetSourceOver")]
    internal static partial void SetSourceOver(int contextId, bool sourceOver);

    [JSImport("globalThis.wfwasmGpuClear")]
    internal static partial void Clear(int contextId, float r, float g, float b, float a);

    [JSImport("globalThis.wfwasmGpuFillQuad")]
    internal static partial void FillQuad(int contextId,
        float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3,
        int straightSrgbRgba);

    [JSImport("globalThis.wfwasmGpuBlit")]
    internal static partial void Blit(int contextId,
        float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3,
        int sourceTexture, float sx, float sy, float sw, float sh,
        int texWidth, int texHeight, bool linear, float opacity);

    [JSImport("globalThis.wfwasmGpuUpload")]
    internal static partial void Upload(int contextId, int texture,
        int x, int y, int width, int height, byte[] straightRgba, int rowStride);

    [JSImport("globalThis.wfwasmGpuRead")]
    internal static partial byte[] Read(int contextId, int framebuffer, int x, int y, int width, int height);

    [JSImport("globalThis.wfwasmGpuFlush")]
    internal static partial void Flush(int contextId);
}

internal sealed class WebGlGlesContextFactory : IGlesContextFactory
{
    public bool IsSupported
    {
        get
        {
            try { return (GpuBind.GetCaps() ?? "").StartsWith("ok:", StringComparison.Ordinal); }
            catch { return false; }
        }
    }

    public string SupportDetail
    {
        get
        {
            try { return GpuBind.GetCaps() ?? "no-caps"; }
            catch (Exception ex) { return "caps-failed:" + ex.GetType().Name; }
        }
    }

    public IGlesContext Create(string presentationTargetId, SurfaceDescriptor presentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentationTargetId);
        ArgumentNullException.ThrowIfNull(presentation);
        int id = 0;
        try
        {
            id = GpuBind.Create(presentationTargetId, presentation.PixelWidth, presentation.PixelHeight);
        }
        catch (Exception ex)
        {
            throw new NotSupportedException("WebGL2 context creation failed: " + ex.GetType().Name, ex);
        }
        if (id <= 0)
            throw new NotSupportedException("WebGL2 context creation failed (null canvas or no webgl2).");
        return new WebGlGlesContext(id, presentation.PixelWidth, presentation.PixelHeight);
    }
}

internal sealed class WebGlGlesContext : IGlesContext
{
    private readonly int _id;
    private bool _disposed;

    internal WebGlGlesContext(int id, int pixelWidth, int pixelHeight)
    {
        _id = id;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    internal int PixelWidth { get; }
    internal int PixelHeight { get; }

    private readonly Dictionary<int, (int Width, int Height)> _textureSizes = new();

    public event Action? ContextLost;

    public bool IsLost
    {
        get
        {
            if (_disposed) return true;
            try
            {
                if (GpuBind.IsLost(_id))
                {
                    ContextLost?.Invoke();
                    return true;
                }
                return false;
            }
            catch { return true; }
        }
    }

    private void ThrowIfDead()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WebGlGlesContext));
        if (IsLost) throw new InvalidOperationException("WebGL2 context is lost.");
    }

    public void MakeCurrent()
    {
        ThrowIfDead();
        GpuBind.MakeCurrent(_id);
    }

    public int CreateTexture(int pixelWidth, int pixelHeight)
    {
        ThrowIfDead();
        int texture = GpuBind.CreateTexture(_id, pixelWidth, pixelHeight);
        _textureSizes[texture] = (pixelWidth, pixelHeight);
        return texture;
    }

    public void DeleteTexture(int texture)
    {
        _textureSizes.Remove(texture);
        if (_disposed) return;
        try { GpuBind.DeleteTexture(_id, texture); } catch { }
    }

    public int CreateFramebuffer(int texture)
    {
        ThrowIfDead();
        return GpuBind.CreateFramebuffer(_id, texture);
    }

    public void DeleteFramebuffer(int framebuffer)
    {
        if (_disposed) return;
        try { GpuBind.DeleteFramebuffer(_id, framebuffer); } catch { }
    }

    public void BindFramebuffer(int framebuffer)
    {
        ThrowIfDead();
        GpuBind.BindFramebuffer(_id, framebuffer);
    }

    public void SetViewport(int pixelWidth, int pixelHeight)
    {
        ThrowIfDead();
        GpuBind.SetViewport(_id, pixelWidth, pixelHeight);
    }

    public void SetScissor(PixelRect? scissor)
    {
        ThrowIfDead();
        if (scissor is null)
            GpuBind.SetScissor(_id, false, 0, 0, 0, 0);
        else
            GpuBind.SetScissor(_id, true, scissor.Value.X, scissor.Value.Y, scissor.Value.Width, scissor.Value.Height);
    }

    public void SetSourceOver(bool sourceOver)
    {
        ThrowIfDead();
        GpuBind.SetSourceOver(_id, sourceOver);
    }

    public void Clear(float r, float g, float b, float a)
    {
        ThrowIfDead();
        GpuBind.Clear(_id, r, g, b, a);
    }

    public void FillQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, uint straightSrgbRgba)
    {
        ThrowIfDead();
        GpuBind.FillQuad(_id, p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y,
            unchecked((int)straightSrgbRgba));
    }

    public void DrawTextureQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int sourceTexture,
        RenderRect sourcePixels, ImageSampling sampling, float opacity)
    {
        ThrowIfDead();
        _textureSizes.TryGetValue(sourceTexture, out var size);
        GpuBind.Blit(_id, p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, sourceTexture,
            sourcePixels.X, sourcePixels.Y, sourcePixels.Width, sourcePixels.Height,
            size.Width, size.Height, sampling == ImageSampling.Linear, opacity);
    }

    public void UploadSubImage(int texture, PixelRect region, ReadOnlySpan<byte> straightRgba, int rowStride)
    {
        ThrowIfDead();
        GpuBind.Upload(_id, texture, region.X, region.Y, region.Width, region.Height,
            straightRgba.ToArray(), rowStride);
    }

    public void ReadPixels(int framebuffer, PixelRect region, Span<byte> destination, int rowStride)
    {
        ThrowIfDead();
        byte[] got = GpuBind.Read(_id, framebuffer, region.X, region.Y, region.Width, region.Height);
        int rowBytes = region.Width * 4;
        for (int y = 0; y < region.Height; y++)
            got.AsSpan(y * rowBytes, rowBytes).CopyTo(destination.Slice(y * rowStride, rowBytes));
    }

    public void Flush()
    {
        ThrowIfDead();
        GpuBind.Flush(_id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { GpuBind.Delete(_id); } catch { }
    }
}

// Managed driver for the explicit GPU probe: real GlesRenderDevice over the
// real WebGL2 binding on a driver-created probe canvas (clear + solid rect +
// CPU-lease readback with exact-pixel asserts). Invoked from Home.razor's
// [JSInvokable] GpuProbeRun; the playwright driver (E:/tmp/gpu-probe.py)
// creates the canvas element first. Production painting is untouched.
public static class GpuProbeDriver
{
    public static async Task<string> RunScenarioAsync(string canvasId, string scenario)
    {
        var report = new Dictionary<string, object?>();
        var checks = new List<Dictionary<string, object?>>();
        void Check(string name, uint expected, uint actual, int tol = 0)
        {
            bool pass = true;
            for (int i = 0; i < 4; i++)
            {
                int d = Math.Abs((int)((expected >> (24 - 8 * i)) & 255) - (int)((actual >> (24 - 8 * i)) & 255));
                if (d > tol) pass = false;
            }
            checks.Add(new Dictionary<string, object?> {
                ["name"] = name, ["expected"] = expected.ToString("X8"),
                ["actual"] = actual.ToString("X8"), ["tol"] = tol, ["pass"] = pass });
        }
        try
        {
            var drivers = new WebGlGlesContextFactory();
            report["caps"] = drivers.SupportDetail;
            if (!drivers.IsSupported)
            {
                report["ok"] = false;
                report["reason"] = "unsupported";
                report["checks"] = checks;
                return JsonSerializer.Serialize(report);
            }
            var layout = new PixelLayout(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);
            var factory = new GlesRenderDeviceFactory(drivers);
            // Alpha scenarios run on small surfaces; exact blend math in
            // straight sRGB: out = src*sa + dst*(1-sa) per channel, alpha
            // likewise. Half values (127.5) round in hardware: tolerance 1.
            if (scenario == "alpha")
            {
                var desc = new SurfaceDescriptor(64, 48, 1f, layout);
                var device = await factory.CreateAsync(
                    new RenderDeviceOptions(canvasId, desc, new ResourceBudget(16L * 1024 * 1024)));
                try
                {
                    var snap = device.Surfaces.Create(desc);
                    var rect = RectPath(8, 8, 56, 40);
                    var halfRed = new SolidBrushSnapshot(new RenderColor(0xFF000080));
                    // Over transparent black.
                    device.Enqueue(new DrawingBatch(snap.Handle, snap.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0x00000000)),
                        new FillPath(rect, halfRed),
                    }));
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(snap.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("transparent-center", 0x80000080u, At(px, lease.RowStride, 32, 24), 1);
                        Check("transparent-corner", 0x00000000u, At(px, lease.RowStride, 0, 0));
                    }
                    // Over opaque blue.
                    var snap2 = device.Surfaces.Create(desc);
                    device.Enqueue(new DrawingBatch(snap2.Handle, snap2.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0x0000FFFF)),
                        new FillPath(rect, halfRed),
                    }));
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(snap2.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("opaque-center", 0x80007FFFu, At(px, lease.RowStride, 32, 24), 1);
                        Check("opaque-corner", 0x0000FFFFu, At(px, lease.RowStride, 0, 0));
                    }
                    report["draws"] = ((GlesRenderDevice)device).Counters.ExecutedDraws;
                }
                finally { await device.DisposeAsync(); }
            }
            else if (scenario == "compose")
            {
                var desc = new SurfaceDescriptor(64, 48, 1f, layout);
                var device = await factory.CreateAsync(
                    new RenderDeviceOptions(canvasId, desc, new ResourceBudget(16L * 1024 * 1024)));
                try
                {
                    // Source image: CPU snapshot import (exercises the
                    // upload path), left red / right green, opaque.
                    // Per-image opacity is a retained-placement concept
                    // (milestone 1 blits opacity 1); translucency is
                    // covered by half-alpha fills in the alpha scenario.
                    var srcDesc = new SurfaceDescriptor(8, 8, 1f, layout);
                    byte[] pattern = new byte[8 * 8 * 4];
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                        {
                            int o = (y * 8 + x) * 4;
                            if (x < 4) { pattern[o] = 255; pattern[o + 3] = 255; }
                            else { pattern[o + 1] = 255; pattern[o + 3] = 255; }
                        }
                    var imported = device.Images.Import(
                        new CpuImageSnapshot("probe-pattern", 0, srcDesc, 32, pattern));
                    var dst = device.Surfaces.Create(desc);
                    var batch = new DrawingBatch(dst.Handle, dst.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0x0000FFFF)),
                        new DrawSurfaceImage(imported, new RenderRect(0, 0, 8, 8),
                            new RenderRect(4, 4, 52, 36), ImageSampling.Nearest),
                    });
                    device.Enqueue(batch);
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(dst.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        // Opaque source over blue: exact texel colors.
                        Check("compose-left", 0xFF0000FFu, At(px, lease.RowStride, 16, 24));
                        Check("compose-right", 0x00FF00FFu, At(px, lease.RowStride, 48, 24));
                    }
                    // SourceCopy overwrite must be exact, ignoring dst.
                    var dst2 = device.Surfaces.Create(desc);
                    var batch2 = new DrawingBatch(dst2.Handle, dst2.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0x0000FFFF)),
                        new SetComposite(CompositeOperation.SourceCopy),
                        new DrawSurfaceImage(imported, new RenderRect(0, 0, 8, 8),
                            new RenderRect(4, 4, 52, 36), ImageSampling.Nearest),
                    });
                    device.Enqueue(batch2);
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(dst2.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("sourcecopy-left", 0xFF0000FFu, At(px, lease.RowStride, 16, 24));
                        Check("sourcecopy-right", 0x00FF00FFu, At(px, lease.RowStride, 48, 24));
                        Check("sourcecopy-outside", 0x0000FFFFu, At(px, lease.RowStride, 0, 0));
                    }
                    report["draws"] = ((GlesRenderDevice)device).Counters.ExecutedDraws;
                    report["uploaded"] = ((GlesRenderDevice)device).Counters.UploadedBytes;
                }
                finally { await device.DisposeAsync(); }
            }
            else if (scenario == "clip")
            {
                var desc = new SurfaceDescriptor(64, 48, 1f, layout);
                var device = await factory.CreateAsync(
                    new RenderDeviceOptions(canvasId, desc, new ResourceBudget(16L * 1024 * 1024)));
                try
                {
                    var snap = device.Surfaces.Create(desc);
                    var batch = new DrawingBatch(snap.Handle, snap.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0xFFFFFFFF)),
                        new SetClip(RectPath(16, 12, 32, 24), ClipOperation.Replace),
                        new FillPath(RectPath(0, 0, 64, 48),
                            new SolidBrushSnapshot(new RenderColor(0x000000FF))),
                        new ResetClip(),
                    });
                    device.Enqueue(batch);
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(snap.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("clip-inside", 0x000000FFu, At(px, lease.RowStride, 32, 24));
                        Check("clip-left", 0xFFFFFFFFu, At(px, lease.RowStride, 15, 24));
                        Check("clip-right", 0xFFFFFFFFu, At(px, lease.RowStride, 48, 24));
                        Check("clip-top", 0xFFFFFFFFu, At(px, lease.RowStride, 32, 11));
                        Check("clip-bottom", 0xFFFFFFFFu, At(px, lease.RowStride, 32, 36));
                    }
                    report["draws"] = ((GlesRenderDevice)device).Counters.ExecutedDraws;
                }
                finally { await device.DisposeAsync(); }
            }
            else if (scenario == "move")
            {
                var desc = new SurfaceDescriptor(64, 48, 1f, layout);
                var device = await factory.CreateAsync(
                    new RenderDeviceOptions(canvasId, desc, new ResourceBudget(16L * 1024 * 1024)));
                try
                {
                    // Repaint-based move (placement-only recompose is
                    // compositor scope, not device batches): the layer is
                    // repainted at the new spot over a cleared target, so
                    // no stale pixels may survive at the old spot.
                    var layerDesc = new SurfaceDescriptor(16, 16, 1f, layout);
                    var layer = device.Surfaces.Create(layerDesc);
                    var dst = device.Surfaces.Create(desc);
                    device.Enqueue(new DrawingBatch(layer.Handle, layer.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0x00000000)),
                        new FillPath(RectPath(2, 2, 12, 12),
                            new SolidBrushSnapshot(new RenderColor(0x0000FFFF))),
                    }));
                    await device.SubmitAsync();
                    // Compose at the first spot: import the layer pixels
                    // through a CPU snapshot (explicit, measured), then blit.
                    ImageHandle snapLayer;
                    using (var lease = device.CpuAccess.Acquire(layer.Handle,
                        new PixelRect(0, 0, 16, 16), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        snapLayer = device.Images.Import(new CpuImageSnapshot(
                            "probe-layer", 0, layerDesc, lease.RowStride,
                            lease.ReadablePixels.ToArray()));
                    }
                    device.Enqueue(new DrawingBatch(dst.Handle, dst.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0xFF0000FF)),
                        new DrawSurfaceImage(snapLayer, new RenderRect(0, 0, 16, 16),
                            new RenderRect(8, 8, 16, 16), ImageSampling.Nearest),
                    }));
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(dst.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("move1-blue", 0x0000FFFFu, At(px, lease.RowStride, 16, 16));
                        Check("move1-red", 0xFF0000FFu, At(px, lease.RowStride, 48, 32));
                    }
                    // Repaint at the new spot over a cleared target.
                    // Fresh Describe: the first submit bumped the surface
                    // past the creation snapshot (optimistic-concurrency
                    // contract); reusing v0 must keep failing.
                    var dstNow = device.Surfaces.Describe(dst.Handle);
                    device.Enqueue(new DrawingBatch(dstNow.Handle, dstNow.ContentVersion, new DrawingCommand[]
                    {
                        new ClearSurface(new RenderColor(0xFF0000FF)),
                        new DrawSurfaceImage(snapLayer, new RenderRect(0, 0, 16, 16),
                            new RenderRect(40, 24, 16, 16), ImageSampling.Nearest),
                    }));
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(dst.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("move2-red-oldspot", 0xFF0000FFu, At(px, lease.RowStride, 16, 16));
                        Check("move2-blue-newspot", 0x0000FFFFu, At(px, lease.RowStride, 48, 32));
                    }
                    var counters = ((GlesRenderDevice)device).Counters;
                    report["draws"] = counters.ExecutedDraws;
                    report["uploaded"] = counters.UploadedBytes;
                }
                finally { await device.DisposeAsync(); }
            }
            else if (scenario == "control")
            {
                // Vertical slice: one real WinForms paint sequence through
                // Graphics.ForDisplay into the real GlesRenderDevice. Solid
                // backdrop + clip + asymmetric band + translucent wash +
                // text + placement-only translate move + SourceCopy stamp.
                // No whole-control CPU raster or upload: the only import is
                // the glyph-island scratch, measured at the import boundary.
                var desc = new SurfaceDescriptor(128, 96, 1f, layout);
                var device = await factory.CreateAsync(
                    new RenderDeviceOptions(canvasId, desc, new ResourceBudget(16L * 1024 * 1024)));
                try
                {
                    var target = device.Surfaces.Create(desc);
                    var coord = new DisplayPaintCoordinator(device);
                    long uploaded = 0, islandPx = 0;
                    ImageHandle Import(CpuImageSnapshot snap)
                    {
                        uploaded += snap.ByteLength;
                        islandPx += (long)snap.Descriptor.PixelWidth * snap.Descriptor.PixelHeight;
                        return device.Images.Import(snap);
                    }
                    void PaintFrame(int badgeDx)
                    {
                        bool painted = coord.Paint(target.Handle, rec =>
                        {
                            using System.Drawing.Graphics g =
                                System.Drawing.Graphics.ForDisplay(rec, Import);
                            PaintControlSlice(g, badgeDx);
                        });
                        if (!painted)
                            throw new InvalidOperationException("control paint recorded zero commands.");
                    }
                    // Frame 1: badge at x=80.
                    PaintFrame(80);
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(target.Handle,
                        new PixelRect(0, 0, 128, 96), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("control-clear", 0x203040FFu, At(px, lease.RowStride, 4, 4));
                        Check("control-origin", 0x78783CFFu, At(px, lease.RowStride, 12, 8), 3);
                        Check("control-outside-clip", 0x203040FFu, At(px, lease.RowStride, 124, 90));
                        Check("control-wash-band", 0x78783CFFu, At(px, lease.RowStride, 20, 40), 3);
                        Check("control-wash-steel", 0x24724DFFu, At(px, lease.RowStride, 100, 40), 3);
                        Check("control-badge", 0xFAD23CFFu, At(px, lease.RowStride, 92, 72));
                        Check("control-sourcecopy", 0x0A141EFFu, At(px, lease.RowStride, 16, 72));
                        // Text island: glyph pixels must differ from the
                        // local wash background (band side vs steel side).
                        int inked = 0, scanned = 0;
                        for (int y = 8; y < 32; y++)
                            for (int x = 12; x < 60; x++)
                            {
                                uint p = At(px, lease.RowStride, x, y);
                                uint bg = x < 68 ? 0x78783CFFu : 0x24724DFFu;
                                int dr = Math.Abs((int)((p >> 24) & 255) - (int)((bg >> 24) & 255));
                                int dg = Math.Abs((int)((p >> 16) & 255) - (int)((bg >> 16) & 255));
                                int db = Math.Abs((int)((p >> 8) & 255) - (int)((bg >> 8) & 255));
                                scanned++;
                                if (dr + dg + db > 24) inked++;
                            }
                        checks.Add(new Dictionary<string, object?> {
                            ["name"] = "control-text-inked", ["expected"] = ">=8 of " + scanned,
                            ["actual"] = inked.ToString(), ["tol"] = 0, ["pass"] = inked >= 8 });
                    }
                    // Frame 2: badge moves to x=40 over a cleared target:
                    // the new spot shows the badge, the old spot shows wash.
                    PaintFrame(40);
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(target.Handle,
                        new PixelRect(0, 0, 128, 96), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("control-move-new", 0xFAD23CFFu, At(px, lease.RowStride, 52, 72));
                        Check("control-move-old", 0x24724DFFu, At(px, lease.RowStride, 92, 72), 3);
                    }
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "control-no-whole-upload", ["expected"] = "0<uploaded<49152",
                        ["actual"] = uploaded.ToString(), ["tol"] = 0,
                        ["pass"] = uploaded > 0 && uploaded < 49152 });
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "control-no-whole-raster", ["expected"] = "0<islandPx<12288",
                        ["actual"] = islandPx.ToString(), ["tol"] = 0,
                        ["pass"] = islandPx > 0 && islandPx < 12288 });
                    report["uploaded"] = uploaded;
                    report["islandPx"] = islandPx;
                    report["draws"] = ((GlesRenderDevice)device).Counters.ExecutedDraws;
                    report["deviceUploaded"] = ((GlesRenderDevice)device).Counters.UploadedBytes;
                }
                finally { await device.DisposeAsync(); }
            }
            else if (scenario == "winforms-control")
            {
                // GPU-4 vertical slice: one REAL Control.OnPaint through
                // ForDisplay -> recorder -> GLES -> retained presentation.
                // Content paint (counted once), content update (counted
                // twice, pixels move), placement-only recompose at two
                // offsets (no repaint, no upload), idle silence. Imports are
                // the glyph island and the 8x8 image only, measured.
                var ctlDesc = new SurfaceDescriptor(64, 48, 1f, layout);
                var stageDesc = new SurfaceDescriptor(128, 96, 1f, layout);
                var gles = (GlesRenderDevice)await factory.CreateAsync(
                    new RenderDeviceOptions(canvasId, stageDesc, new ResourceBudget(16L * 1024 * 1024)));
                IRenderDevice device = gles;
                try
                {
                    var bgSurf = device.Surfaces.Create(stageDesc);
                    var ctlSurf = device.Surfaces.Create(ctlDesc);
                    var stageSurf = device.Surfaces.Create(stageDesc);
                    var coord = new DisplayPaintCoordinator(device);
                    long uploaded = 0;
                    ImageHandle Import(CpuImageSnapshot snap)
                    {
                        uploaded += snap.ByteLength;
                        return device.Images.Import(snap);
                    }
                    using var image = new System.Drawing.Bitmap(8, 8);
                    using (var ig = System.Drawing.Graphics.FromImage(image))
                    using (var ifill = new System.Drawing.SolidBrush(
                        System.Drawing.Color.FromArgb(160, 255, 0, 0)))
                        ig.FillRectangle(ifill, 0, 0, 8, 8);
                    using var control = new GpuProbeControl { GlyphImage = image };
                    bool PaintControl()
                    {
                        return coord.Paint(ctlSurf.Handle, rec =>
                        {
                            using System.Drawing.Graphics g =
                                System.Drawing.Graphics.ForDisplay(rec, Import);
                            control.PaintOnce(g, new System.Drawing.Rectangle(0, 0, 64, 48));
                        });
                    }
                    // Frame 1: initial content, exactly one OnPaint.
                    if (!PaintControl())
                        throw new InvalidOperationException("control paint recorded zero commands.");
                    await device.SubmitAsync();
                    using (var lease = device.CpuAccess.Acquire(ctlSurf.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("ctl-clear", 0x203040FFu, At(px, lease.RowStride, 2, 2));
                        Check("ctl-band", 0x78783CFFu, At(px, lease.RowStride, 6, 6), 3);
                        Check("ctl-steel", 0x24724DFFu, At(px, lease.RowStride, 58, 42), 3);
                        Check("ctl-image", 0x712A1DFFu, At(px, lease.RowStride, 46, 8), 4); // image stores straight (160,0,0,160), not (255,0,0,160)
                        Check("ctl-badge", 0xFAD23CFFu, At(px, lease.RowStride, 50, 34));
                        Check("ctl-stamp", 0x0A141EFFu, At(px, lease.RowStride, 6, 38));
                        Check("ctl-outside-clip", 0x203040FFu, At(px, lease.RowStride, 62, 46));
                        int inked = 0;
                        for (int y = 6; y < 22; y++)
                            for (int x = 6; x < 30; x++)
                            {
                                uint p = At(px, lease.RowStride, x, y);
                                uint bg = 0x78783CFFu;
                                int dr = Math.Abs((int)((p >> 24) & 255) - (int)((bg >> 24) & 255));
                                int dg = Math.Abs((int)((p >> 16) & 255) - (int)((bg >> 16) & 255));
                                int db = Math.Abs((int)((p >> 8) & 255) - (int)((bg >> 8) & 255));
                                if (dr + dg + db > 24) inked++;
                            }
                        checks.Add(new Dictionary<string, object?> {
                            ["name"] = "ctl-text-inked", ["expected"] = ">=8",
                            ["actual"] = inked.ToString(), ["tol"] = 0, ["pass"] = inked >= 8 });
                    }
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "ctl-paint-once", ["expected"] = "1",
                        ["actual"] = control.PaintCount.ToString(), ["tol"] = 0, ["pass"] = control.PaintCount == 1 });
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "ctl-no-whole-upload", ["expected"] = "0<uploaded<12288",
                        ["actual"] = uploaded.ToString(), ["tol"] = 0,
                        ["pass"] = uploaded > 0 && uploaded < 12288 });
                    // Content update: badge moves inside the surface (repaint).
                    control.BadgeX = 20;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    if (!PaintControl())
                        throw new InvalidOperationException("control update recorded zero commands.");
                    await device.SubmitAsync();
                    sw.Stop();
                    using (var lease = device.CpuAccess.Acquire(ctlSurf.Handle,
                        new PixelRect(0, 0, 64, 48), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("ctl-update-new", 0xFAD23CFFu, At(px, lease.RowStride, 24, 34));
                        Check("ctl-update-old", 0x24724DFFu, At(px, lease.RowStride, 50, 34), 3);
                    }
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "ctl-paint-twice", ["expected"] = "2",
                        ["actual"] = control.PaintCount.ToString(), ["tol"] = 0, ["pass"] = control.PaintCount == 2 });
                    report["updateMs"] = sw.ElapsedMilliseconds;
                    // Placement-only: background + control layers recomposed
                    // at T1, then control re-placed at T2 with no repaint.
                    if (!coord.Paint(bgSurf.Handle, rec =>
                    {
                        using System.Drawing.Graphics g =
                            System.Drawing.Graphics.ForDisplay(rec, Import);
                        using var b = new System.Drawing.SolidBrush(
                            System.Drawing.Color.FromArgb(255, 16, 16, 16));
                        g.FillRectangle(b, 0, 0, 128, 96);
                    }))
                        throw new InvalidOperationException("background paint recorded zero commands.");
                    await device.SubmitAsync();
                    var scene = new RetainedScene(device.Identity);
                    scene.SetLayer(new LayerVisual("bg",
                        device.Surfaces.Describe(bgSurf.Handle), Matrix3x2.Identity, null, 1f, 0));
                    scene.SetLayer(new LayerVisual("ctl",
                        device.Surfaces.Describe(ctlSurf.Handle), Matrix3x2.CreateTranslation(8, 8), null, 1f, 1));
                    long upBefore = gles.Counters.UploadedBytes;
                    int paintsBefore = control.PaintCount;
                    await gles.PresentToSurfaceAsync(scene.Capture(), stageSurf.Handle);
                    using (var lease = device.CpuAccess.Acquire(stageSurf.Handle,
                        new PixelRect(0, 0, 128, 96), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("stage-bg", 0x101010FFu, At(px, lease.RowStride, 120, 90));
                        Check("stage-ctl-origin", 0x203040FFu, At(px, lease.RowStride, 10, 10));
                        Check("stage-badge", 0xFAD23CFFu, At(px, lease.RowStride, 32, 42));
                    }
                    scene.SetPlacement("ctl", Matrix3x2.CreateTranslation(48, 40), null, 1f, 1);
                    scene.SetLayer(new LayerVisual("bg",
                        device.Surfaces.Describe(bgSurf.Handle), Matrix3x2.Identity, null, 1f, 0));
                    scene.SetLayer(new LayerVisual("ctl",
                        device.Surfaces.Describe(ctlSurf.Handle), Matrix3x2.CreateTranslation(48, 40), null, 1f, 1));
                    await gles.PresentToSurfaceAsync(scene.Capture(), stageSurf.Handle);
                    using (var lease = device.CpuAccess.Acquire(stageSurf.Handle,
                        new PixelRect(0, 0, 128, 96), CpuPixelAccess.Read, CpuAccessReason.Export))
                    {
                        var px = lease.ReadablePixels.Span;
                        Check("stage-move-new", 0xFAD23CFFu, At(px, lease.RowStride, 72, 74));
                        Check("stage-move-old", 0x101010FFu, At(px, lease.RowStride, 32, 42));
                    }
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "stage-no-repaint", ["expected"] = paintsBefore.ToString(),
                        ["actual"] = control.PaintCount.ToString(), ["tol"] = 0, ["pass"] = control.PaintCount == paintsBefore });
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "stage-no-upload", ["expected"] = upBefore.ToString(),
                        ["actual"] = gles.Counters.UploadedBytes.ToString(), ["tol"] = 0,
                        ["pass"] = gles.Counters.UploadedBytes == upBefore });
                    // Idle silence: a submit with nothing queued executes nothing.
                    long drawsBefore = gles.Counters.ExecutedDraws;
                    await device.SubmitAsync();
                    checks.Add(new Dictionary<string, object?> {
                        ["name"] = "idle-submits-nothing", ["expected"] = drawsBefore.ToString(),
                        ["actual"] = gles.Counters.ExecutedDraws.ToString(), ["tol"] = 0,
                        ["pass"] = gles.Counters.ExecutedDraws == drawsBefore });
                    report["uploaded"] = uploaded;
                    report["paints"] = control.PaintCount;
                    report["draws"] = gles.Counters.ExecutedDraws;
                    report["deviceUploaded"] = gles.Counters.UploadedBytes;
                }
                finally { await device.DisposeAsync(); }
            }
            else
            {
                report["ok"] = false;
                report["reason"] = "unknown-scenario:" + scenario;
                report["checks"] = checks;
                return JsonSerializer.Serialize(report);
            }
            report["checks"] = checks;
            report["ok"] = checks.TrueForAll(c => (bool)c["pass"]!);
        }
        catch (Exception ex)
        {
            report["ok"] = false;
            report["error"] = ex.GetType().Name + ": " + (ex.Message ?? "").Split('\n')[0];
            string stackTop = string.Join("\n", (ex.StackTrace ?? "").Split('\n').Take(6));
            report["stackTop"] = stackTop;
            report["checks"] = checks;
        }
        return JsonSerializer.Serialize(report);
    }

    private static PathSnapshot RectPath(float x, float y, float w, float h) =>
        new PathSnapshot(new[]
        {
            new PathSegment(PathVerb.Move, new Vector2(x, y)),
            new PathSegment(PathVerb.Line, new Vector2(x + w, y)),
            new PathSegment(PathVerb.Line, new Vector2(x + w, y + h)),
            new PathSegment(PathVerb.Line, new Vector2(x, y + h)),
            new PathSegment(PathVerb.Close),
        }, PathFillRule.Alternate);

    private static uint At(ReadOnlySpan<byte> s, int stride, int x, int y)
    {
        int o = y * stride + x * 4;
        return ((uint)s[o] << 24) | ((uint)s[o + 1] << 16) | ((uint)s[o + 2] << 8) | s[o + 3];
    }

    // One real WinForms control paint: opaque backdrop, clip, asymmetric
    // band, translucent wash, text, placement-only move, SourceCopy stamp.
    // Runs against ANY System.Drawing.Graphics; the display hooks engage
    // only on ForDisplay graphics (CPU bitmap painting is untouched).
    private static void PaintControlSlice(System.Drawing.Graphics g, int badgeDx)
    {
        using (var back = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(255, 32, 48, 64)))
            g.FillRectangle(back, 0, 0, 128, 96);
        g.SetClip(new System.Drawing.Rectangle(8, 8, 112, 80));
        using (var band = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(255, 200, 60, 30)))
            g.FillRectangle(band, 8, 8, 60, 80);
        using (var wash = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(128, 40, 180, 90)))
            g.FillRectangle(wash, 0, 0, 128, 96);
        using (var font = new System.Drawing.Font("Arial", 12))
        using (var ink = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(220, 255, 255, 255)))
            g.DrawString("Hi", font, ink, 12f, 12f);
        // Placement-only move: later draws land at (badgeDx, 64).
        g.TranslateTransform(badgeDx, 64);
        using (var badge = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(255, 250, 210, 60)))
            g.FillRectangle(badge, 0, 0, 24, 16);
        g.TranslateTransform(-badgeDx, -64);
        // SourceCopy stamp proves overwrite (not blend) semantics.
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        using (var stamp = new System.Drawing.SolidBrush(
            System.Drawing.Color.FromArgb(255, 10, 20, 30)))
            g.FillRectangle(stamp, 8, 64, 16, 16);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        g.ResetClip();
    }

    // One REAL WinForms control through the GPU path: genuine Control.OnPaint
    // dispatch (not a helper) with milestone-1 vocabulary only — opaque
    // backdrop, clip, band, translucent wash, translucent image blit, text,
    // badge, SourceCopy stamp. Anything outside the vocabulary throws
    // explicitly from the adapter; production CPU painting is untouched.
    private sealed class GpuProbeControl : System.Windows.Forms.Control
    {
        public int PaintCount;
        public int BadgeX = 46;
        public System.Drawing.Bitmap? GlyphImage;
        public void PaintOnce(System.Drawing.Graphics g, System.Drawing.Rectangle clip)
        {
            OnPaint(new System.Windows.Forms.PaintEventArgs(g, clip));
        }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            PaintCount++;
            var g = e.Graphics;
            using (var back = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(255, 32, 48, 64)))
                g.FillRectangle(back, 0, 0, 64, 48);
            g.SetClip(new System.Drawing.Rectangle(4, 4, 56, 40));
            using (var band = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(255, 200, 60, 30)))
                g.FillRectangle(band, 4, 4, 28, 40);
            using (var wash = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(128, 40, 180, 90)))
                g.FillRectangle(wash, 0, 0, 64, 48);
            if (GlyphImage is not null)
                g.DrawImageUnscaled(GlyphImage, 44, 6);
            using (var font = new System.Drawing.Font("Arial", 12))
            using (var ink = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(220, 255, 255, 255)))
                g.DrawString("Hi", font, ink, 6f, 6f);
            using (var badge = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(255, 250, 210, 60)))
                g.FillRectangle(badge, BadgeX, 30, 12, 10);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            using (var stamp = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(255, 10, 20, 30)))
                g.FillRectangle(stamp, 4, 36, 10, 8);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
            g.ResetClip();
        }
    }

    public static async Task<string> RunAsync(string canvasId)
    {
        var report = new Dictionary<string, object?>();
        try
        {
            var drivers = new WebGlGlesContextFactory();
            string caps = drivers.SupportDetail;
            report["caps"] = caps;
            if (!drivers.IsSupported)
            {
                report["ok"] = false;
                report["reason"] = "unsupported";
                return JsonSerializer.Serialize(report);
            }
            var layout = new PixelLayout(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);
            var desc = new SurfaceDescriptor(128, 96, 1f, layout);
            var factory = new GlesRenderDeviceFactory(drivers);
            var device = await factory.CreateAsync(
                new RenderDeviceOptions(canvasId, desc, new ResourceBudget(64L * 1024 * 1024)));
            try
            {
                var snap = device.Surfaces.Create(desc);
                var path = new PathSnapshot(new[]
                {
                    new PathSegment(PathVerb.Move, new Vector2(8, 8)),
                    new PathSegment(PathVerb.Line, new Vector2(120, 8)),
                    new PathSegment(PathVerb.Line, new Vector2(120, 88)),
                    new PathSegment(PathVerb.Line, new Vector2(8, 88)),
                    new PathSegment(PathVerb.Close),
                }, PathFillRule.Alternate);
                var batch = new DrawingBatch(snap.Handle, snap.ContentVersion, new DrawingCommand[]
                {
                    new ClearSurface(new RenderColor(0xFF0000FF)),
                    new FillPath(path, new SolidBrushSnapshot(new RenderColor(0x0000FFFF))),
                });
                device.Enqueue(batch);
                var receipt = await device.SubmitAsync();
                using var lease = device.CpuAccess.Acquire(snap.Handle,
                    new PixelRect(0, 0, 128, 96), CpuPixelAccess.Read, CpuAccessReason.Export);
                var px = lease.ReadablePixels.Span;
                int stride = lease.RowStride;
                static uint At(ReadOnlySpan<byte> s, int st, int x, int y)
                {
                    int o = y * st + x * 4;
                    return ((uint)s[o] << 24) | ((uint)s[o + 1] << 16) | ((uint)s[o + 2] << 8) | s[o + 3];
                }
                uint c00 = At(px, stride, 0, 0);
                uint cMid = At(px, stride, 64, 48);
                uint cMax = At(px, stride, 127, 95);
                var counters = ((GlesRenderDevice)device).Counters;
                report["ok"] = c00 == 0xFF0000FFu && cMid == 0x0000FFFFu && cMax == 0xFF0000FFu;
                report["corner"] = c00.ToString("X8");
                report["center"] = cMid.ToString("X8");
                report["far"] = cMax.ToString("X8");
                report["receipt"] = receipt.Sequence;
                report["draws"] = counters.ExecutedDraws;
                report["readbackBytes"] = counters.ReadbackBytes;
                report["residentBytes"] = counters.ResidentBytes;
            }
            finally
            {
                await device.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            report["ok"] = false;
            report["error"] = ex.GetType().Name + ": " + (ex.Message ?? "").Split('\n')[0];
            string stackTop = string.Join("\n", (ex.StackTrace ?? "").Split('\n').Take(6));
            report["stackTop"] = stackTop;
        }
        return JsonSerializer.Serialize(report);
    }
}
