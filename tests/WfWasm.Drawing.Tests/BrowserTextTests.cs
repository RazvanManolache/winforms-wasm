extern alias WfDraw;

using System;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfBrushes = WfDraw::System.Drawing.Brushes;
using WfFont = WfDraw::System.Drawing.Font;
using WfFontFamily = WfDraw::System.Drawing.FontFamily;
using WfFontStyle = WfDraw::System.Drawing.FontStyle;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSizeF = WfDraw::System.Drawing.SizeF;
using WfStringFormat = WfDraw::System.Drawing.StringFormat;
using WfStringFormatFlags = WfDraw::System.Drawing.StringFormatFlags;
using Xunit;

public sealed class BrowserTextTests
{
    [Fact]
    public void ToCss_Maps_Family_Size_And_Style()
    {
        using var regular = new WfFont(WfFontFamily.GenericSansSerif, 12f);
        Assert.Equal("16px 'Microsoft Sans Serif', sans-serif", WfBrowserText.ToCss(regular, 96f));
        using var bold = new WfFont(new WfFontFamily("Segoe UI"), 9f, WfFontStyle.Bold | WfFontStyle.Italic);
        Assert.Equal("italic bold 12px 'Segoe UI', sans-serif", WfBrowserText.ToCss(bold, 96f));
        Assert.Equal(12, WfBrowserText.ToPx(bold, 96f));
    }

    [Fact]
    public void Headless_DrawString_Throws_Honest_NotSupported()
    {
        // No browser canvas off-WASM: IsAvailable probes false and the
        // DrawString core keeps throwing instead of painting nothing.
        Assert.False(WfBrowserText.IsAvailable);
        Assert.Null(WfBrowserText.TryRaster("hi", new WfFont(WfFontFamily.GenericSansSerif, 9f), 96f, 200));
        using var bmp = new WfBitmap(64, 32);
        using var g = WfGraphics.FromImage(bmp);
        using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
        var ex = Assert.Throws<NotSupportedException>(() =>
            g.DrawString("hi", font, WfBrushes.Black, new WfRectangleF(0, 0, 64, 32), null));
        Assert.Contains("browser canvas bridge", ex.Message);
    }

    [Fact]
    public void MeasureString_Huge_Layout_Width_Stays_Unconstrained()
    {
        // int.MaxValue layout widths (Telerik unconstrained measure)
        // must reach the rasterizer unclamped: (int)Ceiling of the
        // float-widened value wraps to int.MinValue unchecked, which
        // Math.Max(1, ..) pinned to a 1px raster.
        // Regression: every Telerik label measured ~1px wide
        // (selectCategoryLabel rendered as "I").
        int seenMaxW = 0;
        WfBrowserText.MeasureOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new WfBrowserText.Raster(100, 20, new byte[100 * 20 * 4]);
        };
        try
        {
            using var bmp = new WfBitmap(64, 32);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
            var size = g.MeasureString("SELECT CATEGORY:", font, int.MaxValue);
            Assert.Equal(2048, seenMaxW);
            Assert.Equal(100f, size.Width);
            Assert.Equal(20f, size.Height);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void MeasureString_NoWrap_Stays_Unconstrained_And_Unfolded()
    {
        // GDI+ NoWrap: single line even in a narrow layout box. The
        // raster request goes out unconstrained and the row-fold is
        // skipped, so long Telerik labels measure one line.
        int seenMaxW = 0;
        WfBrowserText.MeasureOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new WfBrowserText.Raster(300, 20, new byte[300 * 20 * 4]);
        };
        try
        {
            using var bmp = new WfBitmap(64, 32);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
            using var format = new WfStringFormat(WfStringFormatFlags.NoWrap);
            var size = g.MeasureString("a long title that would wrap at 100px", font, 100, format);
            Assert.Equal(2048, seenMaxW);
            Assert.Equal(300f, size.Width);
            Assert.Equal(20f, size.Height);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void MeasureString_Default_Format_Wraps_At_Layout_Width()
    {
        // Without NoWrap the layout width constrains the raster, and
        // the JS side wraps (see wraptest.cjs + tile screenshots); the
        // C# side must request the narrow width.
        int seenMaxW = 0;
        WfBrowserText.MeasureOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new WfBrowserText.Raster(90, 80, new byte[90 * 80 * 4]);
        };
        try
        {
            using var bmp = new WfBitmap(64, 32);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
            var size = g.MeasureString("a long title that wraps at 100px", font, 100, null);
            Assert.Equal(100, seenMaxW);
            Assert.Equal(90f, size.Width);
            Assert.Equal(80f, size.Height);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void MeasureString_LinesFilled_Counts_Wrapped_Raster_Lines()
    {
        // linesFilled derives from the wrapped raster height, not from
        // explicit newlines: counting only \n starved Telerik text
        // layout of height (BookShelf titles: 1 line over a void).
        using var bmp = new WfBitmap(64, 32);
        using var g = WfGraphics.FromImage(bmp);
        using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
        float linePx = font.GetHeight(g.DpiY);
        Assert.True(linePx > 1f);
        int rasterH = (int)MathF.Round(4f * linePx);
        WfBrowserText.MeasureOverride = (text, f, dpi, maxW) =>
            new WfBrowserText.Raster(100, rasterH, new byte[100 * rasterH * 4]);
        try
        {
            var size = g.MeasureString("four wrapped lines here", font,
                new WfSizeF(100, 1000), null, out int chars, out int lines);
            Assert.Equal("four wrapped lines here".Length, chars);
            Assert.Equal(4, lines);
            Assert.Equal(rasterH, (int)size.Height);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void DrawString_Empty_Text_Is_No_Op_Everywhere()
    {
        // Desktop no-ops empty text before touching any rasterizer, so
        // headless (and raster-failure) callers never throw for ""/null.
        using var bmp = new WfBitmap(16, 16);
        using var g = WfGraphics.FromImage(bmp);
        using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
        g.DrawString(string.Empty, font, WfBrushes.Black, new WfRectangleF(0, 0, 16, 16), null);
        g.DrawString(null, font, WfBrushes.Black, 0f, 0f);
    }
}
