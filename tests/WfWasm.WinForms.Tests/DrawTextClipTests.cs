// Regression: GDI DrawText bridge (WasmGdiText.DrawText, the rewriter target
// for vendor gdi32 DrawText P/Invokes) must clip to the formatting rect
// (desktop parity: clipped unless DT_NOCLIP). The FCTB tester paints labels
// past their cells into neighbor buttons/columns because the bridge never
// clips. Uses a 100px-wide raster for "Hi" so a missing clip spills visibly.
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

namespace WfWasm.WinForms.Tests;

public sealed class DrawTextClipTests
{
    private const int DT_NOCLIP = 0x100;

    // 100x12 fully-opaque block; the paint path tints it by the brush.
    private static WfBrowserText.Raster Solid100x12()
    {
        byte[] rgba = new byte[100 * 12 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(100, 12, rgba);
    }

    private static bool IsDark(WfColor c) => c.R < 80 && c.G < 80 && c.B < 80;

    [Fact]
    public void DrawText_Clips_To_Rect_Without_NoClip()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid100x12();
        try
        {
            using var bmp = new WfBitmap(200, 60);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            var hdc = (ForkHdc)(IntPtr)0x4510;
            var scope = ForkFill.TrackTarget(hdc, g);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            ForkGdiText.SelectObject((IntPtr)0x4510, font.ToHfont());
            scope.Dispose();
            int height = ForkGdiText.DrawText((IntPtr)0x4510, "Hi", -1, 10, 4, 50, 22, 0, out _, out _);
            Assert.True(height > 0);
            Assert.True(IsDark(bmp.GetPixel(12, 5)), "ink must land inside the rect");
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(60, 5).ToArgb());
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(109, 5).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void DrawText_NoClip_Allows_Spill()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid100x12();
        try
        {
            using var bmp = new WfBitmap(200, 60);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            var hdc = (ForkHdc)(IntPtr)0x4511;
            var scope = ForkFill.TrackTarget(hdc, g);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            ForkGdiText.SelectObject((IntPtr)0x4511, font.ToHfont());
            scope.Dispose();
            int height = ForkGdiText.DrawText((IntPtr)0x4511, "Hi", -1, 10, 4, 50, 22, DT_NOCLIP, out _, out _);
            Assert.True(height > 0);
            Assert.True(IsDark(bmp.GetPixel(60, 5)), "NoClip must let ink spill past the rect");
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
