extern alias WfDraw;

using System;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfBrushes = WfDraw::System.Drawing.Brushes;
using WfFont = WfDraw::System.Drawing.Font;
using WfFontFamily = WfDraw::System.Drawing.FontFamily;
using WfFontStyle = WfDraw::System.Drawing.FontStyle;
using WfCounters = WfDraw::System.Drawing.TextRasterCounters;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSizeF = WfDraw::System.Drawing.SizeF;
using WfStringFormat = WfDraw::System.Drawing.StringFormat;
using WfStringFormatFlags = WfDraw::System.Drawing.StringFormatFlags;
using Xunit;

public sealed class BrowserTextTests
{
    [Fact]
    public void ToCss_Caches_By_Value()
    {
        using var a = new WfFont("Segoe UI", 9f);
        using var b = new WfFont("Segoe UI", 9f);
        // Equal-valued distinct instances share the cached string.
        Assert.Same(WfBrowserText.ToCss(a, 96f), WfBrowserText.ToCss(b, 96f));
        using var c = new WfFont("Segoe UI", 10f);
        Assert.NotEqual(WfBrowserText.ToCss(a, 96f), WfBrowserText.ToCss(c, 96f));
        using var d = new WfFont("Segoe UI", 9f);
        Assert.NotEqual(WfBrowserText.ToCss(a, 96f), WfBrowserText.ToCss(d, 120f));
    }

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
            // GDI+ layout pad (see MeasureString_GdiPlus_Pad_Matches_Desktop):
            // 100 + 12/3 + 0.03*100 - 0.5 = 106.5.
            Assert.Equal(106.5f, size.Width);
            // GDI+ line-box height: 12 x 1.1318 + 12/8 = 15.08.
            Assert.InRange(size.Height, 15.0, 15.2);
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
            // GDI+ layout pad: 300 + 12/3 + 0.03*300 - 0.5 = 312.5.
            Assert.Equal(312.5f, size.Width);
            // GDI+ line-box height: 12 x 1.1318 + 12/8 = 15.08.
            Assert.InRange(size.Height, 15.0, 15.2);
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
            // GDI+ layout pad: 90 + 12/3 + 0.03*90 - 0.5 = 96.2.
            Assert.InRange(size.Width, 96.1, 96.3);
            // GDI+ line-box height (string-independent): 15.08.
            Assert.InRange(size.Height, 15.0, 15.2);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void MeasureString_LinesFilled_Counts_Wrapped_Raster_Lines()
    {
        // linesFilled counts visual rows off the line-box grid: a
        // 100px raster in a 30px layout wraps to 4 rows, and the
        // height is rows x line box + em/8 (GDI+ rule, oracle
        // text-multi.fsx). Counting only \n starved Telerik text
        // layout of height (BookShelf titles: 1 line over a void).
        WfBrowserText.MeasureOverride = (text, f, dpi, maxW) =>
            new WfBrowserText.Raster(100, 48, new byte[100 * 48 * 4]);
        try
        {
            using var bmp = new WfBitmap(64, 32);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
            var size = g.MeasureString("four wrapped lines here", font,
                new WfSizeF(30, 1000), null, out int chars, out int lines);
            Assert.Equal("four wrapped lines here".Length, chars);
            Assert.Equal(4, lines);
            // 4 x 12 x 1.1318 + 12/8 = 55.83.
            Assert.InRange(size.Height, 55.5, 56.1);
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

    [Fact]
    public void MeasureString_GdiPlus_Pad_Matches_Desktop()
    {
        // GDI+ MeasureString(GenericDefault) pads the tight advances:
        // desktop oracle (text-padfit.fsx, Segoe UI 9): typographic
        // 25.83 -> GenericDefault 30.60, i.e. pad = em/3 + 0.03*width.
        // The production raster (26x15) is the ceiled typographic
        // width, so the pad carries a -0.5 raster-ceil correction:
        // 26 + 4 + 0.78 - 0.5 = 30.28 (desktop 30.60). Telerik tab
        // items size from this (BugTracker Bugs tab: 34px without
        // the pad, 39px native). GenericTypographic stays tight.
        WfBrowserText.MeasureOverride = (text, font, dpi, maxW) =>
            new WfBrowserText.Raster(26, 15, new byte[26 * 15 * 4]);
        try
        {
            using var bmp = new WfBitmap(64, 32);
            using var g = WfGraphics.FromImage(bmp);
            using var font = new WfFont(new WfFontFamily("Segoe UI"), 9f);
            var twoArg = g.MeasureString("Bugs", font);
            Assert.InRange(twoArg.Width, 30.0, 31.0);
            Assert.InRange(twoArg.Height, 17.3, 17.6);
            var fourArg = g.MeasureString("Bugs", font, 9999, WfStringFormat.GenericDefault);
            Assert.InRange(fourArg.Width, 30.0, 31.0);
            Assert.InRange(fourArg.Height, 17.3, 17.6);
            var typo = g.MeasureString("Bugs", font, 9999, WfStringFormat.GenericTypographic);
            Assert.Equal(26f, typo.Width);
            Assert.Equal(15f, typo.Height);
        }
        finally
        {
            WfBrowserText.MeasureOverride = null;
        }
    }

    [Fact]
    public void RasterCounters_Take_Snapshots_And_Resets()
    {
        // Perf item 4 contract: off by default (one bool read), Take
        // drains per frame. Headless TryRaster returns null past the
        // availability check but still counts the call.
        Assert.False(WfCounters.PerfEnabled);
        WfCounters.PerfEnabled = true;
        try
        {
            WfCounters.Take();
            using var font = new WfFont(WfFontFamily.GenericSansSerif, 9f);
            Assert.Null(WfBrowserText.TryRaster("hi", font, 96f, 200));
            Assert.Null(WfBrowserText.TryMeasureWidth("hi", font, 96f, 200));
            WfCounters.NoteDraw(2);
            var snap = WfCounters.Take();
            Assert.Equal(1, snap.DrawCalls);
            Assert.Equal(2, snap.DrawChars);
            Assert.Equal(1, snap.RasterCalls);
            Assert.Equal(0, snap.RasterHits);
            Assert.Equal(0, snap.RasterMisses);
            Assert.Equal(1, snap.MeasureWidthCalls);
            var empty = WfCounters.Take();
            Assert.Equal(0, empty.RasterCalls);
            WfCounters.NoteDrawPx(600);
            Assert.Equal(600, WfCounters.Take().DrawPx);
            WfCounters.NoteDrawTicks(44);
            Assert.Equal(44, WfCounters.Take().DrawTicks);
            WfCounters.NoteDrawPx(7);
            Assert.Equal(7, WfCounters.Peek().DrawPx);
            Assert.Equal(7, WfCounters.Take().DrawPx);
            // Op census contract: take drains, peek does not.
            WfCounters.NoteFill();
            WfCounters.NoteFillPx(10);
            WfCounters.NoteImage();
            WfCounters.NoteImagePx(20);
            WfCounters.NoteLine();
            var ops = WfCounters.Peek();
            Assert.Equal(1, ops.FillCalls);
            Assert.Equal(10, ops.FillPx);
            Assert.Equal(1, ops.ImageCalls);
            Assert.Equal(20, ops.ImagePx);
            Assert.Equal(1, ops.LineCalls);
            var drained = WfCounters.Take();
            Assert.Equal(1, drained.FillCalls);
            Assert.Equal(0, WfCounters.Peek().FillCalls);
            // Behavioral: a real fill and a real line trip the hooks.
            WfCounters.Take();
            using var bmp = new WfBitmap(16, 16);
            using var g = WfGraphics.FromImage(bmp);
            using var brush = new WfDraw::System.Drawing.SolidBrush(WfDraw::System.Drawing.Color.Red);
            g.FillRectangle(brush, new WfDraw::System.Drawing.RectangleF(1, 1, 4, 4));
            var seen = WfCounters.Take();
            Assert.Equal(1, seen.FillCalls);
            Assert.Equal(16, seen.FillPx);
            // Big-fill attribution contract.
            WfCounters.NoteBigFill(60000, "T.X.Y", -1);
            WfCounters.TakeBigFills(out long bigN, out long bigPx, out string? bigCaller, out int bigArgb);
            Assert.Equal(1, bigN);
            Assert.Equal(60000, bigPx);
            Assert.Equal("T.X.Y", bigCaller);
            Assert.Equal(-1, bigArgb);
            WfCounters.TakeBigFills(out long bigN2, out long _, out string? _, out int _);
            Assert.Equal(0, bigN2);
            // Caller capture never throws; outside Telerik/app frames
            // it reports non-telerik.
            Assert.Equal("non-telerik", WfCounters.CaptureTelerikCaller());
            // Tier-2 line-height math matches the JS scratch canvas
            // (ceil(px * 1.25)): 16px -> 20, 12px -> 15, 9px -> 12.
            Assert.Equal(20, WfBrowserText.FullLineHeight(16));
            Assert.Equal(15, WfBrowserText.FullLineHeight(12));
            Assert.Equal(12, WfBrowserText.FullLineHeight(9));
        }
        finally
        {
            WfCounters.PerfEnabled = false;
            WfCounters.Take();
        }
    }
}
