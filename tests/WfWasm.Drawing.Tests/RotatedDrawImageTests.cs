extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfGraphicsUnit = WfDraw::System.Drawing.GraphicsUnit;
using WfMatrix = WfDraw::System.Drawing.Drawing2D.Matrix;
using WfPointF = WfDraw::System.Drawing.PointF;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSizeF = WfDraw::System.Drawing.SizeF;
using Xunit;

public sealed class RotatedDrawImageTests
{
    private static WfBitmap QuadSource()
    {
        var source = new WfBitmap(2, 2);
        source.SetPixel(0, 0, WfColor.Red);
        source.SetPixel(1, 0, WfColor.Lime);
        source.SetPixel(0, 1, WfColor.Blue);
        source.SetPixel(1, 1, WfColor.White);
        return source;
    }

    [Fact]
    public void AxisAligned_RectBlit_IsPixelExact()
    {
        using var source = QuadSource();
        using var dest = new WfBitmap(6, 6);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.DrawImage(source, new WfRectangleF(2, 2, 2, 2),
            new WfRectangleF(0, 0, 2, 2), WfGraphicsUnit.Pixel);
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(3, 2).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(2, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(5, 5).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(1, 2).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(4, 4).ToArgb());
    }

    [Fact]
    public void AxisAligned_TranslateOnly_Keeps_FastPath_Shift()
    {
        using var source = QuadSource();
        using var dest = new WfBitmap(6, 6);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.TranslateTransform(1f, 0f);
        graphics.DrawImage(source, new WfRectangleF(2, 2, 2, 2),
            new WfRectangleF(0, 0, 2, 2), WfGraphicsUnit.Pixel);
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(3, 2).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(4, 2).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(4, 3).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(2, 2).ToArgb());
    }

    [Fact]
    public void ExplicitParallelogram_AxisAligned_Matches_RectBlit()
    {
        using var source = QuadSource();
        using var dest = new WfBitmap(6, 6);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.DrawImage(source,
            new[] { new WfPointF(2, 2), new WfPointF(4, 2), new WfPointF(2, 4) },
            new WfRectangleF(0, 0, 2, 2), WfGraphicsUnit.Pixel);
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(3, 2).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(2, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(4, 4).ToArgb());
    }

    [Fact]
    public void Rotated90_RectBlit_Paints_Rotated_Pixels()
    {
        // 90deg CW about (4,4): (x,y) -> (-y,x) + (8,0), so the (3,3,2,2)
        // rect maps onto itself with content rotated: TL<-BL, TR<-TL, BL<-BR, BR<-TR.
        using var source = QuadSource();
        using var dest = new WfBitmap(8, 8);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.Transform = new WfMatrix(0f, 1f, -1f, 0f, 8f, 0f);
        graphics.DrawImage(source, new WfRectangleF(3, 3, 2, 2),
            new WfRectangleF(0, 0, 2, 2), WfGraphicsUnit.Pixel);
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(4, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(3, 4).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(4, 4).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void Rotated90_UnscaledBlit_Paints_Rotated_Pixels()
    {
        using var source = QuadSource();
        using var dest = new WfBitmap(8, 8);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.Transform = new WfMatrix(0f, 1f, -1f, 0f, 8f, 0f);
        graphics.DrawImageUnscaled(source, 3, 3);
        Assert.Equal(WfColor.Blue.ToArgb(), dest.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), dest.GetPixel(4, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(3, 4).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), dest.GetPixel(4, 4).ToArgb());
    }

    [Fact]
    public void RotatedSmallAngle_RectBlit_Covers_Rotated_Area()
    {
        using var source = new WfBitmap(8, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                source.SetPixel(x, y, WfColor.White);
        using var dest = new WfBitmap(24, 24);
        using var graphics = WfGraphics.FromImage(dest);
        graphics.Clear(WfColor.Black);
        graphics.RotateTransform(30f);
        graphics.DrawImage(source, new WfRectangleF(8, 8, 8, 8),
            new WfRectangleF(0, 0, 8, 8), WfGraphicsUnit.Pixel);
        // Rect center (12,12) maps to device (4.4,16.4): the rotated copy
        // sits there, not at the unrotated spot.
        Assert.Equal(WfColor.White.ToArgb(), dest.GetPixel(4, 16).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(12, 12).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), dest.GetPixel(23, 23).ToArgb());
        int painted = 0;
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
                if (dest.GetPixel(x, y).ToArgb() != WfColor.Black.ToArgb())
                    painted++;
        Assert.InRange(painted, 40, 88);
    }

    [Fact]
    public void MeasureString_HeadlessFallback_IsSaneAndStable()
    {
        using var bitmap = new WfBitmap(8, 8);
        using var graphics = WfGraphics.FromImage(bitmap);
        using var font = new WfFont("Arial", 12f);
        var first = graphics.MeasureString("hello", font);
        var second = graphics.MeasureString("hello", font);
        Assert.Equal(first, second);
        Assert.True(first.Width > 0f);
        Assert.True(first.Height > 0f);
        var longer = graphics.MeasureString("hello world, this is a much longer line", font);
        Assert.True(longer.Width > first.Width);
        Assert.Equal(WfSizeF.Empty, graphics.MeasureString(string.Empty, font));
        Assert.Equal(WfSizeF.Empty, graphics.MeasureString(null, font));
        // Every overload degrades to the fallback instead of throwing.
        Assert.True(graphics.MeasureString("hello", font, 100).Width > 0f);
        Assert.True(graphics.MeasureString("hello", font, new WfSizeF(100, 50), null).Width > 0f);
        var sized = graphics.MeasureString("hello", font, new WfSizeF(100, 50), null,
            out int charsFitted, out int linesFilled);
        Assert.True(sized.Width > 0f);
        Assert.Equal("hello".Length, charsFitted);
        Assert.True(linesFilled >= 1);
    }

    [Fact]
    public void MeasureString_RasterPath_Returns_Raster_Size_And_Stays_Stable()
    {
        var previous = WfBrowserText.MeasureOverride;
        try
        {
            WfBrowserText.MeasureOverride = (text, font, dpi, maxW) =>
                new WfBrowserText.Raster(42, 13, new byte[42 * 13 * 4]);
            using var bitmap = new WfBitmap(8, 8);
            using var graphics = WfGraphics.FromImage(bitmap);
            using var font = new WfFont("Arial", 12f);
            var first = graphics.MeasureString("hi", font);
            Assert.Equal(42f, first.Width);
            Assert.Equal(13f, first.Height);
            Assert.Equal(first, graphics.MeasureString("hi", font));
            // Narrow layout wraps into rows like the historical fallback.
            var wrapped = graphics.MeasureString("hi", font, new WfSizeF(10, 0), null);
            Assert.Equal(10f, wrapped.Width);
            Assert.Equal(5 * 13f, wrapped.Height);
        }
        finally
        {
            WfBrowserText.MeasureOverride = previous;
        }
    }

    [Fact]
    public void MeasureString_RasterFailure_Falls_Back_Without_Throwing()
    {
        var previous = WfBrowserText.MeasureOverride;
        try
        {
            WfBrowserText.MeasureOverride = (text, font, dpi, maxW) => null;
            using var bitmap = new WfBitmap(8, 8);
            using var graphics = WfGraphics.FromImage(bitmap);
            using var font = new WfFont("Arial", 12f);
            var fallback = graphics.MeasureString("hello", font);
            Assert.True(fallback.Width > 0f);
            Assert.True(fallback.Height > 0f);

            WfBrowserText.MeasureOverride = (text, font, dpi, maxW) =>
                throw new InvalidOperationException("bridge down");
            var survived = graphics.MeasureString("hello", font);
            Assert.True(survived.Width > 0f);
            Assert.True(survived.Height > 0f);
        }
        finally
        {
            WfBrowserText.MeasureOverride = previous;
        }
    }
}
