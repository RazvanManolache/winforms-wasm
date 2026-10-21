// GPU REGRESSION (display adapter): text islands must respect the
// application clip. These go through the PUBLIC Graphics.DrawString entry
// point with the documented headless RasterOverride seam, unlike the
// seam-level RecordDisplayText probes elsewhere. They challenge the
// assumption that the layout clip alone is sufficient: GDI+ keeps the
// Graphics clip active during text drawing (the CPU path tests every pixel
// against _clip), and StringFormatFlags.NoClip only lifts the LAYOUT clamp,
// never the Graphics clip.
extern alias WfDraw;

using System.Numerics;
using System.Reflection;
using WfWasm.Rendering;

namespace WfWasm.Drawing.Tests;

public sealed class DisplayTextClipRegressionTests
{
    private static readonly DeviceIdentity Device = new(Guid.NewGuid(), 1);

    private static DrawingCommandRecorder Recorder() => (DrawingCommandRecorder)Activator.CreateInstance(
        typeof(DrawingCommandRecorder), BindingFlags.Instance | BindingFlags.NonPublic, null,
        [new SurfaceSnapshot(new(Device, 1, 1), new(100, 100, 1,
            new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb)), 0, PixelAuthority.Device)], null)!;

    private static DrawingBatch Seal(DrawingCommandRecorder recorder) => (DrawingBatch)typeof(DrawingCommandRecorder)
        .GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recorder, null)!;

    private static WfDraw::System.Drawing.BrowserText.Raster Opaque8x8()
    {
        byte[] rgba = new byte[8 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfDraw::System.Drawing.BrowserText.Raster(8, 8, rgba);
    }

    private static DrawingBatch PaintText(
        Action<WfDraw::System.Drawing.Graphics> configure,
        WfDraw::System.Drawing.RectangleF layout,
        WfDraw::System.Drawing.StringFormat? format)
    {
        var recorder = Recorder();
        var imports = new List<CpuImageSnapshot>();
        using var g = WfDraw::System.Drawing.Graphics.ForDisplay(
            recorder, image => { imports.Add(image); return new ImageHandle(Device, imports.Count, 1); });
        configure(g);
        WfDraw::System.Drawing.BrowserText.RasterOverride = (_, _, _, _) => Opaque8x8();
        try
        {
            using var font = new WfDraw::System.Drawing.Font(
                WfDraw::System.Drawing.FontFamily.GenericSansSerif, 9f);
            using var brush = new WfDraw::System.Drawing.SolidBrush(WfDraw::System.Drawing.Color.Black);
            g.DrawString("hi", font, brush, layout, format);
        }
        finally
        {
            WfDraw::System.Drawing.BrowserText.RasterOverride = null;
        }
        return Seal(recorder);
    }

    // The island clip the adapter records, evaluated in device space (the
    // same convention as the command-state oracle: recorded world rects
    // mapped through the active device transform). Exactly one text island
    // is expected per batch.
    private static WfDraw::System.Drawing.RectangleF IslandClip(DrawingBatch batch)
    {
        var transform = Matrix3x2.Identity;
        WfDraw::System.Drawing.RectangleF? clip = null;
        var stack = new Stack<(Matrix3x2 Transform, WfDraw::System.Drawing.RectangleF? Clip)>();
        WfDraw::System.Drawing.RectangleF Map(WfWasm.Rendering.RenderRect r)
        {
            var a = Vector2.Transform(new(r.X, r.Y), transform);
            var b = Vector2.Transform(new(r.X + r.Width, r.Y + r.Height), transform);
            return new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }
        int islands = 0;
        WfDraw::System.Drawing.RectangleF islandClip = default;
        foreach (var command in batch.Commands)
            switch (command)
            {
                case SaveState: stack.Push((transform, clip)); break;
                case RestoreState: (transform, clip) = stack.Pop(); break;
                case SetTransform set: transform = set.Transform; break;
                case ResetClip: clip = null; break;
                case SetClip set:
                    var points = set.Path.Segments.Where(p => p.Verb != PathVerb.Close).Select(p => p.A).ToArray();
                    var r = Map(new(points.Min(p => p.X), points.Min(p => p.Y),
                        points.Max(p => p.X) - points.Min(p => p.X), points.Max(p => p.Y) - points.Min(p => p.Y)));
                    clip = set.Operation switch
                    {
                        ClipOperation.Replace => r,
                        ClipOperation.Intersect => clip is null ? r : WfDraw::System.Drawing.RectangleF.Intersect(clip.Value, r),
                        _ => throw new Exception("Extend the oracle for the newly supported clip operation")
                    };
                    break;
                case DrawSurfaceImage:
                    islands++;
                    Assert.NotNull(clip);
                    islandClip = clip.Value;
                    break;
            }
        Assert.Equal(1, islands);
        return islandClip;
    }

    [Fact]
    public void NoClip_Does_Not_Disable_The_Graphics_Clip()
    {
        using var format = new WfDraw::System.Drawing.StringFormat(
            WfDraw::System.Drawing.StringFormatFlags.NoClip);
        var batch = PaintText(
            g => g.SetClip(new WfDraw::System.Drawing.Rectangle(4, 4, 2, 2)),
            new WfDraw::System.Drawing.RectangleF(0, 0, 16, 16), format);
        // Layout clamp is lifted, but the (4,4,2,2) Graphics clip still wins.
        Assert.Equal(new WfDraw::System.Drawing.RectangleF(4, 4, 2, 2), IslandClip(batch));
    }

    [Fact]
    public void NoClip_Without_Graphics_Clip_Paints_The_Island()
    {
        // Control: proves the probe paints when no application clip exists.
        using var format = new WfDraw::System.Drawing.StringFormat(
            WfDraw::System.Drawing.StringFormatFlags.NoClip);
        var batch = PaintText(
            _ => { },
            new WfDraw::System.Drawing.RectangleF(0, 0, 16, 16), format);
        Assert.Equal(new WfDraw::System.Drawing.RectangleF(0, 0, 8, 8), IslandClip(batch));
    }

    [Fact]
    public void Layout_Clip_Intersects_The_Graphics_Clip()
    {
        var batch = PaintText(
            g => g.SetClip(new WfDraw::System.Drawing.Rectangle(4, 4, 2, 2)),
            new WfDraw::System.Drawing.RectangleF(0, 0, 16, 16), null);
        // 8x8 island at the origin, layout (0,0,16,16): only the (4,4,2,2)
        // application-clip overlap may draw.
        Assert.Equal(new WfDraw::System.Drawing.RectangleF(4, 4, 2, 2), IslandClip(batch));
    }

    [Fact]
    public void Translated_Graphics_Clip_Constrains_Text()
    {
        var batch = PaintText(
            g =>
            {
                g.TranslateTransform(7, 11);
                g.SetClip(new WfDraw::System.Drawing.Rectangle(0, 0, 2, 2));
            },
            new WfDraw::System.Drawing.RectangleF(0, 0, 8, 8), null);
        // Device space: 8x8 island at (7,11), application clip (7,11,2,2).
        Assert.Equal(new WfDraw::System.Drawing.RectangleF(7, 11, 2, 2), IslandClip(batch));
    }

    [Fact]
    public void Empty_Graphics_Clip_Suppresses_Text()
    {
        var batch = PaintText(
            g => g.SetClip(new WfDraw::System.Drawing.Rectangle(0, 0, 0, 0)),
            new WfDraw::System.Drawing.RectangleF(0, 0, 8, 8), null);
        Assert.True(IslandClip(batch).IsEmpty);
    }
}
