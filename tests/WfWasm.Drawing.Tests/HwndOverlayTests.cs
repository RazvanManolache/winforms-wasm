// Tests for the Graphics.FromHwnd window-overlay path (WfWasm web backing store).
// Telerik/WinForms controls paint adornments (spell squiggles, caret helpers,
// drag feedback) on control HWNDs via Graphics.FromHwnd; on web the overlay
// bitmap is committed back into the live control scene on Dispose so the
// CanvasHost blits it over the repainted control (see WasmOverlayBridge).
extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfPen = WfDraw::System.Drawing.Pen;

namespace WfWasm.Drawing.Tests;

public sealed class HwndOverlayTests
{
    [Fact]
    public void FromHwnd_Overlay_CommitOnDispose_DeliversPaintedPixels()
    {
        var hwnd = new IntPtr(0x0BEEF);
        using var controlBacking = new WfBitmap(60, 24);
        using (var seed = WfGraphics.FromImage(controlBacking))
            seed.Clear(WfColor.White);

        // Browser-side scene reports the control's current size + backing store.
        WfGraphics.HwndOverlaySizeHook = h => h == hwnd ? new Size(60, 24) : Size.Empty;
        WfBitmap? committed = null;
        WfGraphics.HwndOverlayCommitHook = (h, bmp) =>
        {
            Assert.Equal(hwnd, h);
            committed = bmp;
        };
        try
        {
            using (var overlay = WfGraphics.FromHwnd(hwnd))
            {
                Assert.NotNull(overlay);
                using var red = new WfPen(WfColor.Red, 1f);
                overlay!.DrawLine(red, 2, 2, 40, 2);
            }

            Assert.NotNull(committed);
            bool sawRed = false;
            for (int x = 2; x <= 40 && !sawRed; x++)
                sawRed |= committed!.GetPixel(x, 2).ToArgb() == WfColor.Red.ToArgb();
            Assert.True(sawRed, "committed overlay must carry the painted squiggle");
            // Control backing store stays untouched: overlays are composited, not baked.
            Assert.Equal(WfColor.White.ToArgb(), controlBacking.GetPixel(10, 2).ToArgb());
        }
        finally
        {
            WfGraphics.HwndOverlaySizeHook = null;
            WfGraphics.HwndOverlayCommitHook = null;
        }
    }

    [Fact]
    public void FromHwnd_UnknownHandle_StillReturnsWorkingGraphics()
    {
        var hwnd = new IntPtr(0xDEAD);
        WfGraphics.HwndOverlaySizeHook = _ => Size.Empty;
        try
        {
            using var g = WfGraphics.FromHwnd(hwnd);
            Assert.NotNull(g);
            // Must not throw even with no scene entry: paint into the fallback bitmap.
            g!.Clear(WfColor.Blue);
        }
        finally
        {
            WfGraphics.HwndOverlaySizeHook = null;
        }
    }

    [Fact]
    public void FromHwnd_Overlay_ReplacesExistingCommit()
    {
        var hwnd = new IntPtr(0xC0FFEE);
        WfGraphics.HwndOverlaySizeHook = _ => new Size(16, 16);
        var commits = new List<WfBitmap>();
        WfGraphics.HwndOverlayCommitHook = (_, bmp) => commits.Add(bmp);
        try
        {
            using (var first = WfGraphics.FromHwnd(hwnd))
                first!.Clear(WfColor.Green);
            using (var second = WfGraphics.FromHwnd(hwnd))
                second!.Clear(WfColor.Yellow);

            Assert.Equal(2, commits.Count);
            Assert.Equal(WfColor.Yellow.ToArgb(), commits[1].GetPixel(4, 4).ToArgb());
        }
        finally
        {
            WfGraphics.HwndOverlaySizeHook = null;
            WfGraphics.HwndOverlayCommitHook = null;
            foreach (var bmp in commits)
                bmp.Dispose();
        }
    }
}
