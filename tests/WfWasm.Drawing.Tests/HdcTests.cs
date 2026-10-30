extern alias WfDraw;

using System;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGdiBitmap = WfDraw::System.Drawing.WasmGdiBitmap;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfImage = WfDraw::System.Drawing.Image;
using Xunit;

public sealed class HdcTests
{
    // Headless has no GDI device contexts, but Telerik builds its text
    // measurement Graphics from a screen DC (MeasurementGraphics..ctor).
    // FromHdcInternal answers the same dummy canvas Graphics as its
    // FromHdc/FromHwnd siblings instead of throwing: measurement works,
    // HDC rendering was fatal before either way.
    // Regression: NotSupportedException | FromHdcInternal during
    // RadButtonElement measure on the first Telerik demo form-create.
    [Fact]
    public void FromHdcInternal_Answers_Dummy_Graphics()
    {
        using var g = WfGraphics.FromHdcInternal(new IntPtr(0x1000));
        Assert.NotNull(g);
    }

    [Fact]
    public void FromHdc_Family_Answers_Dummy_Graphics()
    {
        using var a = WfGraphics.FromHdc(new IntPtr(0x1000));
        using var b = WfGraphics.FromHdc(new IntPtr(0x1000), IntPtr.Zero);
        using var c = WfGraphics.FromHwnd(new IntPtr(0x2000));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotNull(c);
    }

    [Fact]
    public void ReleaseHdc_Tolerates_Stub_Handles()
    {
        using var g = WfGraphics.FromHdcInternal(new IntPtr(0x1000));
        var ex = Record.Exception(() =>
        {
            g.ReleaseHdc();
            g.ReleaseHdc(new IntPtr(0x1000));
            g.ReleaseHdcInternal(new IntPtr(0x1000));
        });
        Assert.Null(ex);
    }

    // Grid dashed-border milestone: Syncfusion GridBorderPaint
    // CreateBitmapFromBorderSpecs P/Invokes gdi32!CreateBitmap for 8x8x1bpp
    // dash tiles and converts via Image.FromHbitmap. The tracked decode
    // must resolve to the exact pattern pixels.
    // Regression: NotSupportedException | FromHbitmap <- GridBorderPaint
    // <- OnOutlineCurrentCell during grid-grouping f_Load (first-chance,
    // current-cell outline never painted).
    [Fact]
    public void CreateTracked_Decodes_1bpp_MsbFirst()
    {
        // Row word 0x8000: only pixel (0,0) set (MSB-first); 0 clears.
        var bits = new short[] { unchecked((short)0x8000), 0, 0, 0, 0, 0, 0, 0 };
        nint h = WfGdiBitmap.CreateTracked(8, 8, 1, 1, bits);
        Assert.NotEqual(IntPtr.Zero, h);
        using var bmp = WfImage.FromHbitmap(h);
        Assert.Equal(8, bmp.Width);
        Assert.Equal(8, bmp.Height);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(0, 1).ToArgb());
    }

    [Fact]
    public void CreateTracked_AllOnes_Is_White_AllZeros_Is_Black()
    {
        nint white = WfGdiBitmap.CreateTracked(8, 8, 1, 1,
            new short[] { -1, -1, -1, -1, -1, -1, -1, -1 });
        nint black = WfGdiBitmap.CreateTracked(8, 8, 1, 1,
            new short[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.NotEqual(IntPtr.Zero, white);
        Assert.NotEqual(IntPtr.Zero, black);
        Assert.NotEqual(white, black);
        using var w = WfImage.FromHbitmap(white);
        using var b = WfImage.FromHbitmap(black);
        Assert.Equal(WfColor.White.ToArgb(), w.GetPixel(7, 7).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), b.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void CreateTracked_Decodes_24bpp_BGR()
    {
        // 2x1, 24bpp: row stride is WORD-aligned (6 bytes = 3 shorts);
        // the 4-short buffer still decodes: only the first 3 are read.
        // Pixel0 = red (B=0,G=0,R=255), pixel1 = blue (B=255,G=0,R=0).
        // Little-endian shorts: bytes 00 00 FF FF 00 00 00 00.
        var bits = new short[] { 0, -1, 0, 0 };
        nint h = WfGdiBitmap.CreateTracked(2, 1, 1, 24, bits);
        Assert.NotEqual(IntPtr.Zero, h);
        using var bmp = WfImage.FromHbitmap(h);
        Assert.Equal(2, bmp.Width);
        Assert.Equal(1, bmp.Height);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bmp.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void CreateTracked_Rejects_Bad_Shapes_With_Zero()
    {
        Assert.Equal(IntPtr.Zero, WfGdiBitmap.CreateTracked(0, 8, 1, 1, new short[4]));
        Assert.Equal(IntPtr.Zero, WfGdiBitmap.CreateTracked(8, 8, 2, 1, new short[4]));
        Assert.Equal(IntPtr.Zero, WfGdiBitmap.CreateTracked(8, 8, 1, 4, new short[16]));
        Assert.Equal(IntPtr.Zero, WfGdiBitmap.CreateTracked(8, 8, 1, 1, null));
        Assert.Equal(IntPtr.Zero, WfGdiBitmap.CreateTracked(8, 8, 1, 1, new short[3]));
        Assert.Equal(IntPtr.Zero, WfGdiBitmap.CreateTrackedPtr(8, 8, 1, 1, IntPtr.Zero));
    }

    [Fact]
    public void FromHbitmap_Foreign_Handle_Still_Throws_Honest_NSE()
    {
        // Untracked handles (the old 0x1000 sentinel) keep the honest
        // failure contract instead of silently answering wrong pixels.
        Assert.Throws<System.NotSupportedException>(
            () => WfImage.FromHbitmap(new IntPtr(0x1000)));
    }
}
