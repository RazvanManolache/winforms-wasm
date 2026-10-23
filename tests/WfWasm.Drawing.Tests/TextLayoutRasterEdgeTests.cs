extern alias WfDraw;

using System;
using WfBrowserText = WfDraw::System.Drawing.BrowserText;
using WfLayoutRaster = WfDraw::System.Drawing.LayoutRaster;
using WfTextLayoutRaster = WfDraw::System.Drawing.TextLayoutRaster;
using Xunit;

// Edge contract for TextLayoutRaster.TryRasterLayout: expected nulls
// (bad dimensions, unreadable/unrasterizable input) never throw, never
// call the browser override, and never set LastFault even with
// diagnostics on. Only UNEXPECTED engine faults record LastFault, and
// only when the WfWasm.TextDiagnostics switch is on.
public sealed class TextLayoutRasterEdgeTests
{
    private class FakeLayout
    {
        public string Text { get; set; } = "Layers";
        public string FontFamilyName { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 12;
        public int FontWeight { get; set; } = 700;
        public string FontStyle { get; set; } = "Normal";
        public double MaxWidth { get; set; } = double.PositiveInfinity;
    }

    private sealed class MinimalLayout
    {
        public string Text { get; set; } = "Hi";
        public string FontFamilyName { get; set; } = "Arial";
        public double FontSize { get; set; } = 10;
    }

    private sealed class ThrowingSizeLayout : FakeLayout
    {
        public new double FontSize => throw new InvalidOperationException("no size");
    }

    private sealed class ThrowingWeightLayout : FakeLayout
    {
        public new int FontWeight => throw new InvalidOperationException("no weight");
    }

    [Fact]
    public void NonPositive_Dimensions_Return_Null_Without_Calling()
    {
        bool called = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            called = true;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new FakeLayout(), 0f, 2048));
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new FakeLayout(), -96f, 2048));
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new FakeLayout(), 96f, 0));
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new FakeLayout(), 96f, -5));
            Assert.False(called);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Null_Layout_With_Override_Returns_Null_Without_Calling()
    {
        bool called = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            called = true;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(null, 96f, 2048));
            Assert.False(called);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Zero_And_Negative_Size_Return_Null_Without_Calling()
    {
        bool called = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            called = true;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            var zero = new FakeLayout { FontSize = 0 };
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(zero, 96f, 2048));
            var neg = new FakeLayout { FontSize = -12 };
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(neg, 96f, 2048));
            var nan = new FakeLayout { FontSize = double.NaN };
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(nan, 96f, 2048));
            var inf = new FakeLayout { FontSize = double.PositiveInfinity };
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(inf, 96f, 2048));
            Assert.False(called);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Whitespace_Family_Falls_Back_To_SansSerif()
    {
        string? seenFamily = null;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenFamily = font.Name;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            var layout = new FakeLayout { FontFamilyName = "   " };
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(layout, 96f, 2048));
            Assert.Equal("sans-serif", seenFamily);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Weight_Boundary_699_Regular_700_Bold()
    {
        bool? seenBold = null;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenBold = font.Bold;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(new FakeLayout { FontWeight = 699 }, 96f, 2048));
            Assert.False(seenBold);
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(new FakeLayout { FontWeight = 700 }, 96f, 2048));
            Assert.True(seenBold);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Oblique_Maps_To_Italic_Like_Italic()
    {
        bool seenItalic = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenItalic = font.Italic;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            var layout = new FakeLayout { FontStyle = "Oblique", FontWeight = 400 };
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(layout, 96f, 2048));
            Assert.True(seenItalic);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Bad_MaxWidth_Is_Ignored_Request_Keeps_Caller_Value()
    {
        int seenMaxW = 0;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenMaxW = maxW;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(new FakeLayout { MaxWidth = double.NaN }, 96f, 2048));
            Assert.Equal(2048, seenMaxW);
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(new FakeLayout { MaxWidth = 0 }, 96f, 2048));
            Assert.Equal(2048, seenMaxW);
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(new FakeLayout { MaxWidth = -50 }, 96f, 2048));
            Assert.Equal(2048, seenMaxW);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Throwing_Size_Is_Expected_Null_No_Fault_Even_When_Diagnostics_On()
    {
        AppContext.SetSwitch("WfWasm.TextDiagnostics", true);
        bool called = false;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            called = true;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            Assert.Null(WfTextLayoutRaster.TryRasterLayout(new ThrowingSizeLayout(), 96f, 2048));
            Assert.False(called);
            Assert.Null(WfTextLayoutRaster.LastFault);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
            AppContext.SetSwitch("WfWasm.TextDiagnostics", false);
        }
    }

    [Fact]
    public void Throwing_Weight_Degrades_To_Regular_No_Fault()
    {
        AppContext.SetSwitch("WfWasm.TextDiagnostics", true);
        bool? seenBold = null;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenBold = font.Bold;
            return new WfBrowserText.Raster(1, 1, new byte[4]);
        };
        try
        {
            Assert.NotNull(WfTextLayoutRaster.TryRasterLayout(new ThrowingWeightLayout(), 96f, 2048));
            Assert.False(seenBold);
            Assert.Null(WfTextLayoutRaster.LastFault);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
            AppContext.SetSwitch("WfWasm.TextDiagnostics", false);
        }
    }

    [Fact]
    public void Minimal_Shape_Without_Optional_Getters_Still_Rasterizes()
    {
        string? seenText = null;
        bool? seenBold = null;
        bool? seenItalic = null;
        int seenMaxW = 0;
        WfBrowserText.RasterOverride = (text, font, dpi, maxW) =>
        {
            seenText = text;
            seenBold = font.Bold;
            seenItalic = font.Italic;
            seenMaxW = maxW;
            return new WfBrowserText.Raster(3, 2, new byte[3 * 2 * 4]);
        };
        try
        {
            WfLayoutRaster? hit = WfTextLayoutRaster.TryRasterLayout(new MinimalLayout(), 96f, 512);
            Assert.NotNull(hit);
            Assert.Equal(3, hit.Width);
            Assert.Equal("Hi", seenText);
            Assert.False(seenBold);
            Assert.False(seenItalic);
            Assert.Equal(512, seenMaxW);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
