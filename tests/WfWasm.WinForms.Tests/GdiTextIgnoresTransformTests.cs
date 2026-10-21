// Regression: GDI text (TextRenderer.DrawText -> DrawTextEx -> WasmTextBridge)
// painted at double offset under a non-identity GDI+ world transform. Real
// GDI draws in device coords and ignores the GDI+ world transform; Telerik
// TPF relies on this (absolute element rects under absolute translates).
// Our bridge routed through Graphics.DrawString, which applies the
// transform, so every TPF word landed at rect+transform (RadTextBoxControl
// showed ~2x word spacing). The bridge now resets the world transform to
// identity around the paint (page unit/scale stay); GDI+ DrawString keeps
// applying it (second test guards the split).
extern alias Fork;
extern alias WasmDraw;

using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfBrush = WasmDraw::System.Drawing.Brush;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfRectangle = WasmDraw::System.Drawing.Rectangle;
using WfRectangleF = WasmDraw::System.Drawing.RectangleF;
using WfSolidBrush = WasmDraw::System.Drawing.SolidBrush;
using WfBrowserText = WasmDraw::System.Drawing.BrowserText;
using ForkForms = Fork::System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

public sealed class GdiTextIgnoresTransformTests
{
    // 30x12 fully-opaque block; the paint path tints it by the brush.
    private static WfBrowserText.Raster Solid30x12()
    {
        byte[] rgba = new byte[30 * 12 * 4];
        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = 255;
        return new WfBrowserText.Raster(30, 12, rgba);
    }

    private static bool IsDark(WfColor c) => c.R < 80 && c.G < 80 && c.B < 80;

    [Fact]
    public void DrawText_IgnoresWorldTransform_PaintsAtDeviceRect()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid30x12();
        try
        {
            using var bmp = new WfBitmap(200, 60);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            g.TranslateTransform(60, 20); // TPF-style absolute translate
            using var font = new WfFont("Segoe UI", 9f);
            ForkForms::TextRenderer.DrawText(g, "Hi", font, new WfRectangle(10, 5, 100, 20), WfColor.Black,
                ForkForms::TextFormatFlags.Left | ForkForms::TextFormatFlags.Top | ForkForms::TextFormatFlags.NoPadding);

            int dark = 0, minX = 9999, minY = 9999, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (IsDark(bmp.GetPixel(x, y))) { dark++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            Assert.True(IsDark(bmp.GetPixel(12, 7)), $"GDI text must paint at the device rect, ignoring the world transform (dark={dark} bbox=({minX},{minY})-({maxX},{maxY}))");
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(72, 27).ToArgb());
            float[] el = g.Transform.Elements;
            Assert.Equal(60f, el[4]);
            Assert.Equal(20f, el[5]);
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }

    [Fact]
    public void DrawString_StillAppliesWorldTransform()
    {
        WfBrowserText.RasterOverride = (_, _, _, _) => Solid30x12();
        try
        {
            using var bmp = new WfBitmap(200, 60);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            g.TranslateTransform(60, 20);
            using var font = new WfFont("Segoe UI", 9f);
            using WfBrush brush = new WfSolidBrush(WfColor.Black);
            g.DrawString("Hi", font, brush, new WfRectangleF(10, 5, 100, 20));

            Assert.True(IsDark(bmp.GetPixel(72, 27)), "GDI+ DrawString must keep applying the world transform");
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(12, 7).ToArgb());
        }
        finally
        {
            WfBrowserText.RasterOverride = null;
        }
    }
}
