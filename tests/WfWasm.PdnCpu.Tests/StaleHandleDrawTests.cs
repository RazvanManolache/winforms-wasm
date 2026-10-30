using System.Runtime.InteropServices;
using WfWasm.PdnCpu;
using Xunit;

namespace WfWasm.PdnCpu.Tests;

// Fail-closed handle contract for CpuPaint.DrawBitmap: a bitmap handle that
// was freed (or otherwise went stale while static upload/stash tables were
// shared across tests) must be skipped silently, never throw out of the
// GCHandle deref. Pre-fix, FromHandle<Bitmap> threw here and the throw died
// in CLR EH dispatch (0x80131506)mid-suite; the same probe classes stay green
// in isolation because their tables start fresh.
public sealed unsafe class StaleHandleDrawTests
{
    private static (long Ctx, byte[] Pixels, GCHandle Pin) MakeTarget(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        long ctx = CpuPaint.CreateTarget(pin.AddrOfPinnedObject(), w, h, w * 4, 4);
        return (ctx, pixels, pin);
    }

    [Fact]
    public void DrawBitmap_With_Freed_Handle_Skips_Without_Throwing()
    {
        long bmp = CpuPaint.CreateBitmapEmpty(4, 4);
        Assert.NotEqual(0, bmp);
        CpuPaint.FreeBitmap(bmp);

        var (ctx, pixels, pin) = MakeTarget(8, 8);
        try
        {
            Exception? ex = Record.Exception(() =>
                CpuPaint.DrawBitmap(ctx, bmp, 0, 0, 4, 4, true, 0, 0, 4, 4, true, 1f, false));
            Assert.Null(ex);
            Assert.All(pixels, b => Assert.Equal(0, b));
        }
        finally
        {
            CpuPaint.FreeContext(ctx);
            pin.Free();
        }
    }
}
