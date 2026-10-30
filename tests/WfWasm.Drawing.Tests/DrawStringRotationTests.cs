extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrushes = WfDraw::System.Drawing.Brushes;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;

namespace WfWasm.Drawing.Tests;

// Syncfusion Chart Y-axis title ("Tasks finished (%)" painted as horizontal
// wrapped "Tasl/fini/shed(/%)"): axis titles draw under
// TranslateTransform + RotateTransform(-90) but fork DrawString ignores glyph
// rotation (Graphics.cs: "Rotation beyond the origin is ignored"). GDI+
// orients glyphs with the world transform; the ink bounding box of a wide
// raster drawn under -90 degrees must be taller than wide.
public sealed class DrawStringRotationTests
{
    [Fact]
    public void Baseline_NoRotation_Paints_Horizontal_Band()
    {
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
            new WfBrowserText.Raster(20, 10, WhiteOpaque(20, 10));
        try
        {
            using var bmp = new WfBitmap(120, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Arial", 12f);
            g.TranslateTransform(60f, 10f);
            g.DrawString("AB", font, WfBrushes.Black, new WfRectangleF(0, 0, 200, 50));

            var (minX, minY, maxX, maxY) = InkBounds(bmp);
            Assert.True(maxX >= minX, "baseline: expected ink on the bitmap");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void NegativeNinety_Rotates_Glyph_Band_Vertical()
    {
        // Wide opaque block stands in for a horizontal text raster.
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
            new WfBrowserText.Raster(20, 10, WhiteOpaque(20, 10));
        try
        {
            using var bmp = new WfBitmap(120, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Arial", 12f);
            g.TranslateTransform(60f, 60f);
            g.RotateTransform(-90f);
            g.DrawString("AB", font, WfBrushes.Black, new WfRectangleF(0, 0, 200, 50));

            var (minX, minY, maxX, maxY) = InkBounds(bmp);
            Assert.True(maxX >= minX, "expected ink on the bitmap");
            int inkW = maxX - minX + 1;
            int inkH = maxY - minY + 1;
            Assert.True(inkH > inkW, $"ink should run vertical under -90 deg, was {inkW}x{inkH}");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void PositiveNinety_Rotates_Glyph_Band_Vertical()
    {
        // GDI+ +90 degrees maps text +x to device +y: the 20x10 raster
        // lands as a 10-wide, 20-tall band below the mapped origin.
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
            new WfBrowserText.Raster(20, 10, WhiteOpaque(20, 10));
        try
        {
            using var bmp = new WfBitmap(120, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Arial", 12f);
            g.TranslateTransform(60f, 20f);
            g.RotateTransform(90f);
            g.DrawString("AB", font, WfBrushes.Black, new WfRectangleF(0, 0, 200, 50));

            var (minX, minY, maxX, maxY) = InkBounds(bmp);
            Assert.True(maxX >= minX, "expected ink on the bitmap");
            int inkW = maxX - minX + 1;
            int inkH = maxY - minY + 1;
            Assert.True(inkH > inkW, $"ink should run vertical under +90 deg, was {inkW}x{inkH}");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void OneEighty_Flips_Glyph_Band_Horizontal()
    {
        // GDI+ 180 degrees maps text +x to device -x: the band keeps its
        // 20x10 shape, blitted left/up of the mapped origin.
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
            new WfBrowserText.Raster(20, 10, WhiteOpaque(20, 10));
        try
        {
            using var bmp = new WfBitmap(120, 120);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Arial", 12f);
            g.TranslateTransform(60f, 60f);
            g.RotateTransform(180f);
            g.DrawString("AB", font, WfBrushes.Black, new WfRectangleF(0, 0, 200, 50));

            var (minX, minY, maxX, maxY) = InkBounds(bmp);
            Assert.True(maxX >= minX, "expected ink on the bitmap");
            int inkW = maxX - minX + 1;
            int inkH = maxY - minY + 1;
            Assert.True(inkW > inkH, $"ink should run horizontal under 180 deg, was {inkW}x{inkH}");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    private static byte[] WhiteOpaque(int w, int h)
    {
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < rgba.Length; i++)
            rgba[i] = 255;
        return rgba;
    }

    private static (int minX, int minY, int maxX, int maxY) InkBounds(WfBitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                WfColor c = bmp.GetPixel(x, y);
                if (c.A > 8 && (c.R < 128 || c.G < 128 || c.B < 128))
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        return (minX, minY, maxX, maxY);
    }
}
