using System.Reflection;
using System.Runtime.InteropServices;
using WfWasm.PdnCpu;
using WfWasm.PdnCpu.Tests.Fakes;

namespace WfWasm.PdnCpu.Tests;

// Independent-layer semantics for the LayersStrip composite path
// (CpuPaint.DrawCompositeImage): a row whose provider overlay holds real
// pixels must paint those pixels even when the host-fed doc composite
// carries another layer's strokes. Only a genuinely empty (fully
// transparent) overlay may stand in the composite. Regression net for
// the second-layer background thumbnail defect: after stroking layer 2,
// the background row must keep showing the background layer.
public sealed unsafe class StripCompositeLayerTests
{
    // Light red: opaque but luminance 170, so the old dark-pixel
    // "has content" heuristic misclassified it as content-free and the
    // row painted the fed composite (with layer-2 strokes) instead.
    private const uint LightRed = 0xFFFF8080u;
    private const uint StrokeGreen = 0xFF008000u;

    public StripCompositeLayerTests()
    {
        // Static-feed isolation: this class is the suite's only writer
        // of the doc-composite slot; reset regardless of order.
        ResetStatic("s_docComposite", null);
        ResetStatic("s_thumbLayerW", 0);
        ResetStatic("s_thumbLayerH", 0);
    }

    private static void ResetStatic(string name, object? value)
    {
        FieldInfo? f = typeof(CpuPaint).GetField(name,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(f);
        f!.SetValue(null, value);
    }

    private static (long Ctx, byte[] Pixels, GCHandle Pin) MakeTarget(int w, int h)
    {
        var pixels = new byte[w * h * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        long ctx = CpuPaint.CreateTarget(pin.AddrOfPinnedObject(), w, h, w * 4, 4);
        return (ctx, pixels, pin);
    }

    private static long MakeOverlay(uint color, int w, int h)
    {
        uint[] px = new uint[w * h];
        Array.Fill(px, color);
        fixed (uint* p = px)
            return CpuPaint.CreateBitmap((IntPtr)p, w * 4, w, h);
    }

    private static object MixerRoot(object overlay)
    {
        var root = new object();
        CpuPaint.NoteEffectInput(root, 0, overlay);
        CpuPaint.NoteEffectInput(root, 1, new CheckerboardEffect());
        return root;
    }

    private static object OverlayOf(long handle, int w, int h)
        => new CpuDeviceBitmap { _width = w, _height = h, _handle = handle };

    // RGBA feed (R,G,B,A byte order, as DrawCompositeImage reads it).
    private static byte[] Composite44(byte r, byte g, byte b, int greenX = -1, int greenY = -1)
    {
        byte[] rgba = new byte[4 * 4 * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = 255;
        }
        if (greenX >= 0)
        {
            int o = (greenY * 4 + greenX) * 4;
            rgba[o] = 0; rgba[o + 1] = 128; rgba[o + 2] = 0; rgba[o + 3] = 255;
        }
        return rgba;
    }

    private static uint PixelOf(byte[] pixels, int w, int x, int y)
        => BitConverter.ToUInt32(pixels, (y * w + x) * 4);

    private static bool DrawRow(long ctx, object root)
        => CpuPaint.DrawCompositeImage(ctx, root,
            0f, 0f, 0f, 0f, 0,
            0f, 0f, 0f, 0f, 0);

    [Fact]
    public void LightLayerRow_PaintsOwnLayer_NotCompositeStroke()
    {
        // Doc composite: light-red background + one green layer-2 stroke
        // pixel. Row overlay: uniform light red (the background layer).
        CpuPaint.NoteDocComposite(Composite44(255, 128, 128, 3, 3), 4, 4);
        long overlay = MakeOverlay(LightRed, 2, 2);
        try
        {
            var (ctx, pixels, pin) = MakeTarget(8, 8);
            try
            {
                Assert.True(DrawRow(ctx, MixerRoot(OverlayOf(overlay, 2, 2))));
                // Darkest-of-footprint downscale would keep the green
                // stroke in cell (1,1); the row must show its own layer.
                // (No outside-rect assertion: DrawChecker's ceil-inclusive
                // edge paints a 1px fringe past the dest rect — pre-existing
                // shared behavior, out of this test's scope.)
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        Assert.Equal(LightRed, PixelOf(pixels, 8, x, y));
            }
            finally
            {
                CpuPaint.FreeContext(ctx);
                pin.Free();
            }
        }
        finally
        {
            CpuPaint.FreeBitmap(overlay);
        }
    }

    [Fact]
    public void TwoRows_PaintIndependently()
    {
        CpuPaint.NoteDocComposite(Composite44(255, 128, 128, 3, 3), 4, 4);
        long bgOverlay = MakeOverlay(LightRed, 2, 2);
        long fgOverlay = MakeOverlay(StrokeGreen, 2, 2);
        try
        {
            var (ctxA, pxA, pinA) = MakeTarget(8, 8);
            var (ctxB, pxB, pinB) = MakeTarget(8, 8);
            try
            {
                Assert.True(DrawRow(ctxA, MixerRoot(OverlayOf(bgOverlay, 2, 2))));
                Assert.True(DrawRow(ctxB, MixerRoot(OverlayOf(fgOverlay, 2, 2))));
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                    {
                        Assert.Equal(LightRed, PixelOf(pxA, 8, x, y));
                        Assert.Equal(StrokeGreen, PixelOf(pxB, 8, x, y));
                    }
            }
            finally
            {
                CpuPaint.FreeContext(ctxA);
                pinA.Free();
                CpuPaint.FreeContext(ctxB);
                pinB.Free();
            }
        }
        finally
        {
            CpuPaint.FreeBitmap(bgOverlay);
            CpuPaint.FreeBitmap(fgOverlay);
        }
    }

    [Fact]
    public void EmptyOverlay_UsesFedComposite()
    {
        // Genuinely empty (fully transparent) row overlay: the composite
        // stand-in stays (single-layer blank-doc path + deliberate
        // multi-layer approximation for not-yet-produced thumbs).
        byte[] comp = Composite44(16, 32, 48);
        CpuPaint.NoteDocComposite(comp, 4, 4);
        long overlay = MakeOverlay(0x00000000u, 2, 2);
        try
        {
            var (ctx, pixels, pin) = MakeTarget(8, 8);
            try
            {
                Assert.True(DrawRow(ctx, MixerRoot(OverlayOf(overlay, 2, 2))));
                const uint expected = 0xFF102030u;
                for (int y = 0; y < 2; y++)
                    for (int x = 0; x < 2; x++)
                        Assert.Equal(expected, PixelOf(pixels, 8, x, y));
            }
            finally
            {
                CpuPaint.FreeContext(ctx);
                pin.Free();
            }
        }
        finally
        {
            CpuPaint.FreeBitmap(overlay);
        }
    }

    [Fact]
    public void IdenticalCompositeFeed_SignalsNoChange()
    {
        // The host re-invalidates layer thumbs only when the feed
        // reports a change: a repeated identical composite (undo/redo
        // settling on the same bytes) must answer false so thumbs are
        // not needlessly repainted.
        byte[] a = Composite44(255, 128, 128);
        byte[] b = Composite44(255, 128, 128, 3, 3);
        CpuPaint.NoteDocComposite(a, 4, 4);
        Assert.True(CpuPaint.NoteDocComposite(b, 4, 4));
        Assert.False(CpuPaint.NoteDocComposite(b, 4, 4));
    }
}
