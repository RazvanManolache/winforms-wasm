extern alias WfDraw;

using D = WfDraw::System.Drawing;
using G2 = WfDraw::System.Drawing.Drawing2D;
using Xunit;

// Pixel contracts deliberately preserve the current rasterizer's fill rules and
// flat gradient approximation. Performance work must not also change rendering.
public sealed class RasterWorkBoundsTests
{
    private static readonly D.Color Background = D.Color.FromArgb(255, 19, 37, 61);
    private static readonly D.PointF[] Polygon =
    [new(-12.25f, -8.5f), new(66.75f, 3.25f), new(51.5f, 58.75f), new(9.25f, 43.5f)];

    [Theory]
    [InlineData("fractional")]
    [InlineData("disjoint")]
    [InlineData("hole")]
    [InlineData("empty")]
    [InlineData("infinite")]
    [InlineData("outside")]
    public void Polygon_Clip_Is_An_Exact_Mask_Of_Unclipped_Pixels(string kind)
    {
        using var clip = MakeClip(kind);
        using var matrix = new G2.Matrix();
        AssertMaskedParity(clip, matrix, "polygon", G2.FillMode.Alternate, G2.CompositingMode.SourceOver);
    }

    [Theory]
    [InlineData("polygon", false)]
    [InlineData("polygon", true)]
    [InlineData("rectangle", false)]
    [InlineData("rectangle", true)]
    [InlineData("ellipse", false)]
    [InlineData("ellipse", true)]
    public void Transformed_Clip_Uses_Device_Coordinates_And_Preserves_Alpha(string primitive, bool sourceCopy)
    {
        using var clip = new D.Region(new D.RectangleF(6.25f, 5.5f, 20.25f, 18.25f));
        // Nonuniform scale, shear and translation. World ClipBounds cannot be
        // used directly to clamp device-space scanlines.
        using var matrix = new G2.Matrix(1.2f, 0.15f, -0.1f, 0.9f, 7.25f, 4.5f);
        AssertMaskedParity(clip, matrix, primitive, G2.FillMode.Alternate,
            sourceCopy ? G2.CompositingMode.SourceCopy : G2.CompositingMode.SourceOver);
    }

    [Theory]
    [InlineData(G2.FillMode.Alternate)]
    [InlineData(G2.FillMode.Winding)]
    public void SelfIntersecting_Polygon_Keeps_Current_Fill_Rule_Under_Clipping(G2.FillMode mode)
    {
        using var clip = MakeClip("hole");
        using var matrix = new G2.Matrix();
        D.PointF[] crossing = [new(1, 1), new(59, 54), new(4, 52), new(58, 2)];
        AssertMaskedParity(clip, matrix, "polygon", mode, G2.CompositingMode.SourceOver, crossing);
    }

    [Theory]
    [InlineData("polygon")]
    [InlineData("rectangle")]
    [InlineData("ellipse")]
    public void Gradient_Preparation_Preserves_Clipped_Alpha_And_Stop_Weights(string primitive)
    {
        using var clip = MakeClip("disjoint");
        using var matrix = new G2.Matrix();
        AssertMaskedParity(clip, matrix, primitive, G2.FillMode.Alternate,
            G2.CompositingMode.SourceOver, gradient: true);
    }

    [Fact]
    public void Prepared_Brush_Does_Not_Turn_A_Texture_Into_A_Constant_Color()
    {
        using var tile = new D.Bitmap(2, 2);
        tile.SetPixel(0, 0, D.Color.Red);
        tile.SetPixel(1, 0, D.Color.Blue);
        tile.SetPixel(0, 1, D.Color.Lime);
        tile.SetPixel(1, 1, D.Color.White);
        using var brush = new D.TextureBrush(tile);
        using var image = new D.Bitmap(16, 16);
        using var g = D.Graphics.FromImage(image);
        g.Clear(Background);
        g.SetClip(new D.Rectangle(3, 3, 6, 6));
        g.FillPolygon(brush, [new D.PointF(-10, -10), new(30, -10), new(30, 30), new(-10, 30)]);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(x >= 3 && x < 9 && y >= 3 && y < 9
                    ? tile.GetPixel(x % 2, y % 2).ToArgb() : Background.ToArgb(), image.GetPixel(x, y).ToArgb());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepared_Gradient_Is_Refreshed_When_The_Same_Brush_Is_Mutated(bool interpolation)
    {
        using var image = new D.Bitmap(20, 8);
        using var g = D.Graphics.FromImage(image);
        using var brush = new G2.LinearGradientBrush(new D.Rectangle(0, 0, 20, 8), D.Color.Red, D.Color.Red, 0f);
        if (interpolation)
            brush.InterpolationColors = new G2.ColorBlend { Colors = [D.Color.Red, D.Color.Red], Positions = [0f, 1f] };
        g.FillRectangle(brush, new D.Rectangle(0, 0, 8, 8));
        if (interpolation)
            brush.InterpolationColors = new G2.ColorBlend { Colors = [D.Color.Blue, D.Color.Blue], Positions = [0f, 1f] };
        else
            brush.LinearColors = [D.Color.Blue, D.Color.Blue];
        g.FillRectangle(brush, new D.Rectangle(10, 0, 8, 8));
        Assert.Equal(D.Color.Red.ToArgb(), image.GetPixel(3, 3).ToArgb());
        Assert.Equal(D.Color.Blue.ToArgb(), image.GetPixel(13, 3).ToArgb());
    }

    [Fact]
    public void Prepared_Gradient_Refreshes_Interpolation_Positions()
    {
        using var image = new D.Bitmap(20, 8);
        using var g = D.Graphics.FromImage(image);
        using var brush = new G2.LinearGradientBrush(new D.Rectangle(0, 0, 20, 8), D.Color.Red, D.Color.Blue, 0f);
        var blend = new G2.ColorBlend
        {
            Colors = [D.Color.Red, D.Color.Lime, D.Color.Blue], Positions = [0f, 0.5f, 1f]
        };
        brush.InterpolationColors = blend;
        g.FillRectangle(brush, new D.Rectangle(0, 0, 8, 8));
        blend.Positions = [0f, 0.25f, 1f];
        brush.InterpolationColors = blend;
        g.FillRectangle(brush, new D.Rectangle(10, 0, 8, 8));
        Assert.Equal(D.Color.FromArgb(255, 64, 128, 64).ToArgb(), image.GetPixel(3, 3).ToArgb());
        Assert.Equal(D.Color.FromArgb(255, 32, 128, 96).ToArgb(), image.GetPixel(13, 3).ToArgb());
    }

    [Theory]
    [InlineData("polygon")]
    [InlineData("rectangle")]
    [InlineData("ellipse")]
    [Trait("Category", "AllocationContract")]
    public void Gradient_Fill_Does_Not_Allocate_Stop_Positions_Per_Pixel(string primitive)
    {
        using var image = new D.Bitmap(64, 48);
        using var g = D.Graphics.FromImage(image);
        using var brush = new G2.LinearGradientBrush(new D.Rectangle(0, 0, 64, 48), D.Color.Red, D.Color.Blue, 0f);
        D.PointF[] polygon = [new(0, 0), new(64, 0), new(64, 48), new(0, 48)];
        double bytes = BytesPerFill(() => Fill(g, brush, primitive, polygon, new D.RectangleF(0, 0, 64, 48)));
        // A broad ceiling permits per-draw preparation, but rejects thousands
        // of stop-position arrays. This is not a timing/GC-collection assertion.
        Assert.True(bytes <= 16_384, $"{primitive}: {bytes:N0} bytes/fill; limit 16,384. Prepare gradient data once per draw.");
        Assert.NotEqual(0, image.GetPixel(32, 24).A);
    }

    [Fact]
    [Trait("Category", "AllocationContract")]
    public void Small_Damage_Clip_Does_Not_Allocate_For_Every_OffClip_Scanline()
    {
        using var image = new D.Bitmap(960, 680);
        using var g = D.Graphics.FromImage(image);
        using var brush = new D.SolidBrush(D.Color.Red);
        g.SetClip(new D.Rectangle(400, 300, 96, 96));
        D.PointF[] polygon = [new(0, 0), new(960, 0), new(960, 680), new(0, 680)];
        double bytes = BytesPerFill(() => g.FillPolygon(brush, polygon));
        Assert.True(bytes <= 16_384, $"{bytes:N0} bytes/fill; limit 16,384. Bound scanlines and reuse crossings storage.");
        Assert.Equal(D.Color.Red.ToArgb(), image.GetPixel(410, 310).ToArgb());
        Assert.Equal(0, image.GetPixel(10, 10).A);
    }

    [Fact]
    [Trait("Category", "AllocationContract")]
    public void Empty_Clip_Does_Not_Perform_PerPixel_Gradient_Preparation()
    {
        using var image = new D.Bitmap(64, 48);
        using var g = D.Graphics.FromImage(image);
        using var clip = new D.Region();
        clip.MakeEmpty();
        g.SetClip(clip, G2.CombineMode.Replace);
        using var brush = new G2.LinearGradientBrush(new D.Rectangle(0, 0, 64, 48), D.Color.Red, D.Color.Blue, 0f);
        D.PointF[] polygon = [new(0, 0), new(64, 0), new(64, 48), new(0, 48)];
        double bytes = BytesPerFill(() => g.FillPolygon(brush, polygon));
        Assert.True(bytes <= 2_048, $"{bytes:N0} bytes/fill; empty clip limit 2,048.");
        Assert.All(image.ToRgbaBytes(), b => Assert.Equal((byte)0, b));
    }

    private static double BytesPerFill(Action fill)
    {
        for (int i = 0; i < 4; i++) fill();
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int count = 8;
        for (int i = 0; i < count; i++) fill();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;
    }

    private static D.Region MakeClip(string kind)
    {
        var clip = new D.Region(new D.RectangleF(8.25f, 7.5f, 25.25f, 24.25f));
        switch (kind)
        {
            case "disjoint": clip.Union(new D.RectangleF(45.25f, 38.5f, 10.25f, 12.25f)); break;
            case "hole": clip.Exclude(new D.RectangleF(16, 15, 8, 8)); break;
            case "empty": clip.MakeEmpty(); break;
            case "infinite": clip.MakeInfinite(); break;
            case "outside": clip.Translate(100, 100); break;
        }
        return clip;
    }

    private static void AssertMaskedParity(D.Region worldClip, G2.Matrix matrix, string primitive,
        G2.FillMode mode, G2.CompositingMode compositing, D.PointF[]? polygon = null, bool gradient = false)
    {
        using var reference = new D.Bitmap(64, 60);
        using var actual = new D.Bitmap(64, 60);
        using var full = D.Graphics.FromImage(reference);
        using var clipped = D.Graphics.FromImage(actual);
        full.Clear(Background);
        clipped.Clear(Background);
        full.Transform = matrix;
        clipped.Transform = matrix;
        full.CompositingMode = clipped.CompositingMode = compositing;
        clipped.SetClip(worldClip, G2.CombineMode.Replace);
        using var deviceClip = worldClip.Clone();
        deviceClip.Transform(matrix);
        using D.Brush brush = gradient
            ? new G2.LinearGradientBrush(new D.Rectangle(0, 0, 64, 60),
                D.Color.FromArgb(0, 255, 0, 0), D.Color.FromArgb(234, 0, 0, 255), 0f)
            {
                InterpolationColors = new G2.ColorBlend
                {
                    Colors = [D.Color.FromArgb(0, 255, 0, 0), D.Color.FromArgb(117, 0, 255, 0), D.Color.FromArgb(234, 0, 0, 255)],
                    Positions = [0f, 0.25f, 1f]
                }
            }
            : new D.SolidBrush(D.Color.FromArgb(117, 211, 73, 29));
        polygon ??= Polygon;
        Fill(full, brush, primitive, polygon, new D.RectangleF(-5.25f, -3.5f, 60.5f, 51.25f), mode);
        Fill(clipped, brush, primitive, polygon, new D.RectangleF(-5.25f, -3.5f, 60.5f, 51.25f), mode);
        int changed = 0;
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 64; x++)
            {
                int expected = deviceClip.IsVisible(x, y) ? reference.GetPixel(x, y).ToArgb() : Background.ToArgb();
                int observed = actual.GetPixel(x, y).ToArgb();
                Assert.True(expected == observed, $"{primitive}: pixel ({x},{y}), expected {expected:X8}, got {observed:X8}.");
                if (observed != Background.ToArgb()) changed++;
            }
        if (!deviceClip.IsEmpty(full) && deviceClip.IsVisible(20, 20)) Assert.True(changed > 0);
    }

    private static void Fill(D.Graphics g, D.Brush brush, string primitive, D.PointF[] polygon,
        D.RectangleF rect, G2.FillMode mode = G2.FillMode.Alternate)
    {
        switch (primitive)
        {
            case "polygon": g.FillPolygon(brush, polygon, mode); break;
            case "rectangle": g.FillRectangle(brush, rect); break;
            case "ellipse": g.FillEllipse(brush, rect); break;
            default: throw new ArgumentOutOfRangeException(nameof(primitive));
        }
    }
}
