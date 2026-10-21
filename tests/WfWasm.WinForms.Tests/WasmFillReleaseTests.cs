// Draw-after-release parity: desktop tolerates GDI calls on an HDC after
// ReleaseHdc (Syncfusion MenuTextCellRenderer releases the grid Graphics
// HDC, then paints the item text through the same HDC). The fill bridge
// must keep resolving a released token to its surface (bounded side
// table); a never-registered HDC still resolves to nothing (quiet zero,
// covered by DrawText_UnregisteredHdc_Returns_Zero_Without_Throw - kept
// intact here). Same raster-override technique as
// WasmGdiTextDrawTextTests/ExtTextOutWordPaintTests.
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

public sealed class WasmFillReleaseTests
{
    private static WfBrowserText.Raster Tick2x8()
    {
        byte[] rgba = new byte[2 * 8 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(2, 8, rgba);
    }

    [Fact]
    public void ReleasedHdc_TryGetTarget_StillResolves()
    {
        using var bmp = new WfBitmap(16, 16);
        using var g = WfGraphics.FromImage(bmp);
        var hdc = (ForkHdc)(IntPtr)0x4500;
        var scope = ForkFill.TrackTarget(hdc, g);
        Assert.True(ForkFill.TryGetTarget(hdc, out var live));
        Assert.Same(g, live);
        // Vendor ReleaseHdc moment: the scope drops, the surface stays.
        scope.Dispose();
        Assert.True(ForkFill.TryGetTarget(hdc, out var released));
        Assert.Same(g, released);
    }

    [Fact]
    public void ReleasedHdc_DrawText_StillInks()
    {
        WfBrowserText.RasterOverride = (text, _, _, _) => Tick2x8();
        try
        {
            using var bmp = new WfBitmap(120, 24);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            var hdc = (ForkHdc)(IntPtr)0x4501;
            var scope = ForkFill.TrackTarget(hdc, g);
            using var font = new WfFont("Microsoft Sans Serif", 8.25f);
            ForkGdiText.SelectObject((IntPtr)0x4501, font.ToHfont());
            scope.Dispose();
            int height = ForkGdiText.DrawText((IntPtr)0x4501, "Hi", -1, 10, 4, 110, 22, 0, out _, out _);
            Assert.True(height > 0);
            Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(10, 5).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void Reregister_AfterRelease_Resolves_New_Target()
    {
        using var bmp1 = new WfBitmap(8, 8);
        using var g1 = WfGraphics.FromImage(bmp1);
        using var bmp2 = new WfBitmap(8, 8);
        using var g2 = WfGraphics.FromImage(bmp2);
        var hdc = (ForkHdc)(IntPtr)0x4502;
        ForkFill.TrackTarget(hdc, g1).Dispose();
        Assert.True(ForkFill.TryGetTarget(hdc, out var first));
        Assert.Same(g1, first);
        using var scope2 = ForkFill.TrackTarget(hdc, g2);
        Assert.True(ForkFill.TryGetTarget(hdc, out var second));
        Assert.Same(g2, second);
    }

    [Fact]
    public void ReleasedTable_Evicts_Oldest_Beyond_Cap()
    {
        var bmps = new List<WfBitmap>();
        var gs = new List<WfGraphics>();
        try
        {
            const int total = 40;
            for (int i = 0; i < total; i++)
            {
                var bmp = new WfBitmap(4, 4);
                bmps.Add(bmp);
                var g = WfGraphics.FromImage(bmp);
                gs.Add(g);
                ForkFill.TrackTarget((ForkHdc)(IntPtr)(0x4600 + i), g).Dispose();
            }
            // Cap is 32 and the suite runs serially: right after our own
            // 40 releases the table holds exactly our last 32, no matter
            // what other test classes released before us.
            int hits = 0;
            for (int i = 0; i < total; i++)
                if (ForkFill.TryGetTarget((ForkHdc)(IntPtr)(0x4600 + i), out _))
                    hits++;
            Assert.Equal(32, hits);
            Assert.False(ForkFill.TryGetTarget((ForkHdc)(IntPtr)0x4600, out _));
            Assert.True(ForkFill.TryGetTarget((ForkHdc)(IntPtr)(0x4600 + total - 1), out _));
        }
        finally
        {
            foreach (var g in gs) g.Dispose();
            foreach (var b in bmps) b.Dispose();
        }
    }
}
