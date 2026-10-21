// Bounded DrawString census: the tile dump reads it to learn which rects
// reach the rasterizer. Pins counting (calls + empty rects + null format)
// through the public DrawString core with the headless raster seam.
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;

namespace WfWasm.Drawing.Tests;

public sealed class DrawStringCensusTests
{
    private static WfBrowserText.Raster Solid10x8()
    {
        byte[] rgba = new byte[10 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(10, 8, rgba);
    }

    private static int CensusDraws()
    {
        string census = WfGraphics.DumpDrawStringCensus();
        foreach (string part in census.Split(' '))
            if (part.StartsWith("draws=") && int.TryParse(part.Substring(6), out int n))
                return n;
        return -1;
    }

    [Fact]
    public void DrawString_Census_Counts_Rect_Shape()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid10x8();
        try
        {
            int before = CensusDraws();
            using var bmp = new WfBitmap(100, 60);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Segoe UI", 9f);
            g.DrawString("hi", font, brush, new WfRectangleF(3, 2, 90, 20), null);
            g.DrawString("hi", font, brush, new WfRectangleF(3, 2, 0, 0), null);
            string census = WfGraphics.DumpDrawStringCensus();
            Assert.Equal(before + 2, CensusDraws());
            Assert.Contains("nullFormat=", census);
            Assert.Contains("emptyRect=", census);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
