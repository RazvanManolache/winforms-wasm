// GDI+ layout trimming: over-wide single-line text fits to the layout
// width (ellipsis modes append U+2026). Headless seam: fixed 10px/char
// measure + solid raster, so the fitted string and its containment are
// exact. Regression: Telerik grid cells (NoClip + autoEllipsis) overran
// their columns (ERP) because trimming was never applied.
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfStringFormat = WfDraw::System.Drawing.StringFormat;
using WfStringFormatFlags = WfDraw::System.Drawing.StringFormatFlags;

namespace WfWasm.Drawing.Tests;

public sealed class LayoutTrimTests
{
    private static WfBrowserText.Raster Block10x8(string text)
    {
        int w = Math.Max(1, text.Length * 10);
        byte[] rgba = new byte[w * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(w, 8, rgba);
    }

    private static string PaintFitted(string text, float rectW, StringTrimming trimming, out WfColor firstInk, out WfColor pastEdge)
    {
        var painted = new List<string>();
        WfBrowserText.MeasureOverride = (t, _, _, _) => Block10x8(t);
        WfBrowserText.RasterOverride = (t, _, _, _) => { painted.Add(t); return Block10x8(t); };
        try
        {
            using var bmp = new WfBitmap(280, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using var brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Segoe UI", 9f);
            using var sf = new WfStringFormat(WfStringFormatFlags.NoWrap);
            sf.Trimming = trimming;
            g.DrawString(text, font, brush, new WfRectangleF(10, 4, rectW, 16), sf);
            firstInk = bmp.GetPixel(10, 5);
            pastEdge = bmp.GetPixel(10 + (int)rectW + 1, 5);
            return painted[^1];
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void EllipsisCharacter_Trims_To_Rect_With_Ellipsis()
    {
        string fitted = PaintFitted("The jamped dog", 40f, StringTrimming.EllipsisCharacter,
            out WfColor firstInk, out WfColor pastEdge);
        Assert.Equal("The…", fitted);
        Assert.Equal(WfColor.Red.ToArgb(), firstInk.ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), pastEdge.ToArgb());
    }

    [Fact]
    public void EllipsisWord_Backs_Off_To_Word_Boundary()
    {
        string fitted = PaintFitted("The jamped dog", 130f, StringTrimming.EllipsisWord,
            out _, out _);
        Assert.Equal("The jamped…", fitted);
    }

    [Fact]
    public void EllipsisPath_Keeps_Head_And_Tail()
    {
        string fitted = PaintFitted("The jamped dog", 60f, StringTrimming.EllipsisPath,
            out _, out _);
        Assert.Equal("The…og", fitted);
    }

    [Fact]
    public void Character_Trims_Without_Ellipsis()
    {
        string fitted = PaintFitted("The jamped dog", 40f, StringTrimming.Character,
            out _, out _);
        Assert.Equal("The ", fitted);
    }

    [Fact]
    public void Fitting_Text_Paints_Unchanged()
    {
        string fitted = PaintFitted("The jamped dog", 200f, StringTrimming.EllipsisCharacter,
            out WfColor firstInk, out WfColor pastEdge);
        Assert.Equal("The jamped dog", fitted);
        Assert.Equal(WfColor.Red.ToArgb(), firstInk.ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), pastEdge.ToArgb());
    }
}
