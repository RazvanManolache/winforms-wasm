// Regression: Graphics.DrawString with an int.MaxValue layout rect (the
// standard "don't wrap" idiom, used by Telerik TPF text) painted nothing:
// dx+dw rounds to 2^31 in float and the unchecked (int) cast wrapped the
// clip bound to int.MinValue, emptying the clip. RadTextBoxControl showed
// only squiggles in an empty box. The clip casts now saturate.
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrush = WfDraw::System.Drawing.Brush;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;

namespace WfWasm.Drawing.Tests;

public sealed class DrawStringHugeRectTests
{
    // 10x8 fully-opaque white raster; DrawString tints it by the brush.
    private static WfBrowserText.Raster Solid10x8()
    {
        byte[] rgba = new byte[10 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(10, 8, rgba);
    }

    private static int RedPixels(WfBitmap bmp)
    {
        int n = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.R > 200 && c.G < 80 && c.B < 80)
                    n++;
            }

        return n;
    }

    [Fact]
    public void DrawString_IntMaxLayoutRect_Paints()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid10x8();
        try
        {
            using var bmp = new WfBitmap(60, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using WfBrush brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Segoe UI", 9f);
            g.DrawString("hi", font, brush, new WfRectangleF(3, 2, int.MaxValue, int.MaxValue));

            Assert.True(RedPixels(bmp) >= 50, "huge-rect DrawString must paint its raster");
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(3, 2).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void DrawString_NormalRect_StillPaints()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid10x8();
        try
        {
            using var bmp = new WfBitmap(60, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using WfBrush brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Segoe UI", 9f);
            g.DrawString("hi", font, brush, new WfRectangleF(3, 2, 100, 20));

            Assert.True(RedPixels(bmp) >= 50, "normal-rect DrawString must keep painting");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void DrawString_Translated_Placement_Is_Exact()
    {
        // ToDevice translation fast path: translate(10,20) + draw at
        // (3,2) must land exactly on (13,22) — no double transform,
        // no dropped offset. Solid 10x8 raster => 80 red pixels there.
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid10x8();
        try
        {
            using var bmp = new WfBitmap(60, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            g.TranslateTransform(10f, 20f);
            using WfBrush brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Segoe UI", 9f);
            g.DrawString("hi", font, brush, new WfRectangleF(3, 2, 100, 20));

            Assert.Equal(80, RedPixels(bmp));
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(13, 22).ToArgb());
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(22, 29).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(12, 22).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(13, 21).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void DrawString_Translucent_Blend_Is_Exact()
    {
        // Hand-computed SourceOver (the hoisted loop must match):
        // bg white, half-red brush, raster alphas 255/128/64/0.
        // ea=(a*128)/255 => 128/64/32/skip; blend over white.
        byte[] rgba = new byte[2 * 2 * 4];
        rgba[3] = 255;
        rgba[7] = 128;
        rgba[11] = 64;
        rgba[15] = 0;
        for (int i = 0; i < 4; i++)
        {
            rgba[i * 4] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
        }
        WfBrowserText.RasterOverride = (_, _, _, _) => new WfBrowserText.Raster(2, 2, rgba);
        try
        {
            using var bmp = new WfBitmap(4, 4);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using WfBrush brush = new WfSolidBrush(WfColor.FromArgb(128, 255, 0, 0));
            using var font = new WfFont("Segoe UI", 9f);
            g.DrawString("hi", font, brush, new WfRectangleF(0, 0, 10, 10));

            Assert.Equal(unchecked((int)0xFFFF7F7F), bmp.GetPixel(0, 0).ToArgb());
            Assert.Equal(unchecked((int)0xFFFFBFBF), bmp.GetPixel(1, 0).ToArgb());
            Assert.Equal(unchecked((int)0xFFFFDFDF), bmp.GetPixel(0, 1).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
