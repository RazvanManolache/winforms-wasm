// Regression: GDI+ MeasureString drops trailing spaces/tabs unless
// StringFormatFlags.MeasureTrailingSpaces, and whitespace-only strings
// measure one space wide by default but zero-width when typographic or
// tight (desktop oracle gdimeasure: "The "=="The" both formats;
// " "=4.00 wide by default, -0.00 typo, heights 17.46/15.96). Telerik TPF
// lays inter-word gaps out as space elements: measuring " " as 4px grew
// every RadTextBoxControl line ~4px per gap (~14% too wide vs desktop).
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfStringFormat = WfDraw::System.Drawing.StringFormat;
using WfStringFormatFlags = WfDraw::System.Drawing.StringFormatFlags;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;

namespace WfWasm.Drawing.Tests;

public sealed class MeasureTypographicWhitespaceTests
{
    private static WfBrowserText.Raster Solid30x12()
    {
        byte[] rgba = new byte[30 * 12 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(30, 12, rgba);
    }

    [Fact]
    public void Typographic_Space_MeasuresZeroWidth_LineBoxHeight()
    {
        string? captured = null;
        WfBrowserText.MeasureOverride = (t, _, _, _) => { captured = t; return Solid30x12(); };
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureString(" ", font, int.MaxValue, WfStringFormat.GenericTypographic);
            Assert.Equal(0f, size.Width);
            Assert.InRange(size.Height, 14f, 18f);
            Assert.Null(captured);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void Default_Space_MeasuresOneSpace_Unpadded()
    {
        string? captured = null;
        WfBrowserText.MeasureOverride = (t, _, _, _) => { captured = t; return Solid30x12(); };
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureString("  ", font, int.MaxValue, WfStringFormat.GenericDefault);
            Assert.Equal(30f, size.Width);
            Assert.InRange(size.Height, 16f, 19f);
            Assert.Equal(" ", captured);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void TrailingSpace_TrimmedUnlessFlagSet()
    {
        string? captured = null;
        WfBrowserText.MeasureOverride = (t, _, _, _) => { captured = t; return Solid30x12(); };
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            g.MeasureString("hi ", font, int.MaxValue, WfStringFormat.GenericTypographic);
            Assert.Equal("hi", captured);
            g.MeasureString("hi ", font, int.MaxValue, WfStringFormat.GenericDefault);
            Assert.Equal("hi", captured);
            using var keep = new WfStringFormat(WfStringFormat.GenericDefault);
            keep.FormatFlags |= WfStringFormatFlags.MeasureTrailingSpaces;
            g.MeasureString("hi ", font, int.MaxValue, keep);
            Assert.Equal("hi ", captured);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void TightEntry_Space_MeasuresZeroWidth()
    {
        WfBrowserText.MeasureOverride = (_, _, _, _) => Solid30x12();
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureStringTight(" ", font);
            Assert.Equal(0f, size.Width);
            Assert.InRange(size.Height, 14f, 18f);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void FractionalWidth_UsedWhenSeamSet()
    {
        WfBrowserText.MeasureOverride = (_, _, _, _) => Solid30x12();
        WfBrowserText.MeasureWidthOverride = (_, _, _, _) => 25.83f;
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureString("hi", font, int.MaxValue, WfStringFormat.GenericTypographic);
            Assert.Equal(25.83f, size.Width);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
            WfBrowserText.MeasureWidthOverride = null;
        }
    }

    [Fact]
    public void FractionalWidth_FallsBackToRasterWithoutSeam()
    {
        WfBrowserText.MeasureOverride = (_, _, _, _) => Solid30x12();
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureString("hi", font, int.MaxValue, WfStringFormat.GenericTypographic);
            Assert.Equal(30f, size.Width);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void FractionalWidth_IgnoredWhenExceedingRaster()
    {
        WfBrowserText.MeasureOverride = (_, _, _, _) => Solid30x12();
        WfBrowserText.MeasureWidthOverride = (_, _, _, _) => 99f;
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureString("hi", font, int.MaxValue, WfStringFormat.GenericTypographic);
            Assert.Equal(30f, size.Width);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
            WfBrowserText.MeasureWidthOverride = null;
        }
    }

    [Fact]
    public void FractionalWidth_PaddedPathUnaffected()
    {
        WfBrowserText.MeasureOverride = (_, _, _, _) => Solid30x12();
        WfBrowserText.MeasureWidthOverride = (_, _, _, _) => 25.83f;
        try
        {
            using var bmp = new WfBitmap(8, 8);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont("Segoe UI", 9f);
            var size = g.MeasureString("hi", font, int.MaxValue, WfStringFormat.GenericDefault);
            Assert.InRange(size.Width, 34f, 35f);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
            WfBrowserText.MeasureWidthOverride = null;
        }
    }
}
