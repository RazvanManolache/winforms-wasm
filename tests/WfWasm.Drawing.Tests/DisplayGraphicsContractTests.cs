extern alias WfDraw;
using D = WfDraw::System.Drawing;
using G2 = WfDraw::System.Drawing.Drawing2D;
using System.Numerics;
using System.Reflection;
using WfWasm.Rendering;

// Adapter semantics only. These tests never claim GPU pixels/performance.
// Reflection accesses the existing internal recorder and text-island seams;
// production code is deliberately unchanged. Keep the behavioral assertions if
// those seams acquire a public test adapter later.
public sealed class DisplayGraphicsContractTests
{
    private static readonly DeviceIdentity Device = new(Guid.NewGuid(), 1);
    private static DrawingCommandRecorder Recorder() => (DrawingCommandRecorder)Activator.CreateInstance(
        typeof(DrawingCommandRecorder), BindingFlags.Instance | BindingFlags.NonPublic, null,
        [new SurfaceSnapshot(new(Device, 1, 1), new(100, 100, 1,
            new(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb)), 0, PixelAuthority.Device)], null)!;
    private static DrawingBatch Seal(DrawingCommandRecorder recorder) => (DrawingBatch)typeof(DrawingCommandRecorder)
        .GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recorder, null)!;

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    [InlineData("hatch")]
    public void NonSolid_Brush_Is_Preserved_Or_Explicitly_Rejected_Never_Flattened(string kind)
    {
        var recorder = Recorder();
        using var graphics = D.Graphics.ForDisplay(recorder, _ => throw new Exception("Unexpected CPU image fallback"));
        using D.Brush brush = kind switch
        {
            "linear" => new G2.LinearGradientBrush(new D.Rectangle(0, 0, 12, 12), D.Color.Red, D.Color.Blue, 0f),
            "radial" => new G2.PathGradientBrush([new D.PointF(0, 0), new D.PointF(12, 0), new D.PointF(6, 12)]),
            _ => new G2.HatchBrush(G2.HatchStyle.Cross, D.Color.Red, D.Color.Blue)
        };
        Exception? error = Record.Exception(() => graphics.FillRectangle(brush, 0, 0, 12, 12));
        if (error is not null)
        {
            Assert.IsType<NotSupportedException>(error);
            Assert.Empty(Seal(recorder).Commands); // rejected before partial output
            return;
        }
        var fill = Assert.Single(Seal(recorder).Commands.OfType<FillPath>());
        Assert.IsNotType<SolidBrushSnapshot>(fill.Brush);
        if (kind == "linear")
        {
            var gradient = Assert.IsType<LinearGradientSnapshot>(fill.Brush);
            Assert.Equal(0xFF0000FFu, gradient.Stops[0].Color.Rgba);
            Assert.Equal(0x0000FFFFu, gradient.Stops[^1].Color.Rgba);
        }
    }

    [Fact]
    public void Solid_Fills_Record_Snapshots_And_Import_No_CPU_Control_Image()
    {
        var recorder = Recorder(); int imports = 0;
        using var graphics = D.Graphics.ForDisplay(recorder, _ => { imports++; return default; });
        using var brush = new D.SolidBrush(D.Color.Red);
        graphics.FillRectangle(brush, 0, 0, 4, 4);
        brush.Color = D.Color.Blue;
        graphics.FillRectangle(brush, 4, 0, 4, 4);
        brush.Dispose();
        var fills = Seal(recorder).Commands.OfType<FillPath>().ToArray();
        Assert.Equal(2, fills.Length);
        Assert.Equal(0xFF0000FFu, Assert.IsType<SolidBrushSnapshot>(fills[0].Brush).Color.Rgba);
        Assert.Equal(0x0000FFFFu, Assert.IsType<SolidBrushSnapshot>(fills[1].Brush).Color.Rgba);
        Assert.Equal(0, imports);
    }

    [Fact]
    public void ClipBounds_Reflects_Explicit_Display_Clip()
    {
        var recorder = Recorder(); using var g = D.Graphics.ForDisplay(recorder, _ => default);
        g.SetClip(new D.Rectangle(4, 5, 6, 7));
        Assert.Equal(new D.RectangleF(4, 5, 6, 7), g.ClipBounds);
        Assert.False(g.IsClipEmpty);
    }

    [Fact]
    public void Clip_Property_RoundTrips_Into_Display_State()
    {
        var recorder = Recorder(); using var g = D.Graphics.ForDisplay(recorder, _ => default);
        using var region = new D.Region(new D.Rectangle(3, 4, 5, 6));
        g.Clip = region;
        Assert.Equal(new D.RectangleF(3, 4, 5, 6), g.ClipBounds);
        using var copy = g.Clip;
        Assert.True(copy.IsVisible(4, 5)); Assert.False(copy.IsVisible(0, 0));
        Assert.Contains(Seal(recorder).Commands, c => c is SetClip);
    }

    [Fact]
    public void SaveRestore_Preserves_Display_Clip_Getters()
    {
        var recorder = Recorder(); using var g = D.Graphics.ForDisplay(recorder, _ => default);
        g.SetClip(new D.Rectangle(2, 3, 20, 21));
        var saved = g.Save();
        g.SetClip(new D.Rectangle(7, 8, 2, 2));
        g.Restore(saved);
        Assert.Equal(new D.RectangleF(2, 3, 20, 21), g.ClipBounds);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(3, 5)]
    [InlineData(5, 3)]
    public void Text_Island_Cannot_Expand_The_Application_Clip(int x, int y)
    {
        var recorder = Recorder(); var imports = new List<CpuImageSnapshot>();
        using var g = D.Graphics.ForDisplay(recorder, image => { imports.Add(image); return new(Device, imports.Count, 1); });
        g.SetClip(new D.Rectangle(4, 4, 2, 2));
        TextIsland(g, 3, 3);
        var batch = Seal(recorder);
        Assert.False(TextWritesAt(batch, x, y), "Text escaped the application clip");
        Assert.True(TextWritesAt(batch, 4, 4), "Clipped text was incorrectly omitted");
        Assert.Equal(64, Assert.Single(imports).ByteLength); // 4x4 glyph island, not a control bitmap
    }

    [Fact]
    public void Translated_Text_Intersects_Translated_Application_Clip()
    {
        var recorder = Recorder(); using var g = D.Graphics.ForDisplay(recorder, _ => new(Device, 1, 1));
        g.TranslateTransform(7, 11);
        g.SetClip(new D.Rectangle(0, 0, 2, 2));
        TextIsland(g, 6, 10); // seam takes device-pixel origin
        var batch = Seal(recorder);
        Assert.False(TextWritesAt(batch, 6, 10));
        Assert.True(TextWritesAt(batch, 7, 11));
    }

    [Fact]
    public void Empty_Display_Clip_Suppresses_Text_Without_Failing()
    {
        var recorder = Recorder(); using var g = D.Graphics.ForDisplay(recorder, _ => new(Device, 1, 1));
        g.SetClip(new D.Rectangle(0, 0, 0, 0));
        TextIsland(g, 0, 0);
        Assert.False(TextWritesAt(Seal(recorder), 0, 0));
        Assert.True(g.IsClipEmpty);
    }

    private static void TextIsland(D.Graphics graphics, int x, int y)
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 64).ToArray();
        try
        {
            typeof(D.Graphics).GetMethod("RecordDisplayText", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(graphics, [pixels, 4, 4, 255, 255, 255, 255, x, y, x, y, x + 4, y + 4]);
        }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); }
    }

    // Independent rectangular state oracle for the adapter's emitted commands.
    // It deliberately does not execute the candidate translator or shaders.
    private static bool TextWritesAt(DrawingBatch batch, float x, float y)
    {
        var transform = Matrix3x2.Identity; D.RectangleF? clip = null; bool wrote = false;
        var stack = new Stack<(Matrix3x2 Transform, D.RectangleF? Clip)>();
        D.RectangleF Map(RenderRect r)
        {
            var a = Vector2.Transform(new(r.X, r.Y), transform);
            var b = Vector2.Transform(new(r.X + r.Width, r.Y + r.Height), transform);
            return new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        }
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
                        ClipOperation.Intersect => clip is null ? r : D.RectangleF.Intersect(clip.Value, r),
                        _ => throw new Exception("Extend the test oracle for the newly supported clip operation")
                    }; break;
                case DrawSurfaceImage draw:
                    if (Map(draw.Destination).Contains(x, y) && (clip is null || clip.Value.Contains(x, y))) wrote = true;
                    break;
            }
        return wrote;
    }
}
