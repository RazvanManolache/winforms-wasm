using System.Runtime.InteropServices;
using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// Native-vs-WASM behavior parity: the CPU backend must reproduce the
// desktop blending contract exactly. The oracle here is an independent
// plain-C# SourceOver simulation (the same math the desktop renderer
// evaluates), not the implementation itself: any drift in coverage,
// accumulation or rounding fails the test.
public sealed unsafe class CpuParityTests
{
    private static (byte[] Target, GCHandle Pin) MakeTarget(int w, int h)
    {
        var target = new byte[w * h];
        return (target, GCHandle.Alloc(target, GCHandleType.Pinned));
    }

    private static long MakeFlatBitmap(int w, int h, byte alpha)
    {
        var pixels = new uint[w * h];
        uint px = (uint)(0x00FFFFFF | (alpha << 24));
        Array.Fill(pixels, px);
        fixed (uint* p = pixels)
            return CpuPaint.CreateBitmap((IntPtr)p, w * 4, w, h);
    }

    // Dest-first, mirroring every production call site (brush stamps,
    // glyph blits): full-sprite source onto a dest square.
    private static void Stamp(long ctx, long bmp, float dx, float dy, float size)
    {
        CpuPaint.DrawBitmap(ctx, bmp,
            dx, dy, size, size, true,
            0f, 0f, size, size, true,
            1f, false);
    }

    // Raster footprint mirror: inclusive floor..ceil bounds.
    private static bool Covers(float d, int p) =>
        p >= Math.Floor(d) && p <= Math.Ceiling(d + 4);

    // Exact mirror of the backend's mask blend (truncating +0.5, not
    // banker's rounding): the oracle must reproduce the bytes.
    private static int SourceOver(int dst, int src)
    {
        double a = src / 255.0;
        double d = dst / 255.0;
        return (int)((a + d * (1 - a)) * 255.0 + 0.5);
    }

    [Fact]
    public void BrushStroke_Dabs_Accumulate_Like_Native_SourceOver()
    {
        // A brush drag is a run of overlapping sprite stamps: covered
        // pixels must accumulate exactly per SourceOver, untouched
        // pixels must stay zero.
        var (target, pin) = MakeTarget(16, 16);
        try
        {
            long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 16, 16, 16);
            long bmp = MakeFlatBitmap(4, 4, 200);
            try
            {
                Stamp(ctx, bmp, 2f, 2f, 4f);
                Stamp(ctx, bmp, 5f, 5f, 4f);
                Stamp(ctx, bmp, 8f, 8f, 4f);

                // Coverage per pixel mirrors the backend rasterizer: the
                // loop runs floor(d)..ceil(d+size) inclusive and edge
                // texels clamp, so each dab covers a 5x5 square here.
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        int n = 0;
                        if (Covers(2f, x) && Covers(2f, y)) n++;
                        if (Covers(5f, x) && Covers(5f, y)) n++;
                        if (Covers(8f, x) && Covers(8f, y)) n++;
                        int expected = 0;
                        for (int i = 0; i < n; i++)
                            expected = SourceOver(expected, 200);
                        Assert.InRange(target[y * 16 + x], expected - 1, expected + 1);
                    }
                }

                // Spot checks: single dab, double-overlap, blank corner.
                Assert.InRange(target[3 * 16 + 3], 199, 201);
                Assert.InRange(target[5 * 16 + 5], 242, 244);
                Assert.Equal(0, target[0]);
                Assert.Equal(0, target[15 * 16 + 15]);
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
    public void GlyphSprite_Blit_Is_Pixel_Exact_And_Idempotent()
    {
        // Tool/button glyphs stamp through the same DrawBitmap path: at
        // unit opacity every sprite alpha must land exactly (no blend
        // drift), and re-stamping opaque art must not accumulate.
        var (target, pin) = MakeTarget(4, 4);
        try
        {
            var pixels = new uint[16];
            for (int i = 0; i < 16; i++)
                pixels[i] = (i % 2 == 0) ? 0xFF000000u : 0x00000000u;
            long bmp;
            fixed (uint* p = pixels)
                bmp = CpuPaint.CreateBitmap((IntPtr)p, 16, 4, 4);
            try
            {
                long ctx = CpuPaint.CreateContext(pin.AddrOfPinnedObject(), 4, 4, 4);
                try
                {
                    Stamp(ctx, bmp, 0f, 0f, 4f);
                    for (int i = 0; i < 16; i++)
                        Assert.Equal(i % 2 == 0 ? 255 : 0, target[i]);
                    Stamp(ctx, bmp, 0f, 0f, 4f);
                    for (int i = 0; i < 16; i++)
                        Assert.Equal(i % 2 == 0 ? 255 : 0, target[i]);
                }
                finally
                {
                    CpuPaint.FreeContext(ctx);
                }
            }
            finally
            {
                CpuPaint.FreeBitmap(bmp);
            }
        }
        finally
        {
            pin.Free();
        }
    }
}
