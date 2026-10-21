// Rev81 DrawText bridge: headless route proof for the app-private gdi32
// DrawText retarget. Runs against the rewritten fork DLL (needs a fresh
// rewrite-small after fork edits, same coupling as ExtTextOutWordPaintTests).
// A TrackTarget-registered Graphics stands in for the scoped paint target
// the rewriter's DeviceContextHdcScope registers during real control paint;
// the raster override stands in for browser font rasterization (same
// technique as ExtTextOutWordPaintTests). Proves the bridge resolves the
// target, reaches the word rasterizer with the vendor's text, and inks
// pixels; unregistered HDCs and empty text keep the quiet-zero stub shape.
extern alias Fork;
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfBrowserText = WasmDraw::System.Drawing.BrowserText;
using ForkHdc = Fork::Windows.Win32.Graphics.Gdi.HDC;
using ForkFill = Fork::Windows.Win32.PInvoke.WasmFillBridge;
using ForkGdiText = Fork::System.Windows.Forms.WasmGdiText;
using ForkTextBridge = Fork::Windows.Win32.PInvoke.WasmTextBridge;

namespace WfWasm.WinForms.Tests;

public sealed class WasmGdiTextDrawTextTests
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
    public void DrawText_ScopedTarget_Paints_Words_At_Oracle_Offsets()
    {
        var calls = new List<string>();
        WfBrowserText.RasterOverride = (text, _, _, _) => { calls.Add(text); return Tick2x8(); };
        try
        {
            using var bmp = new WfBitmap(120, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            var hdc = (ForkHdc)(IntPtr)0x4400;
            using var scope = ForkFill.TrackTarget(hdc, g);
            // Ink comes from the DC text-color state (default black);
            // the brush override only shapes the raster ticks.
            // Select MS Sans Serif 8.25 through the real HFONT path so
            // the oracle advances match ExtTextOutWordPaintTests.
            // Same origin (10, 4): ticks at The=19, sp=3, jamped=34 land
            // at 10, 32, 69.
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            ForkGdiText.SelectObject((IntPtr)0x4400, font.ToHfont());
            int height = ForkGdiText.DrawText((IntPtr)0x4400, "The jamped dog", -1, 10, 4, 110, 22, 0, out int pr, out int pb);
            Assert.True(height > 0);
            Assert.Equal(["The", "jamped", "dog"], calls);
            // Paint path reports the input rect unchanged (desktop parity).
            Assert.Equal(110, pr);
            Assert.Equal(22, pb);
            Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(10, 5).ToArgb());
            Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(32, 5).ToArgb());
            Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(69, 5).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(20, 5).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Theory]
    [InlineData(null, -1)]
    [InlineData("Hi", 0)]
    [InlineData("", -1)]
    public void DrawText_EmptyText_Returns_Zero_Without_Throw(string? text, int cch)
    {
        Assert.Equal(0, ForkGdiText.DrawText((IntPtr)0x4401, text, cch, 0, 0, 100, 20, 0, out _, out _));
    }

    [Fact]
    public void DrawText_UnregisteredHdc_Returns_Zero_Without_Throw()
    {
        Assert.Equal(0, ForkGdiText.DrawText((IntPtr)0x4402, "Hi", -1, 0, 0, 100, 20, 0, out _, out _));
        // Unregistered CALCRECT keeps stub-zero with the input rect echoed.
        Assert.Equal(0, ForkGdiText.DrawText((IntPtr)0x4402, "Hi", -1, 0, 0, 100, 20, 0x400, out int ur, out int ub));
        Assert.Equal(100, ur);
        Assert.Equal(20, ub);
    }

    [Fact]
    public void GdiPaintCensus_Survives_Migration()
    {
        // Migration-survival pin for the bounded paint census: the IL
        // rewriter has silently dropped new bridge code before (the
        // WasmGdiText CALCRECT fallback), and the tile dump reads this.
        string census = ForkTextBridge.DumpGdiPaintCensus();
        Assert.Contains("paints=", census);
    }

    [Theory]
    [InlineData(0x400)]
    [InlineData(0x400 | 0x20)]
    public void DrawText_Calcrect_Writes_Measured_Extents(int flags)
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => Tick2x8();
        try
        {
            using var bmp = new WfBitmap(200, 40);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            var hdc = (ForkHdc)(IntPtr)0x4403;
            using var scope = ForkFill.TrackTarget(hdc, g);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            ForkGdiText.SelectObject((IntPtr)0x4403, font.ToHfont());
            // Degenerate input rect (what the tree measures with): the
            // bridge must report real extents or labels squeeze to slivers.
            // Multiline measures against a realistic wrap width instead.
            int right = flags == 0x400 ? 200 : 11;
            int height = ForkGdiText.DrawText((IntPtr)0x4403, "North America", -1, 10, 4, right, 100, flags, out int cr, out int cb);
            Assert.True(height > 0);
            Assert.True(cr > 11);
            Assert.True(cb > 5);
            // CALCRECT never inks the target (the measure path may still
            // fetch a raster for its own scratch, same as MeasureExtent).
            for (int y = 0; y < 40; y++)
                for (int x = 0; x < 200; x++)
                    Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(x, y).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
