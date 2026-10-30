extern alias WfDraw;

using System;
using System.Runtime.CompilerServices;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGdiBitmap = WfDraw::System.Drawing.WasmGdiBitmap;
using WfHandleMap = WfDraw::System.Drawing.BitmapHandleMap;
using WfImage = WfDraw::System.Drawing.Image;
using Xunit;

public sealed class GdiBitmapHandleTests
{
    // GDI packs 24bpp scanlines WORD-aligned: 2px = 6 bytes = 3 shorts/row.
    // The DWORD-aligned stride wrongly rejected this valid buffer.
    // Regression: BitmapProbe "Valid WORD-aligned 2x2 24bpp accepted: False".
    [Fact]
    public void CreateTracked_Accepts_WordAligned_24bpp_With_Exact_Pixels()
    {
        // Row0: red, blue. Row1: green, white.
        var bits = new short[] { 0, -1, 0, -256, -256, -1 };
        nint h = WfGdiBitmap.CreateTracked(2, 2, 1, 24, bits);
        Assert.NotEqual(IntPtr.Zero, h);
        try
        {
            using WfBitmap bmp = WfImage.FromHbitmap(h);
            Assert.Equal(2, bmp.Width);
            Assert.Equal(2, bmp.Height);
            Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bmp.GetPixel(0, 0).ToArgb());
            Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bmp.GetPixel(1, 0).ToArgb());
            Assert.Equal(WfColor.FromArgb(0, 255, 0).ToArgb(), bmp.GetPixel(0, 1).ToArgb());
            Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bmp.GetPixel(1, 1).ToArgb());
        }
        finally
        {
            WfHandleMap.Release(h);
        }
    }

    // Odd width exercises the WORD stride on every row: the pointer path
    // must decode byte-identical pixels to the array path.
    [Fact]
    public void CreateTrackedPtr_Matches_Array_Path_MultiRow()
    {
        // 3x2 24bpp, 5 shorts/row. Row0: red/green/blue. Row1: white/black/red.
        var bits = new short[] { 0, 255, 255, 255, 0, -1, 255, 0, 0, 255 };
        nint arrayHandle = WfGdiBitmap.CreateTracked(3, 2, 1, 24, bits);
        Assert.NotEqual(IntPtr.Zero, arrayHandle);
        nint native = System.Runtime.InteropServices.Marshal.AllocHGlobal(bits.Length * 2);
        nint ptrHandle = IntPtr.Zero;
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(bits, 0, native, bits.Length);
            ptrHandle = WfGdiBitmap.CreateTrackedPtr(3, 2, 1, 24, native);
            Assert.NotEqual(IntPtr.Zero, ptrHandle);
            using WfBitmap a = WfImage.FromHbitmap(arrayHandle);
            using WfBitmap b = WfImage.FromHbitmap(ptrHandle);
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.Height, b.Height);
            int[] expected =
            {
                WfColor.FromArgb(255, 0, 0).ToArgb(), WfColor.FromArgb(0, 255, 0).ToArgb(), WfColor.FromArgb(0, 0, 255).ToArgb(),
                WfColor.FromArgb(255, 255, 255).ToArgb(), WfColor.FromArgb(0, 0, 0).ToArgb(), WfColor.FromArgb(255, 0, 0).ToArgb(),
            };
            int i = 0;
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++, i++)
                {
                    Assert.Equal(expected[i], a.GetPixel(x, y).ToArgb());
                    Assert.Equal(expected[i], b.GetPixel(x, y).ToArgb());
                }
        }
        finally
        {
            WfHandleMap.Release(arrayHandle);
            if (ptrHandle != IntPtr.Zero)
                WfHandleMap.Release(ptrHandle);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(native);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint CreateOrphanedTile()
    {
        // No outlive: the decoded Bitmap is referenced only by the handle.
        return WfGdiBitmap.CreateTracked(8, 1, 1, 1, new short[] { -1 });
    }

    // Created handles own their Bitmap until release: a live HBITMAP must
    // resolve after a full collection. Regression: BitmapProbe "Same live
    // HBITMAP resolves after collection: False".
    [Fact]
    public void Created_Handle_Survives_Garbage_Collection()
    {
        nint h = CreateOrphanedTile();
        Assert.NotEqual(IntPtr.Zero, h);
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using WfBitmap bmp = WfImage.FromHbitmap(h);
            Assert.Equal(8, bmp.Width);
            Assert.Equal(1, bmp.Height);
        }
        finally
        {
            WfHandleMap.Release(h);
        }
    }

    // Release ends ownership: the GDI DeleteObject pairing fails honestly.
    [Fact]
    public void Released_Handle_Resolves_False_With_Honest_NSE()
    {
        nint h = WfGdiBitmap.CreateTracked(8, 8, 1, 1,
            new short[] { -1, -1, -1, -1, -1, -1, -1, -1 });
        Assert.NotEqual(IntPtr.Zero, h);
        using (WfImage.FromHbitmap(h))
        {
        }
        WfHandleMap.Release(h);
        Assert.Throws<System.NotSupportedException>(() => WfImage.FromHbitmap(h));
    }

    // Churn must not corrupt the registry: every released handle stays dead
    // while a freshly minted one resolves.
    [Fact]
    public void Repeated_Alloc_Release_Keeps_Registry_Coherent()
    {
        var dead = new nint[64];
        for (int i = 0; i < dead.Length; i++)
        {
            dead[i] = WfGdiBitmap.CreateTracked(8, 8, 1, 1,
                new short[] { -1, -1, -1, -1, -1, -1, -1, -1 });
            Assert.NotEqual(IntPtr.Zero, dead[i]);
            WfHandleMap.Release(dead[i]);
        }
        foreach (nint h in dead)
            Assert.Throws<System.NotSupportedException>(() => WfImage.FromHbitmap(h));
        nint live = WfGdiBitmap.CreateTracked(8, 8, 1, 1,
            new short[] { -1, -1, -1, -1, -1, -1, -1, -1 });
        try
        {
            using WfBitmap bmp = WfImage.FromHbitmap(live);
            Assert.Equal(8, bmp.Width);
        }
        finally
        {
            WfHandleMap.Release(live);
        }
    }

    // FromHbitmap answers independent copies: mutating one never touches
    // another copy or a later resolution.
    [Fact]
    public void FromHbitmap_Copies_Are_Independent()
    {
        var bits = new short[] { 0, -1, 0, -256, -256, -1 };
        nint h = WfGdiBitmap.CreateTracked(2, 2, 1, 24, bits);
        Assert.NotEqual(IntPtr.Zero, h);
        try
        {
            using WfBitmap a = WfImage.FromHbitmap(h);
            using WfBitmap b = WfImage.FromHbitmap(h);
            a.SetPixel(0, 0, WfColor.FromArgb(0, 0, 0));
            Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), b.GetPixel(0, 0).ToArgb());
            using WfBitmap c = WfImage.FromHbitmap(h);
            Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), c.GetPixel(0, 0).ToArgb());
        }
        finally
        {
            WfHandleMap.Release(h);
        }
    }
}
