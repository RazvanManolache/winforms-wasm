extern alias WfDraw;

using System;
using System.Runtime.InteropServices;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfBrushes = WfDraw::System.Drawing.Brushes;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfPixelFormat = WfDraw::System.Drawing.Imaging.PixelFormat;
using Xunit;

public sealed class ScanPointerWritebackTests
{
    // The scan-pointer Bitmap ctor must behave like desktop GDI+: paints
    // issued through a Graphics over the wrapper reach the caller's buffer
    // (PDN's checkbox/thumbnail builders paint then read the lock buffer
    // after disposing the wrapper; a snapshot-only ctor yields blank glyphs).
    [Fact]
    public void Dispose_Writes_Painted_Pixels_Back_To_Scan_Pointer()
    {
        const int w = 13, h = 13, stride = w * 4;
        byte[] buffer = new byte[stride * h];
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var bmp = new WfBitmap(w, h, stride, WfPixelFormat.Format32bppPArgb,
                pin.AddrOfPinnedObject());
            using (bmp)
            using (var g = WfGraphics.FromImage(bmp))
            using (var brush = new WfDraw::System.Drawing.SolidBrush(
                WfDraw::System.Drawing.Color.FromArgb(255, 10, 20, 30)))
            {
                g.FillRectangle(brush, 2, 3, 4, 5);
            }
            // Painted rect rows 3..7, cols 2..5 must carry the fill color
            // (BGRA byte order) in the caller's buffer.
            for (int y = 3; y < 8; y++)
            {
                for (int x = 2; x < 6; x++)
                {
                    int o = y * stride + x * 4;
                    Assert.Equal(30, buffer[o]);
                    Assert.Equal(20, buffer[o + 1]);
                    Assert.Equal(10, buffer[o + 2]);
                    Assert.Equal(255, buffer[o + 3]);
                }
            }
            // Untouched pixels stay zero.
            Assert.Equal(0, buffer[0]);
            Assert.Equal(0, buffer[stride * (h - 1)]);
        }
        finally
        {
            pin.Free();
        }
    }

    // Copy-in still holds: the wrapper observes the buffer at construction.
    [Fact]
    public void Ctor_Copies_Buffer_Contents_In()
    {
        const int w = 2, h = 2, stride = w * 4;
        byte[] buffer = new byte[stride * h];
        buffer[0] = 7;
        GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            using var bmp = new WfBitmap(w, h, stride, WfPixelFormat.Format32bppPArgb,
                pin.AddrOfPinnedObject());
            Assert.Equal(7, bmp.GetPixel(0, 0).B);
        }
        finally
        {
            pin.Free();
        }
    }
}
