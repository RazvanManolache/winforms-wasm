using System.Runtime.InteropServices;
using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

public sealed unsafe class CpuPaintTests
{
    private static (byte[] Target, GCHandle Pin) MakeTarget(int w, int h)
    {
        var target = new byte[w * h];
        return (target, GCHandle.Alloc(target, GCHandleType.Pinned));
    }

    private static long MakeWhiteBitmap(int w, int h, byte alpha)
    {
        var pixels = new uint[w * h];
        uint px = (uint)(0x00FFFFFF | (alpha << 24));
        Array.Fill(pixels, px);
        fixed (uint* p = pixels)
            return CpuPaint.CreateBitmap((IntPtr)p, w * 4, w, h);
    }

    [Fact]
    public void DrawBitmap_Fills_Mask_With_SourceOver_Blend()
    {
        var (target, pin) = MakeTarget(8, 8);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 8, 8, 8);
            long bmp = MakeWhiteBitmap(2, 2, 255);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    2f, 2f, 2f, 2f, true,
                    0f, 0f, 2f, 2f, true,
                    1f, false);
                Assert.Equal(255, target[2 * 8 + 2]);
                Assert.Equal(255, target[3 * 8 + 3]);
                Assert.Equal(0, target[0]);
                Assert.Equal(0, target[7 * 8 + 7]);
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
    public void DrawBitmap_Translation_Moves_Sprite()
    {
        var (target, pin) = MakeTarget(8, 8);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 8, 8, 8);
            long bmp = MakeWhiteBitmap(2, 2, 255);
            try
            {
                CpuPaint.SetTransform(ctx, 1f, 0f, 0f, 1f, 3f, 4f);
                CpuPaint.DrawBitmap(ctx, bmp,
                    0f, 0f, 0f, 0f, false,
                    0f, 0f, 0f, 0f, false,
                    1f, false);
                Assert.Equal(255, target[4 * 8 + 3]);
                Assert.Equal(255, target[5 * 8 + 4]);
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
    public void DrawBitmap_Opacity_Scales_Coverage()
    {
        var (target, pin) = MakeTarget(4, 4);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 4, 4, 4);
            long bmp = MakeWhiteBitmap(1, 1, 255);
            try
            {
                CpuPaint.DrawBitmap(ctx, bmp,
                    1f, 1f, 1f, 1f, true,
                    0f, 0f, 1f, 1f, true,
                    0.5f, false);
                Assert.InRange(target[1 * 4 + 1], 120, 135);
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
    public void DrawBitmap_Clips_At_Target_Bounds()
    {
        var (target, pin) = MakeTarget(4, 4);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 4, 4, 4);
            long bmp = MakeWhiteBitmap(4, 4, 255);
            try
            {
                CpuPaint.SetTransform(ctx, 1f, 0f, 0f, 1f, 2f, 2f);
                CpuPaint.DrawBitmap(ctx, bmp,
                    0f, 0f, 0f, 0f, false,
                    0f, 0f, 0f, 0f, false,
                    1f, false);
                Assert.Equal(255, target[3 * 4 + 3]);
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
    public void FillRect_Blends_Flat_Alpha_SourceOver()
    {
        var (target, pin) = MakeTarget(6, 6);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 6, 6, 6);
            try
            {
                CpuPaint.FillRect(ctx, 1f, 1f, 2f, 2f, 0.5f, 1f, 1f, 1f, 1f);
                // 0.5 over 0 -> 128; untouched pixels stay 0.
                Assert.Equal(128, target[1 * 6 + 1]);
                Assert.Equal(128, target[2 * 6 + 2]);
                Assert.Equal(0, target[0]);
                Assert.Equal(0, target[5 * 6 + 5]);
                // Second coat composites, not overwrites
                // (0.5 over 128/255 -> 192).
                CpuPaint.FillRect(ctx, 1f, 1f, 2f, 2f, 0.5f, 1f, 1f, 1f, 1f);
                Assert.Equal(192, target[1 * 6 + 1]);
            }
            finally
            {
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void FillEllipse_Covers_Center_Only()
    {
        var (target, pin) = MakeTarget(9, 9);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 9, 9, 9);
            try
            {
                CpuPaint.FillEllipse(ctx, 4f, 4f, 2f, 2f, 1f, 1f, 1f, 1f, 1f);
                Assert.Equal(255, target[4 * 9 + 4]);
                Assert.Equal(255, target[4 * 9 + 3]);
                Assert.Equal(0, target[0]);
                Assert.Equal(0, target[8 * 9 + 8]);
            }
            finally
            {
                CpuPaint.FreeContext(ctx);
            }
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void Bitmap_Lock_Upload_Roundtrip()
    {
        long bmp = CpuPaint.CreateBitmapEmpty(2, 2);
        try
        {
            Assert.NotEqual(0, bmp);
            nint addr = CpuPaint.LockBitmap(bmp);
            try
            {
                Assert.NotEqual(nint.Zero, addr);
                var pixels = new uint[] { 0xFF000000, 0xFF000000, 0xFF000000, 0xFF000000 };
                fixed (uint* p = pixels)
                    Buffer.MemoryCopy(p, (void*)addr, 16, 16);
            }
            finally
            {
                CpuPaint.UnlockBitmap(bmp);
            }
            var (target, pin) = MakeTarget(4, 4);
            try
            {
                long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 4, 4, 4);
                try
                {
                    CpuPaint.DrawBitmap(ctx, bmp,
                        0f, 0f, 0f, 0f, false,
                        0f, 0f, 0f, 0f, false,
                        1f, false);
                    // 0xFF000000 is BGRA(0,0,0,255): full alpha, so the
                    // uploaded bitmap covers the mask.
                    Assert.Equal(255, target[0]);
                }
                finally
                {
                    CpuPaint.FreeContext(ctx);
                }
            }
            finally
            {
                pin.Free();
            }
        }
        finally
        {
            CpuPaint.FreeBitmap(bmp);
        }
    }

    [Fact]
    public void Premultiply_Roundtrip_White()
    {
        var pixels = new byte[] { 255, 255, 255, 128, 100, 150, 200, 255 };
        fixed (byte* p = pixels)
        {
            CpuPaint.PremultiplyInPlace((nint)p, 2, 1, 8);
            Assert.Equal(128, pixels[0]);
            Assert.Equal(128, pixels[1]);
            Assert.Equal(128, pixels[2]);
            Assert.Equal(128, pixels[3]);
            Assert.Equal(100, pixels[4]);
            Assert.Equal(150, pixels[5]);
            Assert.Equal(200, pixels[6]);
            CpuPaint.UnpremultiplyInPlace((nint)p, 2, 1, 8);
            Assert.Equal(255, pixels[0]);
            Assert.Equal(255, pixels[1]);
            Assert.Equal(255, pixels[2]);
        }
    }

    [Fact]
    public unsafe void ConvertBgra32_Premultiplies_To_Float()
    {
        // Straight red at half alpha: r/g/b premultiplied by a.
        var src = new byte[] { 0, 0, 255, 128 };
        var dst = new float[4];
        fixed (byte* sp = src)
        fixed (float* dp = dst)
        {
            CpuPaint.ConvertBgra32ToPrgba128Float((nint)sp, 4, (nint)dp, 16, 1, 1);
        }
        float a = 128f / 255f;
        Assert.Equal(1f * a, dst[0], precision: 5);
        Assert.Equal(0f, dst[1], precision: 5);
        Assert.Equal(0f, dst[2], precision: 5);
        Assert.Equal(a, dst[3], precision: 5);
    }

    [Fact]
    public unsafe void ConvertPbgra32_Scales_To_Float()
    {
        // Premultiplied source: channels scale straight across.
        var src = new byte[] { 10, 20, 30, 40 };
        var dst = new float[4];
        fixed (byte* sp = src)
        fixed (float* dp = dst)
        {
            CpuPaint.ConvertPbgra32ToPrgba128Float((nint)sp, 4, (nint)dp, 16, 1, 1);
        }
        Assert.Equal(30f / 255f, dst[0], precision: 5);
        Assert.Equal(20f / 255f, dst[1], precision: 5);
        Assert.Equal(10f / 255f, dst[2], precision: 5);
        Assert.Equal(40f / 255f, dst[3], precision: 5);
    }

    [Fact]
    public unsafe void ConvertPrgba128Float_Clamps_To_Pbgra32()
    {
        // Premultiplied float red at half alpha, plus out-of-range
        // channels that must clamp instead of wrapping.
        var src = new float[] { 0.5f, 0f, 0f, 0.5f, 1.5f, -1f, 2f, 1f };
        var dst = new byte[8];
        fixed (float* sp = src)
        fixed (byte* dp = dst)
        {
            CpuPaint.ConvertPrgba128FloatToPbgra32((nint)sp, 32, (nint)dp, 8, 2, 1);
        }
        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.Equal(128, dst[2]);
        Assert.Equal(128, dst[3]);
        Assert.Equal(255, dst[4]);
        Assert.Equal(0, dst[5]);
        Assert.Equal(255, dst[6]);
        Assert.Equal(255, dst[7]);
    }

    [Fact]
    public void FillRect_Bgra32_Blends_White()
    {
        var target = new byte[4 * 4 * 4];
        fixed (byte* p = target)
        {
            long ctx = CpuPaint.CreateTarget((nint)p, 4, 4, 16, 4);
            try
            {
                Assert.NotEqual(0, ctx);
                CpuPaint.FillRect(ctx, 0f, 0f, 4f, 4f, 1f, 1f, 1f, 1f, 0.5f);
                // White at half alpha over transparent black.
                Assert.Equal(128, target[0]);
                Assert.Equal(128, target[1]);
                Assert.Equal(128, target[2]);
                Assert.Equal(128, target[3]);
            }
            finally
            {
                CpuPaint.FreeContext(ctx);
            }
        }
    }

    [Fact]
    public void CreateTarget_Rejects_Other_Depths()
    {
        var target = new byte[16];
        fixed (byte* p = target)
        {
            Assert.Equal(0, CpuPaint.CreateTarget((nint)p, 4, 4, 4, 3));
            Assert.Equal(0, CpuPaint.CreateTarget((nint)p, 4, 4, 4, 2));
        }
    }

    private static uint[] ReadSprite(long bmp, out int w, out int h)
    {
        w = CpuPaint.BitmapWidth(bmp);
        h = CpuPaint.BitmapHeight(bmp);
        var px = new uint[w * h];
        nint ptr = CpuPaint.LockBitmap(bmp);
        try
        {
            unsafe
            {
                uint* src = (uint*)ptr;
                for (int i = 0; i < px.Length; i++)
                    px[i] = src[i];
            }
        }
        finally
        {
            CpuPaint.UnlockBitmap(bmp);
        }
        return px;
    }

    private static uint AlphaOf(uint p) => (p >> 24) & 0xFF;

    [Fact]
    public void CreateSprite_DefaultBrush_Makes_Soft_White_Mask()
    {
        // White clear + black sprite + invert: opaque white dab with a
        // soft alpha falloff, transparent corners.
        long bmp = CpuPaint.CreateSprite(35, 35, 17.0, 17.0, 28.755, 4.0,
            1f, 1f, 1f, 1f, 0f, 0f, 0f, 1f, 1, 0.75);
        try
        {
            uint[] px = ReadSprite(bmp, out int w, out int h);
            Assert.Equal(35, w);
            Assert.Equal(35, h);
            Assert.Equal(255u, AlphaOf(px[17 * w + 17]));
            Assert.Equal(0u, AlphaOf(px[0]));
            Assert.Equal(0u, AlphaOf(px[(h - 1) * w + (w - 1)]));
            uint prev = 256;
            for (int x = 17; x < w; x++)
            {
                uint a = AlphaOf(px[17 * w + x]);
                Assert.True(a <= prev);
                prev = a;
            }
        }
        finally
        {
            CpuPaint.FreeBitmap(bmp);
        }
    }

    [Fact]
    public void CreateSprite_HardBrush_Has_Crisp_Edge()
    {
        long bmp = CpuPaint.CreateSprite(35, 35, 17.0, 17.0, 28.0, 4.0,
            1f, 1f, 1f, 1f, 0f, 0f, 0f, 1f, 1, 1.0);
        try
        {
            uint[] px = ReadSprite(bmp, out int w, out _);
            Assert.Equal(255u, AlphaOf(px[17 * w + 17]));
            Assert.Equal(255u, AlphaOf(px[17 * w + 26]));
            Assert.Equal(0u, AlphaOf(px[17 * w + 34]));
            Assert.Equal(0u, AlphaOf(px[0]));
        }
        finally
        {
            CpuPaint.FreeBitmap(bmp);
        }
    }

    [Fact]
    public void CreateSprite_NoInvert_Keeps_Black_Mask()
    {
        // Without invert the black dab stays black: center alpha comes
        // from blue (0), the white surround stays opaque.
        long bmp = CpuPaint.CreateSprite(35, 35, 17.0, 17.0, 28.0, 4.0,
            1f, 1f, 1f, 1f, 0f, 0f, 0f, 1f, 0, 1.0);
        try
        {
            uint[] px = ReadSprite(bmp, out int w, out _);
            Assert.Equal(0u, AlphaOf(px[17 * w + 17]));
            Assert.Equal(255u, AlphaOf(px[0]));
        }
        finally
        {
            CpuPaint.FreeBitmap(bmp);
        }
    }

    [Fact]
    public void CreateSprite_Degenerate_Returns_Zero()
    {
        Assert.Equal(0, CpuPaint.CreateSprite(0, 35, 1.0, 1.0, 4.0, 1.0,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0, 1.0));
        Assert.Equal(0, CpuPaint.CreateSprite(35, 35, 1.0, 1.0, 0.0, 1.0,
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0, 1.0));
    }

    [Fact]
    public void Zero_Handles_Are_Silent_Noops()
    {
        // The Draw rule binds handle 0 for non-mask pixel types; every
        // op must shrug instead of throwing through GCHandle.
        CpuPaint.SetTransform(0, 1f, 0f, 0f, 1f, 0f, 0f);
        CpuPaint.DrawBitmap(0, 0, 0f, 0f, 0f, 0f, false, 0f, 0f, 0f, 0f, false, 1f, true);
        CpuPaint.FillRect(0, 0f, 0f, 4f, 4f, 1f, 1f, 1f, 1f, 1f);
        CpuPaint.FillEllipse(0, 2f, 2f, 2f, 2f, 1f, 1f, 1f, 1f, 1f);
        CpuPaint.FillGeometry(0, new object(), 1f, 1f, 1f, 1f, 1f);
        CpuPaint.FreeBitmap(0);
        CpuPaint.UnlockBitmap(0);
        Assert.Equal(nint.Zero, CpuPaint.LockBitmap(0));
        Assert.Equal(0, CpuPaint.CreateBitmapEmpty(0, 4));
        CpuPaint.GetTransform(0, out float m11, out _, out _, out _, out _, out _);
        Assert.Equal(1f, m11);
    }

    // FillGeometry duck-types the provider by getter shape
    // (get_Figures/get_Rect/get_Center/get_Geometry), so plain
    // stubs exercise the same walk the PDN providers take.
    private sealed class StubPoint(double x, double y)
    {
        public double X => x;
        public double Y => y;
    }

    private sealed class StubSize(double w, double h)
    {
        public double Width => w;
        public double Height => h;
    }

    private sealed class StubBox(double x, double y, double w, double h)
    {
        public double X => x;
        public double Y => y;
        public double Width => w;
        public double Height => h;
    }

    private sealed class StubRect(StubBox box, double rx = 0, double ry = 0)
    {
        public StubBox Rect => box;
        public double RadiusX => rx;
        public double RadiusY => ry;
    }

    private sealed class StubEllipse(StubPoint center, double rx, double ry)
    {
        public StubPoint Center => center;
        public double RadiusX => rx;
        public double RadiusY => ry;
    }

    private sealed class StubLine(StubPoint p)
    {
        public StubPoint Point => p;
    }

    private sealed class StubBezier(StubPoint p1, StubPoint p2, StubPoint p3)
    {
        public StubPoint Point1 => p1;
        public StubPoint Point2 => p2;
        public StubPoint Point3 => p3;
    }

    private sealed class StubQuad(StubPoint p1, StubPoint p2)
    {
        public StubPoint Point1 => p1;
        public StubPoint Point2 => p2;
    }

    private sealed class StubArc(StubPoint p, StubSize size, double angle, bool large, int sweep)
    {
        public StubPoint Point => p;
        public StubSize Size => size;
        public double RotationAngle => angle;
        public bool IsLargeArc => large;
        public int SweepDirection => sweep;
    }

    private sealed class StubPoly(List<object> pts)
    {
        public List<object> Points => pts;
    }

    private sealed class StubFigure(StubPoint start, List<object> segs, bool filled = true)
    {
        public StubPoint StartPoint => start;
        public List<object> Segments => segs;
        public bool IsFilled => filled;
    }

    private sealed class StubPath(List<object> figs, int rule = 0)
    {
        public List<object> Figures => figs;
        public int FillRule => rule;
    }

    private sealed class StubFlat(object inner)
    {
        public object Geometry => inner;
    }

    private static (long Ctx, byte[] Pixels, GCHandle Pin) MakeBgraTarget(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        long ctx = CpuPaint.CreateTarget(pin.AddrOfPinnedObject(), w, h, w * 4, 4);
        return (ctx, pixels, pin);
    }

    private static uint PixelOf(byte[] pixels, int w, int x, int y)
        => BitConverter.ToUInt32(pixels, (y * w + x) * 4);

    [Fact]
    public void FillGeometry_Rect_Fills_Interior()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.FillGeometry(ctx, new StubRect(new StubBox(2, 3, 5, 4)),
                1f, 1f, 0f, 0f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 12, 4, 5) >> 24);
            Assert.Equal(255u, (PixelOf(pixels, 12, 4, 5) >> 16) & 0xFFu);
            Assert.Equal(0u, PixelOf(pixels, 12, 4, 5) & 0xFFu);
            Assert.Equal(0u, PixelOf(pixels, 12, 0, 0) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 11, 11) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillGeometry_RoundedRect_Fills_Center_Not_Corner()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(16, 16);
        try
        {
            CpuPaint.FillGeometry(ctx, new StubRect(new StubBox(2, 2, 12, 12), 5, 5),
                1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 16, 8, 8) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 16, 2, 2) >> 24);
            Assert.Equal(255u, PixelOf(pixels, 16, 8, 2) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillGeometry_Triangle_Path_Fills()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(12, 12);
        try
        {
            var fig = new StubFigure(new StubPoint(2, 2),
            [
                new StubLine(new StubPoint(9, 2)),
                new StubLine(new StubPoint(2, 9)),
            ]);
            CpuPaint.FillGeometry(ctx, new StubPath([fig]), 1f, 0f, 1f, 0f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 12, 3, 3) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 8, 8) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillGeometry_EvenOdd_Leaves_Hole_Nonzero_Fills()
    {
        static StubPath Rings(int rule) => new(
        [
            new StubFigure(new StubPoint(1, 1),
            [
                new StubLine(new StubPoint(10, 1)),
                new StubLine(new StubPoint(10, 10)),
                new StubLine(new StubPoint(1, 10)),
            ]),
            new StubFigure(new StubPoint(4, 4),
            [
                new StubLine(new StubPoint(7, 4)),
                new StubLine(new StubPoint(7, 7)),
                new StubLine(new StubPoint(4, 7)),
            ]),
        ], rule);
        var (ctx0, pixels0, pin0) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.FillGeometry(ctx0, Rings(0), 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(0u, PixelOf(pixels0, 12, 5, 5) >> 24);
            Assert.Equal(255u, PixelOf(pixels0, 12, 2, 2) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx0);
            pin0.Free();
        }
        var (ctx1, pixels1, pin1) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.FillGeometry(ctx1, Rings(1), 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels1, 12, 5, 5) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx1);
            pin1.Free();
        }
    }

    [Fact]
    public void FillGeometry_Ellipse_And_Flattened_And_Curves()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(20, 20);
        try
        {
            CpuPaint.FillGeometry(ctx,
                new StubEllipse(new StubPoint(5, 5), 3, 3), 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 20, 5, 5) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 20, 0, 0) >> 24);
            CpuPaint.FillGeometry(ctx,
                new StubFlat(new StubRect(new StubBox(12, 12, 4, 4))),
                1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 20, 13, 13) >> 24);
            // Closed hump: line out, cubic back along the top.
            var fig = new StubFigure(new StubPoint(12, 4),
            [
                new StubLine(new StubPoint(18, 4)),
                new StubBezier(new StubPoint(16, 0), new StubPoint(14, 0), new StubPoint(12, 4)),
            ]);
            CpuPaint.FillGeometry(ctx, new StubPath([fig]), 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 20, 15, 3) >> 24);
            // Quarter arc (2,14)->(6,18), r=4, clockwise: the arc
            // bulges through (4.8,15.2); the chord side stays empty.
            var arc = new StubFigure(new StubPoint(2, 14),
            [
                new StubArc(new StubPoint(6, 18), new StubSize(4, 4), 0, false, 1),
            ]);
            CpuPaint.FillGeometry(ctx, new StubPath([arc]), 1f, 1f, 1f, 1f, 1f);
            Assert.NotEqual(0u, PixelOf(pixels, 20, 5, 16) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 20, 3, 17) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillGeometry_PolyLine_Fills()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(12, 12);
        try
        {
            var fig = new StubFigure(new StubPoint(2, 7),
            [
                new StubPoly([new StubPoint(9, 2), new StubPoint(9, 9)]),
            ]);
            CpuPaint.FillGeometry(ctx, new StubPath([fig]), 1f, 0f, 0f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 12, 7, 7) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 2, 2) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    private sealed class StubGroup(List<object> kids, int rule = 0)
    {
        public List<object> Children => kids;
        public int FillRule => rule;
    }

    [Fact]
    public void StrokeGeometry_Rect_Strokes_Border_Not_Center()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.StrokeGeometry(ctx, new StubRect(new StubBox(2, 2, 8, 8)),
                2f, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 12, 2, 5) >> 24);
            Assert.Equal(255u, PixelOf(pixels, 12, 5, 2) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 6, 6) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 0, 0) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void GeometryGroup_Fills_And_Strokes_Children()
    {
        var group = new StubGroup(
        [
            new StubRect(new StubBox(1, 1, 3, 3)),
            new StubRect(new StubBox(7, 7, 3, 3)),
        ]);
        var (ctx, pixels, pin) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.FillGeometry(ctx, group, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 12, 2, 2) >> 24);
            Assert.Equal(255u, PixelOf(pixels, 12, 8, 8) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 5, 5) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
        var (ctx2, pixels2, pin2) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.StrokeGeometry(ctx2, group, 1f, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels2, 12, 1, 2) >> 24);
            Assert.Equal(255u, PixelOf(pixels2, 12, 7, 8) >> 24);
            Assert.Equal(0u, PixelOf(pixels2, 12, 5, 5) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx2);
            pin2.Free();
        }
    }

    private sealed class StubLayout(string text, double size)
    {
        public string Text => text;
        public double FontSize => size;
    }

    [Fact]
    public void DrawText_Renders_Glyph_Cells()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(12, 12);
        try
        {
            CpuPaint.DrawText(ctx, new StubLayout("A", 7), 2f, 2f, 1f, 1f, 1f, 1f, 1f);
            // A top row ".###.": cells x=3..5 inked, x=2 blank.
            Assert.Equal(255u, PixelOf(pixels, 12, 4, 2) >> 24);
            Assert.Equal(0u, PixelOf(pixels, 12, 2, 2) >> 24);
            // A left stem rows 1..5: (2,4) inked.
            Assert.Equal(255u, PixelOf(pixels, 12, 2, 4) >> 24);
            // Lowercase maps to uppercase.
            CpuPaint.DrawText(ctx, new StubLayout("a", 7), 2f, 2f, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(255u, PixelOf(pixels, 12, 4, 2) >> 24);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void DrawText_Empty_Silent_Unknown_Throws()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(8, 8);
        try
        {
            CpuPaint.DrawText(ctx, new StubLayout("", 7), 0f, 0f, 1f, 1f, 1f, 1f, 1f);
            Assert.Equal(0u, PixelOf(pixels, 8, 0, 0) >> 24);
            Assert.Throws<NotSupportedException>(
                () => CpuPaint.DrawText(ctx, new StubPoint(1, 2), 0f, 0f, 1f, 1f, 1f, 1f, 1f));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillGeometry_Unknown_Provider_Throws()
    {
        var (ctx, _, pin) = MakeBgraTarget(4, 4);
        try
        {
            Assert.Throws<NotSupportedException>(
                () => CpuPaint.FillGeometry(ctx, new StubPoint(1, 2), 1f, 1f, 1f, 1f, 1f));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void DrawChecker_Tiles_Alternate_By_Parity()
    {
        // Independent oracle: tile index from the origin, not the
        // implementation's float math.
        var (ctx, pixels, pin) = MakeBgraTarget(8, 8);
        try
        {
            const uint c1 = 0xFFFFFFFF;
            const uint c2 = 0xFF000000;
            CpuPaint.DrawChecker(ctx, 0f, 0f, 8f, 8f, c1, c2, 4);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    Assert.Equal((((x / 4) + (y / 4)) & 1) == 0 ? c1 : c2,
                        PixelOf(pixels, 8, x, y));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void DrawChecker_Respects_Rect_And_Skips_Transparent()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(8, 8);
        try
        {
            const uint c1 = 0xFFFFFFFF;
            CpuPaint.DrawChecker(ctx, 0f, 0f, 8f, 8f, c1, c1, 8);
            Assert.Equal(c1, PixelOf(pixels, 8, 0, 0));
            // Transparent tiles leave prior pixels alone.
            CpuPaint.DrawChecker(ctx, 0f, 0f, 4f, 4f, 0x00000000u, 0x00000000u, 2);
            Assert.Equal(c1, PixelOf(pixels, 8, 1, 1));
            // Opaque sub-rect only touches its own pixels.
            const uint c2 = 0xFF000000;
            CpuPaint.DrawChecker(ctx, 4f, 4f, 4f, 4f, c2, c2, 8);
            Assert.Equal(c2, PixelOf(pixels, 8, 5, 5));
            Assert.Equal(c1, PixelOf(pixels, 8, 3, 3));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    // Anchor for the [Fact] dislodged above by this insert (attribute
    // must sit on a method): discovery ignores private methods.
    private static void GradStubsAnchor() { }

    private sealed class StubGradColor(float r, float g, float b, float a)
    {
        public float R => r;
        public float G => g;
        public float B => b;
        public float A => a;
    }

    private sealed class StubGradStop(double offset, StubGradColor color)
    {
        public double Offset => offset;
        public StubGradColor Color => color;
    }

    private static List<object> GradStops(params (double Offset, float R, float G, float B, float A)[] stops)
    {
        var list = new List<object>();
        foreach (var s in stops)
            list.Add(new StubGradStop(s.Offset, new StubGradColor(s.R, s.G, s.B, s.A)));
        return list;
    }

    private static void AssertGray(byte[] pixels, int w, int x, int y, int expected, int alpha, int tol = 4)
    {
        uint px = PixelOf(pixels, w, x, y);
        int b = (int)(px & 0xFFu), g = (int)((px >> 8) & 0xFFu), r = (int)((px >> 16) & 0xFFu), a = (int)(px >> 24);
        Assert.InRange(r, expected - tol, expected + tol);
        Assert.InRange(g, expected - tol, expected + tol);
        Assert.InRange(b, expected - tol, expected + tol);
        Assert.InRange(a, alpha - 1, alpha + 1);
    }

    [Fact]
    public void FillRectGradient_TwoStop_BlackWhite_Ramps()
    {
        // Independent oracle: pixel centers projected on the axis,
        // not the implementation's LUT math.
        var (ctx, pixels, pin) = MakeBgraTarget(8, 1);
        try
        {
            long lut = CpuPaint.CreateGradientLut(
                GradStops((0, 0f, 0f, 0f, 1f), (1, 1f, 1f, 1f, 1f)), 1f, 0);
            CpuPaint.FillRectGradient(ctx, 0f, 0f, 8f, 1f, lut, 0f, 0f, 8f, 0f, 0);
            for (int x = 0; x < 8; x++)
            {
                int expected = (int)(((x + 0.5f) / 8f) * 255f + 0.5f);
                AssertGray(pixels, 8, x, 0, expected, 255);
            }
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillRectGradient_Opacity_Bakes_And_Spread_Wraps()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 1);
        try
        {
            // Half opacity over black: premultiplied halves to mid gray.
            long lut = CpuPaint.CreateGradientLut(
                GradStops((0, 1f, 1f, 1f, 1f), (1, 1f, 1f, 1f, 1f)), 0.5f, 0);
            CpuPaint.FillRectGradient(ctx, 0f, 0f, 4f, 1f, lut, 0f, 0f, 4f, 0f, 0);
            AssertGray(pixels, 4, 2, 0, 128, 128);
            // Repeat: axis half the rect, second half mirrors the first.
            long rep = CpuPaint.CreateGradientLut(
                GradStops((0, 0f, 0f, 0f, 1f), (1, 1f, 1f, 1f, 1f)), 1f, 0);
            CpuPaint.FillRectGradient(ctx, 0f, 0f, 4f, 1f, rep, 0f, 0f, 2f, 0f, 1);
            int left = (int)(PixelOf(pixels, 4, 0, 0) & 0xFFu);
            int right = (int)(PixelOf(pixels, 4, 2, 0) & 0xFFu);
            Assert.InRange(Math.Abs(left - right), 0, 4);
            // Reflect: pixel 2 sits at t=1.25, folding back to 0.75.
            long mirror = CpuPaint.CreateGradientLut(
                GradStops((0, 0f, 0f, 0f, 1f), (1, 1f, 1f, 1f, 1f)), 1f, 0);
            CpuPaint.FillRectGradient(ctx, 0f, 0f, 4f, 1f, mirror, 0f, 0f, 2f, 0f, 2);
            int folded = (int)(PixelOf(pixels, 4, 2, 0) & 0xFFu);
            Assert.InRange(folded, (int)(0.6f * 255f), (int)(0.9f * 255f));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillRectGradient_Empty_Stops_Draw_Nothing()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            long lut = CpuPaint.CreateGradientLut(new List<object>(), 1f, 0);
            Assert.NotEqual(0, lut);
            CpuPaint.FillRectGradient(ctx, 0f, 0f, 4f, 4f, lut, 0f, 0f, 4f, 0f, 0);
            Assert.Equal(0u, PixelOf(pixels, 4, 1, 1) >> 24);
            CpuPaint.FillRectGradient(0, 0f, 0f, 4f, 4f, lut, 0f, 0f, 4f, 0f, 0);
            CpuPaint.FillRectGradient(ctx, 0f, 0f, 4f, 4f, 0, 0f, 0f, 4f, 0f, 0);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void ControlScratchTarget_Is_Usable_And_Freeable()
    {
        long t = CpuPaint.GetControlScratchTarget();
        Assert.NotEqual(0, t);
        CpuPaint.FreeContext(t);
        long t2 = CpuPaint.GetControlScratchTarget();
        Assert.NotEqual(0, t2);
        CpuPaint.FreeContext(t2);
    }

    [Fact]
    public void ConvertBgra32ToPrgba64Half_Premultiplies_Opaque_Red()
    {
        byte[] src = { 0, 0, 255, 255 };
        byte[] dst = new byte[8];
        fixed (byte* s = src)
        fixed (byte* d = dst)
            CpuPaint.ConvertBgra32ToPrgba64Half((IntPtr)s, 4, (IntPtr)d, 8, 1, 1);
        Half* h = (Half*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref dst[0]);
        Assert.Equal(1f, (float)h[0], 3);
        Assert.Equal(0f, (float)h[1], 3);
        Assert.Equal(0f, (float)h[2], 3);
        Assert.Equal(1f, (float)h[3], 3);
    }

    [Fact]
    public void ConvertBgra32ToPrgba64Half_HalfTransparent_Premultiplies()
    {
        byte[] src = { 0, 0, 255, 128 };
        byte[] dst = new byte[8];
        fixed (byte* s = src)
        fixed (byte* d = dst)
            CpuPaint.ConvertBgra32ToPrgba64Half((IntPtr)s, 4, (IntPtr)d, 8, 1, 1);
        Half* h = (Half*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref dst[0]);
        Assert.Equal(128f / 255f, (float)h[0], 2);
        Assert.Equal(0f, (float)h[1], 3);
        Assert.Equal(0f, (float)h[2], 3);
        Assert.Equal(128f / 255f, (float)h[3], 2);
    }

    private sealed record Pt(double X, double Y);

    private sealed class FakeSeg
    {
        public Pt Point { get; }
        public FakeSeg(double x, double y) => Point = new Pt(x, y);
    }

    private sealed class FakeFig
    {
        public bool IsFilled => true;
        public Pt StartPoint { get; }
        public List<FakeSeg> Segments { get; }
        public FakeFig(Pt start, params FakeSeg[] segs) { StartPoint = start; Segments = segs.ToList(); }
    }

    private sealed class FakeGeo
    {
        public List<FakeFig> Figures { get; }
        public FakeGeo(params FakeFig[] figs) => Figures = figs.ToList();
    }

    private sealed class FakeCombined
    {
        public object Geometry1 { get; }
        public object Geometry2 { get; }
        public int GeometryCombineMode { get; }
        public FakeCombined(object g1, object g2, int mode) { Geometry1 = g1; Geometry2 = g2; GeometryCombineMode = mode; }
    }

    private static FakeGeo Square(double x0, double y0, double x1, double y1) => new(new FakeFig(
        new Pt(x0, y0), new FakeSeg(x1, y0), new FakeSeg(x1, y1), new FakeSeg(x0, y1)));

    private static (long Ctx, byte[] Pixels, GCHandle Pin) FillSquares(int mode)
    {
        var t = MakeBgraTarget(8, 8);
        try
        {
            CpuPaint.FillGeometry(t.Ctx,
                new FakeCombined(Square(0, 0, 4, 4), Square(2, 2, 6, 6), mode),
                1f, 1f, 1f, 1f, 1f);
            return t;
        }
        catch
        {
            CpuPaint.FreeContext(t.Ctx);
            t.Pin.Free();
            throw;
        }
    }

    [Fact]
    public void FillCombined_Union_Paints_Both_Squares()
    {
        var (ctx, pixels, pin) = FillSquares(0);
        try
        {
            AssertGray(pixels, 8, 1, 1, 255, 255);
            AssertGray(pixels, 8, 5, 5, 255, 255);
            AssertGray(pixels, 8, 3, 3, 255, 255);
            AssertGray(pixels, 8, 7, 7, 0, 0);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillCombined_Intersect_Paints_Overlap_Only()
    {
        var (ctx, pixels, pin) = FillSquares(1);
        try
        {
            AssertGray(pixels, 8, 1, 1, 0, 0);
            AssertGray(pixels, 8, 5, 5, 0, 0);
            AssertGray(pixels, 8, 3, 3, 255, 255);
            AssertGray(pixels, 8, 7, 7, 0, 0);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillCombined_Exclude_Removes_Overlap()
    {
        var (ctx, pixels, pin) = FillSquares(3);
        try
        {
            AssertGray(pixels, 8, 1, 1, 255, 255);
            AssertGray(pixels, 8, 5, 5, 0, 0);
            AssertGray(pixels, 8, 3, 3, 0, 0);
            AssertGray(pixels, 8, 7, 7, 0, 0);
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    [Fact]
    public void FillCombined_Nested_Union_Of_Intersect()
    {
        var inner = new FakeCombined(Square(0, 0, 4, 4), Square(2, 2, 6, 6), 1);
        var t = MakeBgraTarget(8, 8);
        try
        {
            CpuPaint.FillGeometry(t.Ctx,
                new FakeCombined(inner, Square(5, 5, 7, 7), 0),
                1f, 1f, 1f, 1f, 1f);
            AssertGray(t.Pixels, 8, 3, 3, 255, 255);
            AssertGray(t.Pixels, 8, 6, 6, 255, 255);
            AssertGray(t.Pixels, 8, 1, 1, 0, 0);
            AssertGray(t.Pixels, 8, 5, 3, 0, 0);
        }
        finally
        {
            CpuPaint.FreeContext(t.Ctx);
            t.Pin.Free();
        }
    }

    [Fact]
    public void CreateBitmapFromHalfSource_Widens_Opaque_Blue()
    {
        // Half 0.0 = 0x0000, 1.0 = 0x3C00: opaque blue must widen to
        // 0xFF0000FF through the device upload helper.
        var src = new FakeHalfSource(0x0000, 0x0000, 0x3C00, 0x3C00);
        long bmp = CpuPaint.CreateBitmapFromHalfSource(src, 1, 1);
        Assert.NotEqual(0, bmp);
        try
        {
            IntPtr p = CpuPaint.LockBitmap(bmp);
            Assert.NotEqual(IntPtr.Zero, p);
            uint px = unchecked((uint)Marshal.ReadInt32(p));
            Assert.Equal(0xFF0000FFu, px);
            CpuPaint.UnlockBitmap(bmp);
        }
        finally
        {
            CpuPaint.FreeBitmap(bmp);
        }
    }

    private sealed class FakeHalfSource : IDisposable
    {
        private readonly byte[] _bytes;
        private GCHandle _pin;
        public FakeHalfSource(params ushort[] words)
        {
            _bytes = new byte[words.Length * 2];
            System.Buffer.BlockCopy(words, 0, _bytes, 0, _bytes.Length);
            _pin = GCHandle.Alloc(_bytes, GCHandleType.Pinned);
        }

        public IntPtr Buffer => _pin.AddrOfPinnedObject();
        public int BufferStride => 8;

        public void CopyPixels(IntPtr dst, int stride, int size, object? clip)
        {
            Marshal.Copy(_bytes, 0, dst, Math.Min(size, _bytes.Length));
        }

        public void Dispose()
        {
            if (_pin.IsAllocated)
                _pin.Free();
        }
    }

        [Fact]
    public void CreateBitmapFromHalfSource_PointerBuffer_Widens()
    {
        // PooledBitmap<TPixel>.Buffer returns a raw TPixel*, which
        // PropertyInfo.GetValue cannot delegate-create for; the helper
        // must read it via MethodInfo.Invoke + Pointer unbox instead.
        var src = new FakePointerHalfSource(0x0000, 0x0000, 0x3C00, 0x3C00);
        try
        {
            long bmp = CpuPaint.CreateBitmapFromHalfSource(src, 1, 1);
            Assert.NotEqual(0, bmp);
            try
            {
                IntPtr p = CpuPaint.LockBitmap(bmp);
                Assert.NotEqual(IntPtr.Zero, p);
                Assert.Equal(0xFF0000FFu, unchecked((uint)Marshal.ReadInt32(p)));
                CpuPaint.UnlockBitmap(bmp);
            }
            finally { CpuPaint.FreeBitmap(bmp); }
        }
        finally { src.Dispose(); }
    }

    private sealed unsafe class FakePointerHalfSource : IDisposable
    {
        private readonly byte[] _bytes;
        private GCHandle _pin;
        public FakePointerHalfSource(params ushort[] words)
        {
            _bytes = new byte[words.Length * 2];
            System.Buffer.BlockCopy(words, 0, _bytes, 0, _bytes.Length);
            _pin = GCHandle.Alloc(_bytes, GCHandleType.Pinned);
        }
        public Half* Buffer => (Half*)_pin.AddrOfPinnedObject().ToPointer();
        public int BufferStride => 8;
        public void Dispose()
        {
            if (_pin.IsAllocated)
                _pin.Free();
        }
    }

    [Fact]
    public void GetProxySourceImage_Unwraps_Nested_Proxies()
    {
        // Effect chains nest proxies (outer Source = inner proxy);
        // the unwrap must recurse to the final plain input.
        var inner = new FakeProxy(new FakeSourceBag("FINAL"));
        var outer = new FakeProxy(new FakeSourceBag(inner));
        Assert.Equal("FINAL", CpuPaint.GetProxySourceImage(outer));
    }

    public interface IDeviceEffectProxy<T> { FakePropBag Properties { get; } }
    public sealed class FakePropBag
    {
        private readonly FakeSourceBag _src;
        public FakePropBag(FakeSourceBag src) { _src = src; }
        public FakeSourceBag Source => _src;
    }
    public sealed class FakeSourceBag
    {
        private readonly object? _v;
        public FakeSourceBag(object? v) { _v = v; }
        public object? Get() => _v;
    }
    public sealed class FakeProxy : IDeviceEffectProxy<FakeProxy>
    {
        private readonly FakePropBag _b;
        public FakeProxy(FakeSourceBag src) { _b = new FakePropBag(src); }
        public FakePropBag Properties => _b;
    }

    [Fact]
    public void DrawProceduralEffect_Checkerboard_Alternates()
    {
        // Palette swatches draw a cropped CheckerboardEffect through a
        // proxy: the procedural render must alternate the two colors.
        var (ctx, pixels, pin) = MakeBgraTarget(16, 16);
        try
        {
            var effect = new FakeCheckerboardEffect(
                new FakeFloatColor(1f, 1f, 1f, 1f),
                new FakeFloatColor(0.25f, 0.5f, 0.75f, 1f), 8);
            Assert.True(CpuPaint.DrawProceduralEffect(ctx, effect, 0, 0, 16, 16));
            Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 16, 0, 0));
            Assert.Equal(0xFF4080BFu, PixelOf(pixels, 16, 8, 0));
            Assert.Equal(0xFF4080BFu, PixelOf(pixels, 16, 0, 8));
            Assert.Equal(0xFFFFFFFFu, PixelOf(pixels, 16, 8, 8));
            Assert.False(CpuPaint.DrawProceduralEffect(ctx, new object(), 0, 0, 16, 16));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    private sealed class FakeFloatColor(float r, float g, float b, float a)
    {
        public float R => r;
        public float G => g;
        public float B => b;
        public float A => a;
    }

    private sealed class FakeCheckerProps(FakeFloatColor c1, FakeFloatColor c2, int tile)
    {
        public FakeFloatColor Color1 => c1;
        public FakeFloatColor Color2 => c2;
        public int TileSize => tile;
    }

    private sealed class FakeCheckerboardEffect(FakeFloatColor c1, FakeFloatColor c2, int tile)
    {
        private readonly FakeCheckerProps _p = new(c1, c2, tile);
        public FakeCheckerProps Properties => _p;
    }

[Fact]
    public void ConvertPbgra32ToPrgba64Half_Scales_Only()
    {
        byte[] src = { 10, 20, 30, 40 };
        byte[] dst = new byte[8];
        fixed (byte* s = src)
        fixed (byte* d = dst)
            CpuPaint.ConvertPbgra32ToPrgba64Half((IntPtr)s, 4, (IntPtr)d, 8, 1, 1);
        Half* h = (Half*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref dst[0]);
        Assert.Equal(30f / 255f, (float)h[0], 3);
        Assert.Equal(20f / 255f, (float)h[1], 3);
        Assert.Equal(10f / 255f, (float)h[2], 3);
        Assert.Equal(40f / 255f, (float)h[3], 3);
    }

    [Fact]
    public void BlitRaster_Tints_WhiteAlpha_With_Ink_SourceOver()
    {
        var (ctx, pixels, pin) = MakeBgraTarget(4, 4);
        try
        {
            // 2x2 white-with-alpha sprite: opaque, half, zero, opaque.
            byte[] rgba = new byte[2 * 2 * 4];
            rgba[3] = 255;
            rgba[7] = 128;
            rgba[11] = 0;
            rgba[15] = 255;
            CpuPaint.BlitRaster(ctx, rgba, 2, 2, 1f, 1f, 1f, 1f, 0f, 0f, 1f);
            uint full = PixelOf(pixels, 4, 1, 1);
            Assert.Equal(255u, full >> 24);
            Assert.Equal(255u, (full >> 16) & 0xFFu);
            Assert.Equal(0u, (full >> 8) & 0xFFu);
            Assert.Equal(0u, full & 0xFFu);
            uint half = PixelOf(pixels, 4, 2, 1);
            Assert.Equal(128u, half >> 24);
            Assert.Equal(128u, (half >> 16) & 0xFFu);
            Assert.Equal(0u, PixelOf(pixels, 4, 1, 2));
            Assert.Equal(0u, PixelOf(pixels, 4, 0, 0));
            Assert.Equal(0u, PixelOf(pixels, 4, 3, 3));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }

    private sealed class FakeEffectToken
    {
        public string Tag { get; set; } = "t";
        public FakeEffectToken CloneT() => new FakeEffectToken { Tag = Tag };
    }

    private sealed class FakePaintSurface
    {
        public bool WasCloned;
        public int Width => 320;
        public int Height => 200;
        public FakePaintSurface Clone()
        {
            WasCloned = true;
            return new FakePaintSurface();
        }
    }

    private sealed class FakePaintLayer
    {
        public FakePaintSurface Surface { get; } = new FakePaintSurface();
    }

    private sealed class FakeDocWorkspace
    {
        public FakePaintLayer ActiveLayer { get; } = new FakePaintLayer();
        public FakeHistoryStack History { get; } = new FakeHistoryStack();
        public int ActiveLayerIndex => 0;
    }

    private sealed class FakeHistoryStack
    {
        public readonly System.Collections.Generic.List<object> Pushed = new();
        public void PushNewMemento(object memento) => Pushed.Add(memento);
    }

    private sealed class FakeAppWorkspace
    {
        public FakeDocWorkspace ActiveDocumentWorkspace { get; } = new FakeDocWorkspace();
    }

    private class FakeEffectMenu
    {
        public FakeAppWorkspace AppWorkspace { get; } = new FakeAppWorkspace();
        public readonly System.Collections.Generic.List<object?[]> DoEffectCalls = new();
        public bool LastResult = true;

        private bool DoEffect(object effect, object token, object? a, object? b, object snapshot, out object? error)
        {
            error = null;
            DoEffectCalls.Add(new object?[] { effect, token, a, b, snapshot });
            return LastResult;
        }
    }

    private sealed class FakeEffectDialog
    {
        public int DialogResult { get; set; }
        public object Effect { get; set; } = new object();
        public object Token { get; set; } = new FakeEffectToken();
    }

    private interface IEffectConfigForm
    {
    }

    private sealed class FakeEffectDialogInner : IEffectConfigForm
    {
        public int DialogResult { get; set; }
        public object Effect { get; set; } = new object();
        public object Token { get; set; } = new FakeEffectToken();
    }

    private sealed class FakeEffectDialogWrapper
    {
        public IEffectConfigForm? innerRef;
        public FakeEffectDialogWrapper(IEffectConfigForm inner) { innerRef = inner; }
    }

    [Fact]
    public void ResumeEffectApply_Runs_DoEffect_With_Cloned_Token_And_Snapshot()
    {
        var menu = new FakeEffectMenu();
        var dialog = new FakeEffectDialog { DialogResult = 1 };
        var live = menu.AppWorkspace.ActiveDocumentWorkspace.ActiveLayer.Surface;
        CpuPaint.NoteEffectMenu(dialog, menu);
        Assert.True(CpuPaint.ResumeEffectApply(dialog));
        Assert.Single(menu.DoEffectCalls);
        object?[] call = menu.DoEffectCalls[0];
        Assert.Same(dialog.Effect, call[0]);
        // The token is cloned (dialog keeps its own) and the surface is
        // a snapshot (the live layer is the render target, not the
        // memento source).
        Assert.NotSame(dialog.Token, call[1]);
        Assert.IsType<FakeEffectToken>(call[1]);
        Assert.NotSame(live, call[4]);
        Assert.IsType<FakePaintSurface>(call[4]);
        Assert.True(live.WasCloned);
        // The render-scan Results are real full-surface scans, not the
        // nulls that made DoEffect absorb the apply with no undo push.
        var scans = Assert.IsType<PaintDotNet.Functional.ResultValueImpl<
            System.Collections.Generic.IReadOnlyList<PaintDotNet.Rendering.RectInt32>>>(call[2]);
        Assert.Same(scans, call[3]);
        var rects = Assert.Single(scans.Value);
        Assert.Equal(0, rects.X);
        Assert.Equal(0, rects.Y);
        Assert.Equal(320, rects.Width);
        Assert.Equal(200, rects.Height);
        // The stash is consumed: a second settle is a silent no-op.
        Assert.False(CpuPaint.ResumeEffectApply(dialog));
        Assert.Single(menu.DoEffectCalls);
    }

    [Fact]
    public void ResumeEffectApply_Resolves_Wrapper_Inner_Form()
    {
        // Live, the noted dialog is an EffectConfigFormWrapper while
        // FormClosed fires on the hosted inner form (LevelsGpuEffectForm):
        // the note must cover the inner instance or the resume misses.
        var menu = new FakeEffectMenu();
        var inner = new FakeEffectDialogInner { DialogResult = 1 };
        var wrapper = new FakeEffectDialogWrapper(inner);
        CpuPaint.NoteEffectMenu(wrapper, menu);
        Assert.True(CpuPaint.ResumeEffectApply(inner));
        Assert.Single(menu.DoEffectCalls);
        Assert.Same(inner.Effect, menu.DoEffectCalls[0][0]);
    }

    private sealed class FakeDerivedMenu : FakeEffectMenu
    {
    }

    private sealed class FakeEffectInfo
    {
        public string Name => "Levels";
    }

    private sealed class FakeGpuEffectWithInfo : PaintDotNet.Effects.IEffect
    {
        // Explicit, like the product GPU effects: the public
        // get_EffectInfo lookup misses and the resume must use the
        // interface map, or the history row shows the class name.
        object PaintDotNet.Effects.IEffect.get_EffectInfo() => new FakeEffectInfo();
    }

    [Fact]
    public void DefuseWaitHandle_Keeps_Events_Usable_Without_Finalizer_Crash()
    {
        // Nulls and non-handles are silent no-ops.
        CpuPaint.DefuseWaitHandle(null);
        CpuPaint.DefuseWaitHandle(new object());
        // Real events stay fully usable after defusing (Dispose still
        // releases normally); the abandoned ones simply leak instead
        // of finalize-crashing the browser PAL.
        using var mre = new System.Threading.ManualResetEvent(false);
        CpuPaint.DefuseWaitHandle(mre);
        Assert.False(mre.WaitOne(0));
        Assert.True(mre.Set());
        Assert.True(mre.WaitOne(0));
        using var are = new System.Threading.AutoResetEvent(false);
        CpuPaint.DefuseWaitHandle(are);
        Assert.True(are.Set());
        Assert.True(are.WaitOne(0));
        // Abandoned defused handles survive a full collection with no
        // finalizer behind them (live: no WaitSubsystem.DeleteHandle).
        for (int i = 0; i < 200; i++)
            CpuPaint.DefuseWaitHandle(new System.Threading.ManualResetEvent(false));
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
    }

    [Fact]
    public void ResumeEffectApply_Pushes_Named_History_Memento()
    {
        var menu = new FakeEffectMenu();
        var dialog = new FakeEffectDialog
        {
            DialogResult = 1,
            Effect = new FakeGpuEffectWithInfo(),
        };
        CpuPaint.NoteEffectMenu(dialog, menu);
        Assert.True(CpuPaint.ResumeEffectApply(dialog));
        var history = menu.AppWorkspace.ActiveDocumentWorkspace.History;
        var memento = Assert.Single(history.Pushed);
        var saved = Assert.IsType<PaintDotNet.HistoryMementos.BitmapHistoryMemento>(memento);
        // Desktop b__0 names the row from EffectInfo, not the class.
        Assert.Equal("Levels", saved.SavedName);
        // The memento guards the pre-effect snapshot over the same
        // full-surface scans handed to DoEffect (unwrapped from the
        // Result, exactly as desktop b__0 reads get_Value()).
        // DoEffect's `original` arg is a SEPARATE sacrificial clone:
        // its copy phase renders into it, so handing it the pristine
        // snapshot would contaminate the undo bytes (undo restores
        // post-effect pixels: a silent no-op). Both clones come from
        // the live layer surface but neither may alias it or share
        // with each other.
        object liveSurface = menu.AppWorkspace.ActiveDocumentWorkspace.ActiveLayer.Surface;
        Assert.NotSame(liveSurface, menu.DoEffectCalls[0][4]);
        Assert.NotSame(liveSurface, saved.SavedOriginal);
        Assert.NotSame(menu.DoEffectCalls[0][4], saved.SavedOriginal);
        var scansArg = Assert.IsType<PaintDotNet.Functional.ResultValueImpl<
            System.Collections.Generic.IReadOnlyList<PaintDotNet.Rendering.RectInt32>>>(
            menu.DoEffectCalls[0][2]);
        Assert.Same(scansArg.Value, saved.SavedScans);
        Assert.Same(scansArg, menu.DoEffectCalls[0][3]);
        Assert.Equal(0, saved.SavedLayerIndex);
    }

    [Fact]
    public void ResumeEffectApply_Finds_Base_Private_DoEffect()
    {
        // Live menus derive (AdjustmentsMenu): the private base DoEffect
        // is invisible through the derived type without a hierarchy walk
        // (observed live: resume failed with "needs EffectMenuBase.DoEffect").
        var menu = new FakeDerivedMenu();
        var dialog = new FakeEffectDialog { DialogResult = 1 };
        CpuPaint.NoteEffectMenu(dialog, menu);
        Assert.True(CpuPaint.ResumeEffectApply(dialog));
        Assert.Single(menu.DoEffectCalls);
    }

    [Fact]
    public void ResumeEffectApply_Ignores_Non_Ok_Settle()
    {
        var menu = new FakeEffectMenu();
        var dialog = new FakeEffectDialog { DialogResult = 2 };
        CpuPaint.NoteEffectMenu(dialog, menu);
        Assert.False(CpuPaint.ResumeEffectApply(dialog));
        Assert.Empty(menu.DoEffectCalls);
    }

    [Fact]
    public void ResumeEffectApply_Noop_Without_Note_Or_Shape()
    {
        var menu = new FakeEffectMenu();
        Assert.False(CpuPaint.ResumeEffectApply(null));
        Assert.False(CpuPaint.ResumeEffectApply(new FakeEffectDialog { DialogResult = 1 }));
        var anonymous = new { DialogResult = 1 };
        CpuPaint.NoteEffectMenu(anonymous, menu);
        Assert.False(CpuPaint.ResumeEffectApply(anonymous));
        Assert.Empty(menu.DoEffectCalls);
    }

    private struct FakeRgb96Float
    {
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
    }

    private sealed class FakeLevelsParams
    {
        public FakeRgb96Float InputBlackPoint { get; set; } = new FakeRgb96Float();
        public FakeRgb96Float InputWhitePoint { get; set; } = new FakeRgb96Float { R = 1, G = 1, B = 1 };
        public FakeRgb96Float Gamma { get; set; } = new FakeRgb96Float { R = 1, G = 1, B = 1 };
        public FakeRgb96Float OutputBlackPoint { get; set; } = new FakeRgb96Float();
        public FakeRgb96Float OutputWhitePoint { get; set; } = new FakeRgb96Float { R = 1, G = 1, B = 1 };
    }

    private sealed class FakeLevelsToken
    {
        public FakeLevelsParams Levels { get; } = new FakeLevelsParams();
    }

    private class FakeEffectBase
    {
        protected object? Token { get; } = null;
    }

    private sealed class FakeLevelsGpuEffect : FakeEffectBase
    {
        // Protected like the real EffectBase.Token, and shadowed with
        // new like GpuEffect<TToken>.Token: the renderer must walk
        // most-derived first (observed live: flat lookup died with
        // AmbiguousMatchException and every tile fell through).
        protected new FakeLevelsToken Token { get; } = new FakeLevelsToken();
        public FakeLevelsParams LevelsForTest => Token.Levels;
    }

    private sealed class FakeGpuEffect
    {
        public sealed class Renderer
        {
            public object? owner;
            public Renderer(object? owner) { this.owner = owner; }
        }
    }

    private sealed class FakeTile
    {
        public IntPtr Ptr { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
    }

    private sealed class FakePoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    [Fact]
    public void LevelsLut_Identity_Roundtrips()
    {
        // Oracle values computed independently (python, IEEE double):
        // the linear pipeline must be exactly invertible here.
        foreach (int c in new[] { 0, 1, 10, 64, 128, 200, 254, 255 })
            Assert.Equal(c, (int)CpuPaint.ApplyLevelsChannel(CpuPaint.SrgbByteToLinear(c), 0, 1, 1, 0, 1));
    }

    [Fact]
    public void LevelsLut_Constant_Point_Gamma_Direction_Floor_Clip()
    {
        Assert.Equal(188, (int)CpuPaint.ApplyLevelsChannel(CpuPaint.SrgbByteToLinear(30), 0, 1, 1, 0.5, 0.5));
        // Higher gamma brightens (Photoshop-compatible v1 convention).
        Assert.Equal(181, (int)CpuPaint.ApplyLevelsChannel(CpuPaint.SrgbByteToLinear(128), 0, 1, 2, 0, 1));
        Assert.Equal(61, (int)CpuPaint.ApplyLevelsChannel(CpuPaint.SrgbByteToLinear(128), 0, 1, 0.5, 0, 1));
        Assert.Equal(137, (int)CpuPaint.ApplyLevelsChannel(CpuPaint.SrgbByteToLinear(0), 0, 1, 1, 0.25, 1));
        Assert.Equal(0, (int)CpuPaint.ApplyLevelsChannel(CpuPaint.SrgbByteToLinear(64), 0.25, 1, 1, 0, 1));
    }

    [Fact]
    public void TryRenderGpuTileCpu_Rejects_NonLevels()
    {
        Assert.False(CpuPaint.TryRenderGpuTileCpu(null, null, null));
        Assert.False(CpuPaint.TryRenderGpuTileCpu(new object(), new object(), new object()));
        var blurOwner = new FakeEffectDialog();
        var blurRenderer = new FakeGpuEffect.Renderer(blurOwner);
        Assert.False(CpuPaint.TryRenderGpuTileCpu(blurRenderer, new FakeTile(), new FakePoint()));
    }

    [Fact]
    public void TryRenderGpuTileCpu_Renders_Levels_Identity_Tile()
    {
        var effect = new FakeLevelsGpuEffect();
        // 2x1 BGRA: opaque mid-gray + opaque black.
        byte[] src = new byte[] { 128, 128, 128, 255, 0, 0, 0, 255 };
        CpuPaint.NoteTileSourceBytes(effect, src, 2, 1, 8);
        byte[] dstBytes = new byte[8];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(dstBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var tile = new FakeTile { Ptr = pin.AddrOfPinnedObject(), Width = 2, Height = 1, Stride = 8 };
            Assert.True(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(effect), tile, new FakePoint()));
            Assert.Equal(128, (int)dstBytes[0]);
            Assert.Equal(0, (int)dstBytes[4]);
            Assert.Equal(255, (int)dstBytes[3]);
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void TryRenderGpuTileCpu_Renders_Levels_Constant_Tile()
    {
        var effect = new FakeLevelsGpuEffect();
        effect.LevelsForTest.OutputBlackPoint = new FakeRgb96Float { R = 0.5f, G = 0.5f, B = 0.5f };
        effect.LevelsForTest.OutputWhitePoint = new FakeRgb96Float { R = 0.5f, G = 0.5f, B = 0.5f };
        byte[] src = new byte[] { 30, 200, 90, 255, 0, 0, 0, 255 };
        CpuPaint.NoteTileSourceBytes(effect, src, 2, 1, 8);
        byte[] dstBytes = new byte[8];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(dstBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var tile = new FakeTile { Ptr = pin.AddrOfPinnedObject(), Width = 2, Height = 1, Stride = 8 };
            Assert.True(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(effect), tile, new FakePoint()));
            // Desktop derivation, not implementation echo: constant
            // sRGB-0.5 output is byte 128. (The old 188 enshrined the
            // v1 boundary bug: sRGB token points fed as linear.)
            Assert.Equal(128, (int)dstBytes[0]);
            Assert.Equal(128, (int)dstBytes[1]);
            Assert.Equal(128, (int)dstBytes[2]);
            Assert.Equal(128, (int)dstBytes[4]);
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void TryRenderGpuTileCpu_Linearizes_Srgb_Token_Points()
    {
        // Live oracle (rev123d probe): dialog white-point 100 with a
        // dark stroke remapped 85 -> ~132 under the v1 boundary bug
        // (sRGB token fed as linear). Correct linear-light math maps
        // 85 -> 220; the tile path must agree exactly.
        var effect = new FakeLevelsGpuEffect();
        float w = 100f / 255f;
        effect.LevelsForTest.InputWhitePoint = new FakeRgb96Float { R = w, G = w, B = w };
        byte[] src = new byte[] { 85, 85, 85, 255, 0, 0, 0, 255 };
        CpuPaint.NoteTileSourceBytes(effect, src, 2, 1, 8);
        byte[] dstBytes = new byte[8];
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(dstBytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var tile = new FakeTile { Ptr = pin.AddrOfPinnedObject(), Width = 2, Height = 1, Stride = 8 };
            Assert.True(CpuPaint.TryRenderGpuTileCpu(new FakeGpuEffect.Renderer(effect), tile, new FakePoint()));
            Assert.Equal(220, (int)dstBytes[0]);
            Assert.Equal(220, (int)dstBytes[1]);
            Assert.Equal(220, (int)dstBytes[2]);
            Assert.Equal(0, (int)dstBytes[4]);
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void BlitRaster_Guards_Are_Silent_Noop()
    {
        // Zero handle, null bytes and dead ink never throw: the
        // DrawTextLayout fallback owns visible behavior instead.
        CpuPaint.BlitRaster(0, new byte[4], 1, 1, 0f, 0f, 1f, 1f, 1f, 1f, 1f);
        CpuPaint.BlitRaster(0, null, 1, 1, 0f, 0f, 1f, 1f, 1f, 1f, 1f);
        var (ctx, pixels, pin) = MakeBgraTarget(2, 2);
        try
        {
            CpuPaint.BlitRaster(ctx, new byte[16], 2, 2, 0f, 0f, 0f, 1f, 1f, 1f, 0f);
            Assert.Equal(0u, PixelOf(pixels, 2, 0, 0));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }
}
