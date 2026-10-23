using System.Runtime.InteropServices;
using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// Composite-path regression net for the PDN "new layer hides drawing"
// complaint: DrawBitmap/BlitRaster source-over onto an opaque BGRA
// target must preserve destination under transparent source, replace
// under opaque source, and blend partial alpha exactly. If these pass,
// the defect lives above the primitive (layer stack / host composite).
public sealed unsafe class CpuPaintCompositeTests
{
    private static (long Ctx, byte[] Pixels, GCHandle Pin) MakeBgraTarget(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        long ctx = CpuPaint.CreateTarget(pin.AddrOfPinnedObject(), w, h, w * 4, 4);
        return (ctx, pixels, pin);
    }

    private static (byte[] Target, GCHandle Pin) MakeMaskTarget(int w, int h)
    {
        var target = new byte[w * h];
        return (target, GCHandle.Alloc(target, GCHandleType.Pinned));
    }

    private static long MakeColorBitmap(uint[] pixels, int w, int h)
    {
        fixed (uint* p = pixels)
            return CpuPaint.CreateBitmap((IntPtr)p, w * 4, w, h);
    }

    private static void FillRed(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0;
            pixels[i + 1] = 0;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
    }

    private static uint PixelOf(byte[] pixels, int w, int x, int y)
        => BitConverter.ToUInt32(pixels, (y * w + x) * 4);

    [Fact]
    public void TransparentSource_Preserves_Opaque_Destination()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            FillRed(pixels);
            uint[] sprite = { 0x00FFFFFFu, 0x00FFFFFFu, 0x00FFFFFFu, 0x00FFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    1f, 1f, 2f, 2f, true,
                    0f, 0f, 2f, 2f, true,
                    1f, false);
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 1, 1));
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 2, 2));
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 0, 0));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void OpaqueSource_Replaces_Destination()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            FillRed(pixels);
            uint[] sprite = { 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    1f, 1f, 2f, 2f, true,
                    0f, 0f, 2f, 2f, true,
                    1f, false);
                Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 4, 1, 1));
                Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 4, 2, 2));
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 0, 0));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void HalfAlpha_Over_Opaque_Red_Blends_Midpoint()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            FillRed(pixels);
            uint[] sprite = { 0x80FFFFFFu, 0x80FFFFFFu, 0x80FFFFFFu, 0x80FFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    1f, 1f, 2f, 2f, true,
                    0f, 0f, 2f, 2f, true,
                    1f, false);
                uint got = PixelOf(pixels, 4, 1, 1);
                Assert.Equal(128u, got & 0xFFu);
                Assert.Equal(128u, (got >> 8) & 0xFFu);
                Assert.Equal(255u, (got >> 16) & 0xFFu);
                Assert.Equal(255u, got >> 24);
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void Zero_Opacity_Is_Noop()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            FillRed(pixels);
            uint[] sprite = { 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    0f, 0f, 2f, 2f, true,
                    0f, 0f, 2f, 2f, true,
                    0f, false);
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 0, 0));
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 1, 1));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void Zero_Handles_Are_Silent_Noop()
    {
        uint[] sprite = { 0xFFFFFFFFu };
        long bmp = MakeColorBitmap(sprite, 1, 1);
        try
        {
            CpuPaint.DrawBitmap(0, bmp, 0f, 0f, 1f, 1f, true, 0f, 0f, 1f, 1f, true, 1f, false);
            var (ctx, pixels, pin) = MakeBgraTarget(2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, 0, 0f, 0f, 1f, 1f, true, 0f, 0f, 1f, 1f, true, 1f, false);
                Assert.Equal(0u, PixelOf(pixels, 2, 0, 0));
            }
            finally
            {
                CpuPaint.FreeContext(ctx);
                pin.Free();
            }
        }
        finally
        {
            CpuPaint.FreeBitmap(bmp);
        }
    }

    [Fact]
    public void SourceRect_Selects_Single_Texel()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(8, 8);
        try
        {
            // Corners: red, green / blue, white.
            uint[] sprite = { 0xFF0000FFu, 0xFF00FF00u, 0xFFFF0000u, 0xFFFFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    5f, 5f, 1f, 1f, true,
                    1f, 1f, 1f, 1f, true,
                    1f, false);
                Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 8, 5, 5));
                Assert.Equal(0u, PixelOf(pixels, 8, 4, 5));
                Assert.Equal(0u, PixelOf(pixels, 8, 5, 4));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void Scaled_Uniform_Sprite_Fills_Dest()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            uint[] sprite = { 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    0f, 0f, 4f, 4f, true,
                    0f, 0f, 2f, 2f, true,
                    1f, false);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 4, x, y));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void Singular_Transform_Is_Noop()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            FillRed(pixels);
            uint[] sprite = { 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.SetTransform(ctx, 0f, 0f, 0f, 0f, 0f, 0f);
                CpuPaint.DrawBitmap(ctx, bmp,
                    0f, 0f, 0f, 0f, false,
                    0f, 0f, 0f, 0f, false,
                    1f, false);
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 0, 0));
                Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 3, 3));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void MaskTarget_HalfAlpha_Coverage()
    {
        var (target, pin) = MakeMaskTarget(4, 4);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 4, 4, 4);
            uint[] sprite = { 0x80FFFFFFu };
            long bmp = MakeColorBitmap(sprite, 1, 1);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    1f, 1f, 1f, 1f, true,
                    0f, 0f, 1f, 1f, true,
                    1f, false);
                Assert.Equal(128, target[1 * 4 + 1]);
                Assert.Equal(0, target[0]);
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void Bilinear_Uniform_Matches_Nearest()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            uint[] sprite = { 0xFF00FF00u, 0xFF00FF00u, 0xFF00FF00u, 0xFF00FF00u };
            long bmp = MakeColorBitmap(sprite, 2, 2);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    0f, 0f, 3f, 3f, true,
                    0f, 0f, 2f, 2f, true,
                    1f, true);
                // NOTE: DrawBitmap's high bound is ceil-inclusive, so a
                // 3-wide dest paints x=0..3 (4 px); BlitRaster instead
                // ends at dx0+ceil(w*sx)-1 (3 px). Pinning actual
                // behavior here; the 1px extent difference is filed
                // separately for the coordinator to rule on.
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        Assert.Equal(0xFF00FF00u, PixelOf(pixels, 4, x, y));
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void BlitRaster_FullyOutsideClip_Paints_Nothing()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            CpuPaint.SetTransform(ctx, 1f, 0f, 0f, 1f, 100f, 100f);
            byte[] rgba = new byte[2 * 2 * 4];
            rgba[3] = 255;
            CpuPaint.BlitRaster(ctx, rgba, 2, 2, 0f, 0f, 1f, 1f, 1f, 1f, 1f);
            for (int i = 0; i < pixels.Length; i++)
                Assert.Equal(0, pixels[i]);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void BlitRaster_OneByOne_Paints_Single_Pixel()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            byte[] rgba = { 255, 255, 255, 255 };
            CpuPaint.BlitRaster(ctx, rgba, 1, 1, 3f, 2f, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 4, 3, 2));
            Assert.Equal(0u, PixelOf(pixels, 4, 2, 2));
            Assert.Equal(0u, PixelOf(pixels, 4, 3, 1));
            Assert.Equal(0u, PixelOf(pixels, 4, 0, 0));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void BlitRaster_NegativeDest_Clips_To_Texel11()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            byte[] rgba = new byte[2 * 2 * 4];
            for (int i = 3; i < rgba.Length; i += 4)
                rgba[i] = 255;
            CpuPaint.BlitRaster(ctx, rgba, 2, 2, -1f, -1f, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 4, 0, 0));
            Assert.Equal(0u, PixelOf(pixels, 4, 1, 0));
            Assert.Equal(0u, PixelOf(pixels, 4, 0, 1));
            Assert.Equal(0u, PixelOf(pixels, 4, 3, 3));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void BlitRaster_PartialAlpha_Over_Red_Blends_Midpoint()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            FillRed(pixels);
            byte[] rgba = new byte[2 * 2 * 4];
            for (int i = 3; i < rgba.Length; i += 4)
                rgba[i] = 128;
            CpuPaint.BlitRaster(ctx, rgba, 2, 2, 1f, 1f, 1f, 1f, 1f, 1f, 1f);
            uint got = PixelOf(pixels, 4, 1, 1);
            Assert.Equal(128u, got & 0xFFu);
            Assert.Equal(128u, (got >> 8) & 0xFFu);
            Assert.Equal(255u, (got >> 16) & 0xFFu);
            Assert.Equal(255u, got >> 24);
            Assert.Equal(0xFFFF0000u, PixelOf(pixels, 4, 0, 0));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void BlitRaster_InkColor_Tints_White_Alpha()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            byte[] rgba = { 0, 0, 0, 255 };
            CpuPaint.BlitRaster(ctx, rgba, 1, 1, 0f, 0f, 1f, 1f, 0f, 0f, 1f);
            uint got = PixelOf(pixels, 4, 0, 0);
            Assert.Equal(255u, (got >> 16) & 0xFFu);
            Assert.Equal(0u, (got >> 8) & 0xFFu);
            Assert.Equal(0u, got & 0xFFu);
            Assert.Equal(255u, got >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }
}
