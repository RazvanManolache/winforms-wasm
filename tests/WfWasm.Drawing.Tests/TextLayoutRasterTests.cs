extern alias WfDraw;

using System;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfFontStyle = WfDraw::System.Drawing.FontStyle;
using WfLayoutRaster = WfDraw::System.Drawing.LayoutRaster;
using WfTextLayoutRaster = WfDraw::System.Drawing.TextLayoutRaster;
using Xunit;

public sealed class TextLayoutRasterTests
{
    // Shape twin of PaintDotNet.UI.Media.TextLayout/TextFormat: the
    // helper reads every member by name, so any object with these
    // getters exercises the same path without binding PDN.
    private sealed class FakeLayout
    {
        public string Text { get; set; } = string.Empty;
        public string FontFamilyName { get; set; } = string.Empty;
        public double FontSize { get; set; }
        public int FontWeight { get; set; }
        public string FontStyle { get; set; } = "Normal";
        public double MaxWidth { get; set; }
    }

    private static FakeLayout HeadersLayout() => new()
    {
        Text = "Layers",
        FontFamilyName = "Segoe UI",
        FontSize = 12,
        FontWeight = 700,
        FontStyle = "Normal",
        MaxWidth = double.PositiveInfinity,
    };

    [Fact]
    public void Rasterizes_Through_Override_With_Layout_Font()
    {
        string? seenText = null;
        string? seenFamily = null;
        float seenPts = 0;
        bool seenBold = false;
        bool seenItalic = false;
        float seenDpi = 0;
        int seenMaxW = 0;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenText = text;
            seenFamily = font.Name;
            seenPts = font.SizeInPoints;
            seenBold = font.Bold;
            seenItalic = font.Italic;
            seenDpi = dpi;
            seenMaxW = maxW;
            var rgba = new byte[10 * 5 * 4];
            rgba[3] = 255;
            return new WfBrowserText.Raster(10, 5, rgba);
        };
        try
        {
            WfLayoutRaster? hit = WfTextLayoutRaster.TryRasterLayout(HeadersLayout(), 96f, 2048);
            Assert.NotNull(hit);
            Assert.Equal(10, hit.Width);
            Assert.Equal(5, hit.Height);
            Assert.Equal(255, hit.Rgba[3]);
            Assert.Equal("Layers", seenText);
            Assert.Equal("Segoe UI", seenFamily);
            // DIPs (1/96") convert to points (1/72").
            Assert.Equal(9f, seenPts);
            Assert.True(seenBold);
            Assert.False(seenItalic);
            Assert.Equal(96f, seenDpi);
            Assert.Equal(2048, seenMaxW);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Italic_Style_Name_Maps_To_Font_Italic()
    {
        bool seenItalic = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenItalic = font.Italic;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            var layout = HeadersLayout();
            layout.FontStyle = "Italic";
            layout.FontWeight = 400;
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(layout, 96f, 2048));
            Assert.True(seenItalic);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Layout_MaxWidth_Clamps_Request()
    {
        int seenMaxW = 0;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            var layout = HeadersLayout();
            layout.MaxWidth = 100;
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(layout, 96f, 2048));
            Assert.Equal(100, seenMaxW);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Null_When_No_Override_Headless()
    {
        // No browser canvas off-WASM and no test seam: the caller
        // keeps its embedded 5x7 fallback instead of painting nothing.
        WfBrowserText.RasterOverride = null;
        Assert.False(WfBrowserText.IsAvailable);
        Assert.Null(WfTextLayoutRaster.TryRasterLayout(HeadersLayout(), 96f, 2048));
    }

    [Fact]
    public void Null_When_Text_Missing_Or_Shape_Unknown()
    {
        bool called = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            called = true;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            var empty = HeadersLayout();
            empty.Text = string.Empty;
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(empty, 96f, 2048));
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(null, 96f, 2048));
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new object(), 96f, 2048));
            Assert.False(called);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    // get_Text itself throws: GetString does not catch, so the fault
    // reaches the outer handler (Invoke wraps it in
    // TargetInvocationException). BrowserText never runs.
    private sealed class ThrowingTextLayout
    {
        public string Text => throw new InvalidOperationException("unreadable text");
        public string FontFamilyName { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 12;
        public int FontWeight { get; set; }
        public string FontStyle { get; set; } = "Normal";
        public double MaxWidth { get; set; } = double.PositiveInfinity;
    }

    [Fact]
    public void Unexpected_Raster_Fault_Is_Diagnosable_When_Switch_On()
    {
        AppContext.SetSwitch("WfWasm.TextDiagnostics", true);
        try
        {
            // Fallback contract holds: still null, caller keeps 5x7.
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new ThrowingTextLayout(), 96f, 2048));
            string? fault = WfTextLayoutRaster.LastFault;
            Assert.NotNull(fault);
            Assert.Contains("TargetInvocationException", fault);
        }
        finally
        {
            AppContext.SetSwitch("WfWasm.TextDiagnostics", false);
        }
    }

    [Fact]
    public void Unexpected_Raster_Fault_Stays_Silent_When_Switch_Off()
    {
        AppContext.SetSwitch("WfWasm.TextDiagnostics", false);
        try
        {
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new ThrowingTextLayout(), 96f, 2048));
            Assert.Null(WfTextLayoutRaster.LastFault);
        }
        finally
        {
            AppContext.SetSwitch("WfWasm.TextDiagnostics", false);
        }
    }

    [Fact]
    public void Expected_Null_Leaves_No_Fault_Even_When_Switch_On()
    {
        AppContext.SetSwitch("WfWasm.TextDiagnostics", true);
        try
        {
            var empty = HeadersLayout();
            empty.Text = string.Empty;
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(empty, 96f, 2048));
            Assert.Null(WfTextLayoutRaster.LastFault);
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(null, 96f, 2048));
            Assert.Null(WfTextLayoutRaster.LastFault);
        }
        finally
        {
            AppContext.SetSwitch("WfWasm.TextDiagnostics", false);
        }
    }
}
