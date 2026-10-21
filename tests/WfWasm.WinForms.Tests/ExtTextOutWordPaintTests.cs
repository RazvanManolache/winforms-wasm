// Word-chunk GDI paint: each tabled word rasterizes at its accumulated
// oracle x so ink lands on desktop advances (eval6: MS end +8, RTC end -12
// with whole-line browser shaping). Runs against the rewritten fork DLL,
// so it exercises the shipped paint wiring (needs a fresh rewrite-small
// after fork edits).
extern alias Fork;
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfBrush = WasmDraw::System.Drawing.Brush;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfPointF = WasmDraw::System.Drawing.PointF;
using WfRectangleF = WasmDraw::System.Drawing.RectangleF;
using WfSolidBrush = WasmDraw::System.Drawing.SolidBrush;
using WfStringFormat = WasmDraw::System.Drawing.StringFormat;
using WfBrowserText = WasmDraw::System.Drawing.BrowserText;
using ForkBridge = Fork::Windows.Win32.PInvoke.WasmTextBridge;

namespace WfWasm.WinForms.Tests;

public sealed class ExtTextOutWordPaintTests
{
    // 2x8 fully-opaque tick; DrawString tints it by the brush, so each
    // word paints a 2px tick exactly at its origin.
    private static WfBrowserText.Raster Tick2x8()
    {
        byte[] rgba = new byte[2 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(2, 8, rgba);
    }

    [Fact]
    public void TryPaintWords_Chunks_At_Oracle_Offsets()
    {
        var calls = new List<string>();
        WfBrowserText.RasterOverride = (text, _, _, _) => { calls.Add(text); return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(120, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using WfBrush brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            using var sf = new WfStringFormat();
            Assert.True(ForkBridge.TryPaintWords(g, "The jamped dog", font, brush, 10f, 4f, sf));
            Assert.Equal(["The", "jamped", "dog"], calls);
            // Oracle offsets (MSS 8.25: The=19, sp=3, jamped=34): ticks at
            // 10, 32, 69; gaps stay white.
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(10, 5).ToArgb());
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(32, 5).ToArgb());
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(69, 5).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(20, 5).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(40, 5).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Theory]
    [InlineData("caf\u00e9 x")] // untabled char
    [InlineData("dog")]         // single word: same raster either way
    [InlineData("a\tb x")]      // tab stops need EM context
    public void TryPaintWords_Fallback_Declines(string line)
    {
        var calls = new List<string>();
        WfBrowserText.RasterOverride = (text, _, _, _) => { calls.Add(text); return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(120, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using WfBrush brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            using var sf = new WfStringFormat();
            Assert.False(ForkBridge.TryPaintWords(g, line, font, brush, 10f, 4f, sf));
            Assert.Empty(calls);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void TryPaintWordsRect_Clips_To_Layout()
    {
        var calls = new List<string>();
        WfBrowserText.RasterOverride = (text, _, _, _) => { calls.Add(text); return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(120, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            using WfBrush brush = new WfSolidBrush(WfColor.Red);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            using var sf = new WfStringFormat();
            var layout = new WfRectangleF(10, 4, 30, 20);
            Assert.True(ForkBridge.TryPaintWordsRect(g, "The jamped dog", font, brush, layout, sf));
            Assert.Equal(["The", "jamped"], calls);
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(10, 5).ToArgb());
            Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(32, 5).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(69, 5).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
