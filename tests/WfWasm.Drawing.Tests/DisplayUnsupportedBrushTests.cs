// GPU REGRESSION (display adapter): unsupported brushes must be rejected
// explicitly, never flattened to a solid color; unsupported shapes must be
// reported, never silently rendered as something else. The linear/radial/
// hatch FillRectangle path is pinned elsewhere; this file covers the
// remaining brush kinds, batch atomicity, and the non-rectangle fill/draw
// entry points on the display path.
extern alias WfDraw;

using System.Reflection;
using WfWasm.Rendering;

namespace WfWasm.Drawing.Tests;

public sealed class DisplayUnsupportedBrushTests
{
    private static readonly DeviceIdentity Device = new(Guid.NewGuid(), 1);

    private static DrawingCommandRecorder Recorder() => (DrawingCommandRecorder)Activator.CreateInstance(
        typeof(DrawingCommandRecorder), BindingFlags.Instance | BindingFlags.NonPublic, null,
        [new SurfaceSnapshot(new(Device, 1, 1), new(100, 100, 1,
            new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb)), 0, PixelAuthority.Device)], null)!;

    private static DrawingBatch Seal(DrawingCommandRecorder recorder) => (DrawingBatch)typeof(DrawingCommandRecorder)
        .GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recorder, null)!;

    private static WfDraw::System.Drawing.Graphics Display(DrawingCommandRecorder recorder)
    {
        int imports = 0;
        return WfDraw::System.Drawing.Graphics.ForDisplay(
            recorder, _ => { imports++; return new ImageHandle(Device, imports, 1); });
    }

    [Fact]
    public void TextureBrush_Is_Rejected_Before_Recording()
    {
        var recorder = Recorder();
        using var g = Display(recorder);
        using var tile = new WfDraw::System.Drawing.Bitmap(4, 4);
        using WfDraw::System.Drawing.Brush brush = new WfDraw::System.Drawing.TextureBrush(tile);
        var error = Record.Exception(() =>
            g.FillRectangle(brush, new WfDraw::System.Drawing.Rectangle(0, 0, 12, 12)));
        Assert.IsType<NotSupportedException>(error);
        Assert.Empty(Seal(recorder).Commands); // rejected before partial output
    }

    [Fact]
    public void Gradient_Rejection_Preserves_The_Earlier_Solid_Fill()
    {
        var recorder = Recorder();
        using var g = Display(recorder);
        using var solid = new WfDraw::System.Drawing.SolidBrush(WfDraw::System.Drawing.Color.Red);
        g.FillRectangle(solid, new WfDraw::System.Drawing.Rectangle(0, 0, 4, 4));
        using WfDraw::System.Drawing.Brush gradient =
            new WfDraw::System.Drawing.Drawing2D.LinearGradientBrush(
                new WfDraw::System.Drawing.Rectangle(0, 0, 12, 12),
                WfDraw::System.Drawing.Color.Red, WfDraw::System.Drawing.Color.Blue, 0f);
        var error = Record.Exception(() =>
            g.FillRectangle(gradient, new WfDraw::System.Drawing.Rectangle(4, 0, 4, 4)));
        Assert.IsType<NotSupportedException>(error);
        // Atomicity: the earlier solid fill is intact, the gradient added nothing.
        var fill = Assert.Single(Seal(recorder).Commands.OfType<FillPath>());
        Assert.Equal(0xFF0000FFu, Assert.IsType<SolidBrushSnapshot>(fill.Brush).Color.Rgba);
    }

    [Fact]
    public void HatchBrush_Via_FillRectangles_Plural_Is_Rejected()
    {
        var recorder = Recorder();
        using var g = Display(recorder);
        using WfDraw::System.Drawing.Brush brush =
            new WfDraw::System.Drawing.Drawing2D.HatchBrush(
                WfDraw::System.Drawing.Drawing2D.HatchStyle.Cross,
                WfDraw::System.Drawing.Color.Red, WfDraw::System.Drawing.Color.Blue);
        var error = Record.Exception(() => g.FillRectangles(brush,
            [new WfDraw::System.Drawing.Rectangle(0, 0, 4, 4),
             new WfDraw::System.Drawing.Rectangle(4, 0, 4, 4)]));
        Assert.IsType<NotSupportedException>(error);
        Assert.Empty(Seal(recorder).Commands); // rejected before partial output
    }

    [Fact]
    public void FillEllipse_Solid_Is_Explicit_Not_Silent()
    {
        var recorder = Recorder();
        using var g = Display(recorder);
        using var brush = new WfDraw::System.Drawing.SolidBrush(WfDraw::System.Drawing.Color.Red);
        var error = Record.Exception(() =>
            g.FillEllipse(brush, new WfDraw::System.Drawing.RectangleF(0, 0, 8, 8)));
        Assert.IsType<NotSupportedException>(error);
        Assert.Empty(Seal(recorder).Commands);
    }

    [Fact]
    public void FillPolygon_Solid_Is_Explicit_Not_Silent()
    {
        var recorder = Recorder();
        using var g = Display(recorder);
        using var brush = new WfDraw::System.Drawing.SolidBrush(WfDraw::System.Drawing.Color.Red);
        var error = Record.Exception(() => g.FillPolygon(brush,
            [new WfDraw::System.Drawing.PointF(0, 0),
             new WfDraw::System.Drawing.PointF(8, 0),
             new WfDraw::System.Drawing.PointF(4, 8)]));
        Assert.IsType<NotSupportedException>(error);
        Assert.Empty(Seal(recorder).Commands);
    }

    [Fact]
    public void DrawLine_Is_Explicit_Not_Silent()
    {
        var recorder = Recorder();
        using var g = Display(recorder);
        using var pen = new WfDraw::System.Drawing.Pen(WfDraw::System.Drawing.Color.Red);
        var error = Record.Exception(() => g.DrawLine(pen, 0f, 0f, 4f, 4f));
        Assert.IsType<NotSupportedException>(error);
        Assert.Empty(Seal(recorder).Commands);
    }
}
