extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrushes = WfDraw::System.Drawing.Brushes;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfFontFamily = WfDraw::System.Drawing.FontFamily;
using WfFontStyle = WfDraw::System.Drawing.FontStyle;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfGraphicsUnit = WfDraw::System.Drawing.GraphicsUnit;
using WfIcon = WfDraw::System.Drawing.Icon;
using WfImageLockMode = WfDraw::System.Drawing.Imaging.ImageLockMode;
using WfPen = WfDraw::System.Drawing.Pen;
using WfPens = WfDraw::System.Drawing.Pens;
using WfPixelFormat = WfDraw::System.Drawing.Imaging.PixelFormat;
using WfPoint = WfDraw::System.Drawing.Point;
using WfPointF = WfDraw::System.Drawing.PointF;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfRegion = WfDraw::System.Drawing.Region;
using WfRotateFlipType = WfDraw::System.Drawing.RotateFlipType;
using WfSize = WfDraw::System.Drawing.Size;
using WfSizeF = WfDraw::System.Drawing.SizeF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfTextureBrush = WfDraw::System.Drawing.TextureBrush;
using WfSystemFonts = WfDraw::System.Drawing.SystemFonts;
using WfMatrix = WfDraw::System.Drawing.Drawing2D.Matrix;
using WfMatrixOrder = WfDraw::System.Drawing.Drawing2D.MatrixOrder;
using WfFillMode = WfDraw::System.Drawing.Drawing2D.FillMode;
using WfGraphicsPath = WfDraw::System.Drawing.Drawing2D.GraphicsPath;
using WfHatchBrush = WfDraw::System.Drawing.Drawing2D.HatchBrush;
using WfHatchStyle = WfDraw::System.Drawing.Drawing2D.HatchStyle;
using WfLinearGradientBrush = WfDraw::System.Drawing.Drawing2D.LinearGradientBrush;
using WfLinearGradientMode = WfDraw::System.Drawing.Drawing2D.LinearGradientMode;
using WfDrawImageCache = WfDraw::System.Drawing.DrawImageCache;

public sealed class RasterTests
{
    [Fact]
    public void Bitmap_Pixel_Roundtrips()
    {
        using var bitmap = new WfBitmap(4, 3);
        Assert.Equal(new WfSize(4, 3), bitmap.Size);
        Assert.Equal(WfPixelFormat.Format32bppArgb, bitmap.PixelFormat);
        bitmap.SetPixel(1, 2, WfColor.Red);
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(1, 2).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Graphics_Clear_And_FillRectangle()
    {
        using var bitmap = new WfBitmap(8, 8);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        graphics.FillRectangle(new WfSolidBrush(WfColor.Blue), new WfRectangle(2, 2, 3, 3));
        Assert.Equal(WfColor.Blue.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void Graphics_DrawLine_Hits_Diagonal()
    {
        using var bitmap = new WfBitmap(8, 8);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.Black);
        graphics.DrawLine(new WfPen(WfColor.White), 0, 0, 7, 7);
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(7, 7).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(4, 4).ToArgb());
    }

    [Fact]
    public void Graphics_DrawImage_Scales_Blit()
    {
        using var source = new WfBitmap(2, 2);
        source.SetPixel(0, 0, WfColor.Red);
        source.SetPixel(1, 1, WfColor.Lime);
        using var dest = new WfBitmap(4, 4);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.DrawImage(source, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void Bitmap_LockBits_Roundtrips_Bytes()
    {
        using var bitmap = new WfBitmap(3, 2);
        bitmap.SetPixel(0, 0, WfColor.FromArgb(255, 10, 20, 30));
        var data = bitmap.LockBits(new WfRectangle(0, 0, 3, 2), WfImageLockMode.ReadOnly, WfPixelFormat.Format32bppArgb);
        try
        {
            Assert.Equal(12, data.Stride);
            Assert.Equal(WfPixelFormat.Format32bppArgb, data.PixelFormat);
            Assert.NotEqual(IntPtr.Zero, data.Scan0);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        Assert.Equal(IntPtr.Zero, data.Scan0);
    }

    [Fact]
    public void Pen_Stores_Shape_And_Clones()
    {
        using var pen = new WfPen(WfColor.Red, 2.5f);
        Assert.Equal(WfColor.Red.ToArgb(), pen.Color.ToArgb());
        Assert.Equal(2.5f, pen.Width);
        pen.SetLineCap(LineCap.Round, LineCap.Square, DashCap.Round);
        Assert.Equal(LineCap.Round, pen.StartCap);
        Assert.Equal(LineCap.Square, pen.EndCap);
        var clone = (WfPen)pen.Clone();
        Assert.Equal(pen.Width, clone.Width);
        clone.Width = 9f;
        Assert.Equal(2.5f, pen.Width);
        Assert.NotNull(WfPens.Black);
        Assert.NotNull(WfBrushes.Red);
    }

    [Fact]
    public void Matrix_Translates_And_Inverts()
    {
        var matrix = new WfMatrix();
        Assert.True(matrix.IsIdentity);
        matrix.Translate(5f, 7f);
        var points = new[] { new WfPointF(1f, 1f) };
        matrix.TransformPoints(points);
        Assert.Equal(6f, points[0].X);
        Assert.Equal(8f, points[0].Y);
        matrix.Invert();
        matrix.TransformPoints(points);
        Assert.Equal(1f, points[0].X, 3);
        Assert.Equal(1f, points[0].Y, 3);
    }

    [Fact]
    public void Matrix_Multiply_Order_Matters()
    {
        var append = new WfMatrix();
        append.Translate(10f, 0f);
        append.Scale(2f, 2f, WfMatrixOrder.Append);
        var pa = new[] { new WfPointF(1f, 1f) };
        append.TransformPoints(pa);
        var prepend = new WfMatrix();
        prepend.Translate(10f, 0f);
        prepend.Scale(2f, 2f, WfMatrixOrder.Prepend);
        var pp = new[] { new WfPointF(1f, 1f) };
        prepend.TransformPoints(pp);
        Assert.NotEqual(pa[0].X, pp[0].X);
        Assert.Equal(22f, pa[0].X);
        Assert.Equal(12f, pp[0].X);
    }

    [Fact]
    public void Region_Set_Operations_Work()
    {
        using var region = new WfRegion(new WfRectangle(0, 0, 10, 10));
        Assert.True(region.IsVisible(5, 5));
        Assert.False(region.IsVisible(11, 5));
        region.Exclude(new WfRectangle(0, 0, 5, 10));
        Assert.False(region.IsVisible(2, 5));
        Assert.True(region.IsVisible(7, 5));
        region.Union(new WfRectangle(0, 0, 5, 10));
        Assert.True(region.IsVisible(2, 5));
        var clone = (WfRegion)region.Clone();
        Assert.True(clone.Equals(region, null));
        Assert.Equal(new WfRectangleF(0, 0, 10, 10), region.GetBounds(null));
    }

    [Fact]
    public void GraphicsPath_Rectangle_Flattens()
    {
        using var path = new WfGraphicsPath();
        path.AddRectangle(new WfRectangle(0, 0, 10, 5));
        Assert.Equal(4, path.PointCount);
        var bounds = path.GetBounds();
        Assert.Equal(10f, bounds.Width);
        Assert.Equal(5f, bounds.Height);
        using var ellipse = new WfGraphicsPath();
        ellipse.AddEllipse(new WfRectangleF(0, 0, 10, 10));
        Assert.True(ellipse.PointCount > 8);
        Assert.True(ellipse.IsVisible(new WfPointF(5, 5)));
        Assert.False(ellipse.IsVisible(new WfPointF(0, 0)));
    }

    [Fact]
    public void Bitmap_RotateFlip_Swaps_Dimensions()
    {
        using var bitmap = new WfBitmap(4, 2);
        bitmap.SetPixel(0, 0, WfColor.Red);
        bitmap.RotateFlip(WfRotateFlipType.Rotate90FlipNone);
        Assert.Equal(2, bitmap.Width);
        Assert.Equal(4, bitmap.Height);
    }

    [Fact]
    public void Bitmap_Clone_Crops()
    {
        using var bitmap = new WfBitmap(6, 6);
        bitmap.SetPixel(4, 4, WfColor.Blue);
        using var crop = bitmap.Clone(new WfRectangle(3, 3, 2, 2), WfPixelFormat.Format32bppRgb);
        Assert.Equal(new WfSize(2, 2), crop.Size);
        Assert.Equal(WfColor.Blue.ToArgb(), crop.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void MeasureString_Approximates()
    {
        using var bitmap = new WfBitmap(4, 4);
        using var graphics = WfGraphics.FromImage(bitmap);
        var size = graphics.MeasureString("hello", new WfFont("Arial", 12f));
        Assert.True(size.Width > 0f);
        Assert.True(size.Height > 0f);
        Assert.Equal(WfSizeF.Empty, graphics.MeasureString("", new WfFont("Arial", 12f)));
    }

    [Fact]
    public void SystemFonts_Resolve()
    {
        Assert.Equal("Tahoma", WfSystemFonts.MenuFont.Name);
        Assert.Equal("Tahoma", WfSystemFonts.DefaultFont.Name);
        Assert.Equal("Consolas", WfSystemFonts.GetFontByName("Consolas").Name);
    }

    [Fact]
    public void Font_Gaps_Close()
    {
        var byUnit = new WfFont("Arial", 12f, WfGraphicsUnit.Pixel);
        Assert.Equal(WfGraphicsUnit.Pixel, byUnit.Unit);
        var byFamily = new WfFont(new WfFontFamily("Arial"), 10f, WfGraphicsUnit.Point);
        Assert.Equal(10f, byFamily.Size);
        Assert.Equal(12f * 72f / 96f, byUnit.SizeInPoints);
        Assert.Equal("Arial", byUnit.OriginalFontName);
        Assert.Equal("", byUnit.SystemFontName);
        Assert.True(byUnit.GetHeight() > 0f);
        using var bitmap = new WfBitmap(2, 2);
        using var graphics = WfGraphics.FromImage(bitmap);
        Assert.True(byUnit.GetHeight(graphics) > 0f);
        var hfont = byUnit.ToHfont();
        Assert.NotEqual(IntPtr.Zero, hfont);
        Assert.NotEqual(hfont, byUnit.ToHfont());
        Assert.Same(byUnit, WfFont.FromHfont(hfont));
        byUnit.ToLogFont(out var logFont);
        Assert.Equal("Arial", new string(logFont.lfFaceName).TrimEnd((char)0));
        var back = WfFont.FromLogFont(logFont);
        Assert.Equal("Arial", back.Name);
    }

    [Fact]
    public void Brushes_Store_And_Clone()
    {
        using var hatch = new WfHatchBrush(WfHatchStyle.DiagonalCross, WfColor.Red, WfColor.Blue);
        Assert.Equal(WfHatchStyle.DiagonalCross, hatch.HatchStyle);
        var hclone = (WfHatchBrush)hatch.Clone();
        Assert.Equal(WfColor.Blue.ToArgb(), hclone.BackgroundColor.ToArgb());
        using var linear = new WfLinearGradientBrush(
            new WfPoint(0, 0), new WfPoint(10, 0), WfColor.Red, WfColor.Blue);
        Assert.Equal(2, linear.LinearColors.Length);
        var lclone = (WfLinearGradientBrush)linear.Clone();
        Assert.Equal(2, lclone.LinearColors.Length);
        using var target = new WfBitmap(6, 1);
        using var graphics = WfGraphics.FromImage(target);
        graphics.FillRectangle(linear, new WfRectangle(0, 0, 6, 1));
        Assert.NotEqual(0, target.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Icon_ToBitmap_Scales()
    {
        using var bitmap = new WfBitmap(4, 4);
        bitmap.SetPixel(0, 0, WfColor.Green);
        var icon = new WfIcon(bitmap);
        Assert.Equal(new WfSize(4, 4), icon.Size);
        using var back = icon.ToBitmap();
        Assert.Equal(WfColor.Green.ToArgb(), back.GetPixel(0, 0).ToArgb());
        var clone = (WfIcon)icon.Clone();
        Assert.Equal(icon.Size, clone.Size);
        clone.Dispose();
        icon.Dispose();
    }

    [Fact]
    public void DrawImage_Translucent_Blends_Exact()
    {
        // Hand-computed SourceOver: src(128,255,0,0) over dst(255,0,0,255)
        // => r=(255*128)/255=128, g=0, b=(255*127)/255=127, a=128+127=255.
        using var src = new WfBitmap(1, 1);
        src.SetPixel(0, 0, WfColor.FromArgb(128, 255, 0, 0));
        using var dest = new WfBitmap(1, 1);
        dest.SetPixel(0, 0, WfColor.FromArgb(255, 0, 0, 255));
        using var graphics = WfGraphics.FromImage(dest);
        graphics.DrawImage(src, new WfRectangle(0, 0, 1, 1));
        Assert.Equal(WfColor.FromArgb(255, 128, 0, 127).ToArgb(), dest.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void DrawImage_SourceCopy_Writes_Verbatim()
    {
        using var src = new WfBitmap(1, 1);
        src.SetPixel(0, 0, WfColor.FromArgb(128, 255, 0, 0));
        using var dest = new WfBitmap(1, 1);
        dest.SetPixel(0, 0, WfColor.FromArgb(255, 0, 0, 255));
        using var graphics = WfGraphics.FromImage(dest);
        graphics.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        graphics.DrawImage(src, new WfRectangle(0, 0, 1, 1));
        Assert.Equal(WfColor.FromArgb(128, 255, 0, 0).ToArgb(), dest.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void DrawImage_Partially_Offscreen_Clips()
    {
        // Dest rect (-1,-1,2,2) over a 2x2 target: only (0,0) is visible,
        // sampling src local (1.5,1.5) -> src pixel (1,1).
        using var src = new WfBitmap(2, 2);
        src.SetPixel(0, 0, WfColor.Red);
        src.SetPixel(1, 0, WfColor.Lime);
        src.SetPixel(0, 1, WfColor.Blue);
        src.SetPixel(1, 1, WfColor.Yellow);
        using var dest = new WfBitmap(2, 2);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.DrawImage(src, new WfRectangle(-1, -1, 2, 2));
        Assert.Equal(WfColor.Yellow.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void DrawImage_Upscale_Maps_Quadrants()
    {
        // 2x2 -> 4x4 nearest: dest x 0,1 sample src 0; 2,3 sample src 1
        // ((x+.5)*0.5: .25,.75,1.25,1.75 -> 0,0,1,1).
        using var src = new WfBitmap(2, 2);
        src.SetPixel(0, 0, WfColor.Red);
        src.SetPixel(1, 0, WfColor.Lime);
        src.SetPixel(0, 1, WfColor.Blue);
        src.SetPixel(1, 1, WfColor.Yellow);
        using var dest = new WfBitmap(4, 4);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.DrawImage(src, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(2, 0).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(3, 1).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(0, 2).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(1, 3).ToArgb());
        Assert.Equal(WfColor.Yellow.ToArgb(), dest.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.Yellow.ToArgb(), dest.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void ToRgbaBytes_Emits_Red_Green_Blue_Alpha_Order()
    {
        // Upload path (single pass, no LockBits roundtrip): int ARGB
        // pixels become RGBA bytes, hand-computed.
        using var bitmap = new WfBitmap(2, 1);
        bitmap.SetPixel(0, 0, WfColor.FromArgb(255, 10, 20, 30));
        bitmap.SetPixel(1, 0, WfColor.FromArgb(128, 40, 50, 60));
        Assert.Equal(new byte[] { 10, 20, 30, 255, 40, 50, 60, 128 }, bitmap.ToRgbaBytes());
    }

    [Fact]
    public void FillPath_Degenerate_Polygon_Noops()
    {
        // Winding scanlines with zero crossings (flattened SVG subpaths
        // collapsing to horizontal runs) paint nothing, never crash.
        // Regression: ArgumentOutOfRangeException | FillPolygonCore <-
        // Telerik SvgRenderer.FillPath (BugTracker SVG icons).
        using var bitmap = new WfBitmap(8, 8);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        using var flat = new WfGraphicsPath(WfFillMode.Winding);
        flat.AddPolygon(new[] { new WfPointF(1, 3), new WfPointF(5, 3), new WfPointF(7, 3) });
        var ex = Record.Exception(() => graphics.FillPath(new WfSolidBrush(WfColor.Red), flat));
        Assert.Null(ex);
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
        using var tri = new WfGraphicsPath(WfFillMode.Winding);
        tri.AddPolygon(new[] { new WfPointF(0, 0), new WfPointF(7, 0), new WfPointF(0, 7) });
        graphics.FillPath(new WfSolidBrush(WfColor.Blue), tri);
        Assert.Equal(WfColor.Blue.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void ToRgbaBytes_Rect_Equals_Full_Slice()
    {
        using var bitmap = new WfBitmap(8, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++)
                bitmap.SetPixel(x, y, WfColor.FromArgb((x * 37 + y * 91) & 255, (x * 11) & 255, (y * 53) & 255, (x + y * 7) & 255));
        var rc = new WfRectangle(2, 1, 4, 3);
        byte[] slice = bitmap.ToRgbaBytes(rc);
        byte[] full = bitmap.ToRgbaBytes();
        Assert.Equal(4 * 3 * 4, slice.Length);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
                for (int k = 0; k < 4; k++)
                    Assert.Equal(full[(((1 + y) * 8) + (2 + x)) * 4 + k], slice[((y * 4) + x) * 4 + k]);
    }

    [Fact]
    public void ToRgbaBytes_Rect_Outside_Yields_Empty()
    {
        using var bitmap = new WfBitmap(8, 6);
        Assert.Empty(bitmap.ToRgbaBytes(new WfRectangle(20, 20, 4, 4)));
        // Partial overlap intersects.
        Assert.Equal(2 * 6 * 4, bitmap.ToRgbaBytes(new WfRectangle(6, 0, 9, 9)).Length);
    }

    [Fact]
    public void Clipped_Blit_Equals_Unclipped_Slice_And_Preserves_Surround()
    {
        using var source = new WfBitmap(6, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                source.SetPixel(x, y, WfColor.FromArgb(255, (x * 41) & 255, (y * 67) & 255, (x * y * 13) & 255));
        using var full = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(full))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        var rc = new WfRectangle(3, 2, 4, 5);
        using var clipped = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.SetClip(rc);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
            {
                int expected = rc.Contains(x, y) ? full.GetPixel(x, y).ToArgb() : WfColor.White.ToArgb();
                Assert.Equal(expected, clipped.GetPixel(x, y).ToArgb());
            }
    }

    [Fact]
    public void Clipped_Parallelogram_Equals_Unclipped_Slice()
    {
        using var source = new WfBitmap(6, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                source.SetPixel(x, y, WfColor.FromArgb(255, (x * 41) & 255, (y * 67) & 255, (x * y * 13) & 255));
        var dest = new[] { new WfPointF(1, 1), new WfPointF(9, 2), new WfPointF(0, 9) };
        using var full = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(full))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        var rc = new WfRectangle(3, 2, 4, 5);
        using var clipped = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.SetClip(rc);
            g.DrawImage(source, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
            {
                int expected = rc.Contains(x, y) ? full.GetPixel(x, y).ToArgb() : WfColor.White.ToArgb();
                Assert.Equal(expected, clipped.GetPixel(x, y).ToArgb());
            }
    }

    [Fact]
    public void Clipped_Blit_Region_Slow_Path_Matches_Rect_Fast_Path()
    {
        // A union region (no single int rect) keeps per-pixel checks;
        // its output must equal the integer-rect fast path exactly.
        using var source = new WfBitmap(6, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                source.SetPixel(x, y, WfColor.FromArgb(255, (x * 41) & 255, (y * 67) & 255, (x * y * 13) & 255));
        using var fast = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(fast))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangle(2, 2, 6, 6));
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        using var slow = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(slow))
        {
            g.Clear(WfColor.White);
            using var region = new WfRegion(new WfRectangle(2, 2, 6, 6));
            region.Union(new WfRectangleF(2.5f, 2.5f, 1, 1));
            g.SetClip(region, WfDraw::System.Drawing.Drawing2D.CombineMode.Replace);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        Assert.Equal(fast.ToRgbaBytes(), slow.ToRgbaBytes());
    }

    [Fact]
    public void Writes_Bump_PaintVersion_Clone_Preserves_It()
    {
        using var bitmap = new WfBitmap(4, 4);
        Assert.Equal(0, bitmap.PaintVersion);
        Assert.Equal(0, bitmap.ClearVersion);
        using (var g = WfGraphics.FromImage(bitmap))
        {
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangle(0, 0, 2, 2));
            int filled = bitmap.PaintVersion;
            Assert.True(filled > 0);
            Assert.Equal(0, bitmap.ClearVersion);
            g.Clear(WfColor.Blue);
            Assert.True(bitmap.PaintVersion > filled);
            Assert.Equal(1, bitmap.ClearVersion);
        }
        int stamped = bitmap.PaintVersion;
        using var clone = (WfBitmap)bitmap.Clone();
        Assert.Equal(stamped, clone.PaintVersion);
        clone.SetPixel(0, 0, WfColor.Black);
        Assert.True(clone.PaintVersion > stamped);
        Assert.Equal(stamped, bitmap.PaintVersion);
    }

    private static WfBitmap PatternSource(int w = 6, int h = 6)
    {
        var source = new WfBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                source.SetPixel(x, y, WfColor.FromArgb(255, (x * 41) & 255, (y * 67) & 255, (x * y * 13) & 255));
        return source;
    }

    [Fact]
    public void Memo_Hit_Equals_Direct_Blit()
    {
        using var source = PatternSource();
        using var first = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(first))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        // Identical second draw (memo recall) over the same background.
        using var second = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(second))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        Assert.Equal(first.ToRgbaBytes(), second.ToRgbaBytes());
        // Third identical draw: the stash-on-repeat policy recorded the
        // first sighting and stashed the second, so this one recalls.
        using var third = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(third))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        Assert.Equal(first.ToRgbaBytes(), third.ToRgbaBytes());
    }

    [Fact]
    public void Memo_Invalidated_By_Src_SetPixel()
    {
        using var source = PatternSource();
        using var before = new WfBitmap(6, 6);
        using (var g = WfGraphics.FromImage(before))
            g.DrawImage(source, new WfRectangle(0, 0, 6, 6));
        source.SetPixel(2, 3, WfColor.Magenta);
        using var after = new WfBitmap(6, 6);
        using (var g = WfGraphics.FromImage(after))
            g.DrawImage(source, new WfRectangle(0, 0, 6, 6));
        Assert.Equal(WfColor.Magenta.ToArgb(), after.GetPixel(2, 3).ToArgb());
        Assert.NotEqual(before.ToRgbaBytes(), after.ToRgbaBytes());
    }

    [Fact]
    public void Memo_Invalidated_By_Graphics_Session()
    {
        using var source = PatternSource();
        using var before = new WfBitmap(6, 6);
        using (var g = WfGraphics.FromImage(before))
            g.DrawImage(source, new WfRectangle(0, 0, 6, 6));
        using (var g = WfGraphics.FromImage(source))
            g.FillRectangle(new WfSolidBrush(WfColor.Cyan), new WfRectangle(1, 1, 2, 2));
        using var after = new WfBitmap(6, 6);
        using (var g = WfGraphics.FromImage(after))
            g.DrawImage(source, new WfRectangle(0, 0, 6, 6));
        Assert.Equal(WfColor.Cyan.ToArgb(), after.GetPixel(1, 1).ToArgb());
        Assert.NotEqual(before.ToRgbaBytes(), after.ToRgbaBytes());
    }

    [Fact]
    public void Memo_Respects_Position_And_Clip()
    {
        using var source = PatternSource();
        using var full = new WfBitmap(12, 12);
        using (var g = WfGraphics.FromImage(full))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        // Same draw at a fresh position recalls the layer, not the pixels.
        using var moved = new WfBitmap(16, 16);
        using (var g = WfGraphics.FromImage(moved))
        {
            g.Clear(WfColor.White);
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
            g.DrawImage(source, new WfRectangle(7, 7, 8, 8));
        }
        Assert.Equal(full.GetPixel(2, 2).ToArgb(), moved.GetPixel(2, 2).ToArgb());
        Assert.Equal(full.GetPixel(2, 2).ToArgb(), moved.GetPixel(8, 8).ToArgb());
        // A tighter clip on the same draw recalls nothing stale outside it.
        using var tight = new WfBitmap(12, 12);
        using (var g = WfGraphics.FromImage(tight))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangle(3, 3, 2, 2));
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                bool inside = x >= 3 && x < 5 && y >= 3 && y < 5;
                int expected = inside ? full.GetPixel(x, y).ToArgb() : WfColor.White.ToArgb();
                Assert.Equal(expected, tight.GetPixel(x, y).ToArgb());
            }
        // Same clipped draw again: recalls the stashed layer through the
        // row-copy fast path (opaque source, rect clip), identical bytes.
        using var tightAgain = new WfBitmap(12, 12);
        using (var g = WfGraphics.FromImage(tightAgain))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangle(3, 3, 2, 2));
            g.DrawImage(source, new WfRectangle(1, 1, 8, 8));
        }
        Assert.Equal(tight.ToRgbaBytes(), tightAgain.ToRgbaBytes());
    }

    [Fact]
    public void Memo_Translucent_Blends_Over_Live_Dst()
    {
        using var source = new WfBitmap(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                source.SetPixel(x, y, WfColor.FromArgb(128, 255, 0, 0));
        using var onRed = new WfBitmap(4, 4);
        using (var g = WfGraphics.FromImage(onRed))
        {
            g.Clear(WfColor.Red);
            g.DrawImage(source, new WfRectangle(0, 0, 4, 4));
        }
        using var onBlue = new WfBitmap(4, 4);
        using (var g = WfGraphics.FromImage(onBlue))
        {
            g.Clear(WfColor.Blue);
            g.DrawImage(source, new WfRectangle(0, 0, 4, 4));
        }
        // Same recalled layer, blended over three live backgrounds.
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), onRed.GetPixel(1, 1).ToArgb());
        int blended = onBlue.GetPixel(1, 1).ToArgb();
        Assert.Equal(255, (blended >> 24) & 255);
        Assert.True(((blended >> 16) & 255) > 100 && ((blended >> 16) & 255) < 160);
        Assert.True((blended & 255) > 100 && (blended & 255) < 160);
        // Third draw over a third background: this is the actual recall,
        // and it must blend the stashed layer over the live background.
        using var onGreen = new WfBitmap(4, 4);
        using (var g = WfGraphics.FromImage(onGreen))
        {
            g.Clear(WfColor.Green);
            g.DrawImage(source, new WfRectangle(0, 0, 4, 4));
        }
        int blendedGreen = onGreen.GetPixel(1, 1).ToArgb();
        Assert.Equal(255, (blendedGreen >> 24) & 255);
        Assert.True(((blendedGreen >> 16) & 255) > 120 && ((blendedGreen >> 16) & 255) < 136);
        Assert.True(((blendedGreen >> 8) & 255) > 55 && ((blendedGreen >> 8) & 255) < 72);
        Assert.Equal(0, blendedGreen & 255);
    }

    [Fact]
    public void Memo_Para_Hit_Equals_Direct_Under_SourceCopy()
    {
        // Rotation leaves uninked parallelogram holes; under SourceCopy
        // a hit must skip them exactly like the direct draw (the inked
        // bitmask), never stamp transparent pixels over the background.
        using var source = PatternSource();
        var dest = new[] { new WfPointF(1, 1), new WfPointF(9, 2), new WfPointF(0, 9) };
        using var first = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(first))
        {
            g.Clear(WfColor.Green);
            g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(source, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        using var second = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(second))
        {
            g.Clear(WfColor.Green);
            g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(source, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        Assert.Equal(first.ToRgbaBytes(), second.ToRgbaBytes());
        // Third identical draw recalls through the inked-run fast path
        // (SourceCopy forces direct stores; the tilt leaves holes to skip).
        using var third = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(third))
        {
            g.Clear(WfColor.Green);
            g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(source, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        Assert.Equal(first.ToRgbaBytes(), third.ToRgbaBytes());
        // Same tilted draw under a rect clip recalls (key already stashed);
        // a pixel-identical fresh source forces the direct path as reference.
        using var clipped = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.Green);
            g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.SetClip(new WfRectangle(2, 2, 5, 5));
            g.DrawImage(source, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        using var source2 = new WfBitmap(6, 6);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                source2.SetPixel(x, y, source.GetPixel(x, y));
        using var clippedDirect = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(clippedDirect))
        {
            g.Clear(WfColor.Green);
            g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.SetClip(new WfRectangle(2, 2, 5, 5));
            g.DrawImage(source2, dest, new WfRectangle(0, 0, 6, 6), WfGraphicsUnit.Pixel);
        }
        Assert.Equal(clippedDirect.ToRgbaBytes(), clipped.ToRgbaBytes());
        Assert.Equal(WfColor.Green.ToArgb(), second.GetPixel(9, 9).ToArgb());
    }

    [Fact]
    public void Texture_Fill_Matches_Independent_Reference()
    {
        // 5x3 mixed-alpha tile over 12x10: wrap, SourceOver/SourceCopy,
        // unclipped/rect-clipped/complex-clipped/negative-offset. The
        // reference replicates the contract (mod sample + blend math)
        // without touching the fast path.
        using var tile = new WfBitmap(5, 3);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
                tile.SetPixel(x, y, WfColor.FromArgb((x * 61 + y * 37) & 255, (x * 43) & 255, (y * 91) & 255, (x + y * 5) & 255));
        int[] tilePx = new int[15];
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
                tilePx[y * 5 + x] = tile.GetPixel(x, y).ToArgb();
        var modes = new[] { WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceOver, WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy };
        var cases = new (WfRectangle Rect, WfRectangle? Clip, bool Complex)[]
        {
            (new WfRectangle(0, 0, 12, 10), null, false),
            (new WfRectangle(2, 1, 9, 8), new WfRectangle(3, 3, 4, 4), false),
            (new WfRectangle(-3, -2, 10, 9), null, false),
            (new WfRectangle(0, 0, 12, 10), null, true),
        };
        foreach (var mode in modes)
        {
            foreach (var (rc, clip, complex) in cases)
            {
                using var dest = new WfBitmap(12, 10);
                using (var g = WfGraphics.FromImage(dest))
                {
                    g.Clear(WfColor.White);
                    g.CompositingMode = mode;
                    if (complex)
                    {
                        using var region = new WfRegion(new WfRectangle(1, 1, 5, 8));
                        region.Union(new WfRectangle(6, 2, 5, 6));
                        g.SetClip(region, WfDraw::System.Drawing.Drawing2D.CombineMode.Replace);
                    }
                    else if (clip is not null)
                    {
                        g.SetClip(clip.Value);
                    }
                    using var brush = new WfTextureBrush(tile);
                    g.FillRectangle(brush, rc);
                }
                for (int y = 0; y < 10; y++)
                {
                    for (int x = 0; x < 12; x++)
                    {
                        bool inRect = rc.Contains(x, y);
                        bool inClip = complex
                            ? (x >= 1 && x < 6 && y >= 1 && y < 9) || (x >= 6 && x < 11 && y >= 2 && y < 8)
                            : clip is null || clip.Value.Contains(x, y);
                        int expected = unchecked((int)0xFFFFFFFF);
                        if (inRect && inClip)
                        {
                            int s = tilePx[(((y % 3) + 3) % 3) * 5 + (((x % 5) + 5) % 5)];
                            int sa = (s >> 24) & 255;
                            if (mode == WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy || sa == 255)
                            {
                                expected = s;
                            }
                            else
                            {
                                int inv = 255 - sa;
                                int r = ((((s >> 16) & 255) * sa) + (255 * inv)) / 255;
                                int g = ((((s >> 8) & 255) * sa) + (255 * inv)) / 255;
                                int b = (((s & 255) * sa) + (255 * inv)) / 255;
                                int a = sa + inv;
                                expected = (a << 24) | (r << 16) | (g << 8) | b;
                            }
                        }
                        Assert.True(expected == dest.GetPixel(x, y).ToArgb(), $"mode={mode} rect={rc} clip={clip} complex={complex} at ({x},{y})");
                    }
                }
            }
        }
    }

    [Fact]
    public void DrawImage_Empty_Clip_Intersection_Is_Noop()
    {
        using var source = PatternSource();
        using var dest = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(dest))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangle(20, 20, 5, 5));
            var ex = Record.Exception(() => g.DrawImage(source, new WfRectangle(1, 1, 8, 8)));
            Assert.Null(ex);
        }
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void Memo_Self_Draw_Invalidates_Target_As_Source()
    {
        using var canvas = PatternSource(8, 8);
        using var first = new WfBitmap(8, 8);
        using (var g = WfGraphics.FromImage(first))
            g.DrawImage(canvas, new WfRectangle(0, 0, 8, 8));
        int preScroll = first.GetPixel(2, 0).ToArgb();
        // Self-draw mutates the source mid-draw: never memoized, and the
        // target bump invalidates the stored entry for later draws.
        using (var g = WfGraphics.FromImage(canvas))
            g.DrawImage(canvas, new WfRectangle(2, 0, 6, 8));
        int scrolled = canvas.GetPixel(2, 0).ToArgb();
        Assert.NotEqual(preScroll, scrolled);
        using var probe = new WfBitmap(8, 8);
        using (var g = WfGraphics.FromImage(probe))
            g.DrawImage(canvas, new WfRectangle(0, 0, 8, 8));
        Assert.Equal(scrolled, probe.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void Memo_Stash_On_Repeat_Skips_One_Shot()
    {
        // First sighting records (run direct, no stash alloc); the repeat
        // stashes; a distinct key starts over; buckets are per-source.
        using var source = PatternSource();
        using var other = PatternSource();
        using var target = new WfBitmap(8, 8);
        Assert.True(WfDrawImageCache.KeyForBlit(source, target, 0, 0, 6, 6, 6, 6,
            WfDraw::System.Drawing.Drawing2D.InterpolationMode.Default, null, out var key));
        Assert.False(WfDrawImageCache.NoteRepeat(source, key));
        Assert.True(WfDrawImageCache.NoteRepeat(source, key));
        Assert.True(WfDrawImageCache.KeyForBlit(source, target, 0, 0, 6, 6, 4, 4,
            WfDraw::System.Drawing.Drawing2D.InterpolationMode.Default, null, out var key2));
        Assert.False(WfDrawImageCache.NoteRepeat(source, key2));
        Assert.False(WfDrawImageCache.NoteRepeat(other, key));
    }
}
