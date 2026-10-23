// CPU paint backing for Paint.NET content drawing (brush stamps).
//
// Browser WASM has no Direct2D. The rewriter synthesizes a
// CpuDrawingContext (a DrawingContext subclass) into the target module and
// routes its virtuals here. Everything crossing the boundary is a primitive
// or an opaque handle (long), so this assembly never binds PDN types.
// One sanctioned exception: CreateGradientLut takes the stops collection
// as a duck-typed object, sampled through corlib reflection only.

using System.Runtime.InteropServices;

namespace WfWasm.PdnCpu;

public static unsafe class CpuPaint
{
    private sealed class Target
    {
        public byte* Ptr;
        public int Width;
        public int Height;
        public int Stride;
        // Bytes per pixel: 1 = coverage mask (alpha only), 4 = BGRA.
        public int Bpp = 1;
        // Scratch surfaces pin their managed pixel buffer; only
        // FreeScratchTarget releases the pin (FreeContext would leak it).
        public GCHandle Pin;
        public bool IsPinned;
        public float M11 = 1f;
        public float M12;
        public float M21;
        public float M22 = 1f;
        public float M31;
        public float M32;
    }

    private sealed class Bitmap
    {
        public uint[] Pixels = Array.Empty<uint>();
        public int Width;
        public int Height;
        public GCHandle Pin;
        public bool IsPinned;
    }

    private static long ToHandle(object o) => (long)GCHandle.ToIntPtr(GCHandle.Alloc(o));

    private static T FromHandle<T>(long handle) where T : class
        => (T)GCHandle.FromIntPtr((IntPtr)handle).Target!;

    private static void FreeHandle(long handle)
    {
        if (handle != 0)
            GCHandle.FromIntPtr((IntPtr)handle).Free();
    }

    public static long CreateContext(IntPtr target, int width, int height, int stride)
    {
        var t = new Target { Ptr = (byte*)target, Width = width, Height = height, Stride = stride };
        return ToHandle(t);
    }

    public static long CreateTarget(IntPtr target, int width, int height, int stride, int bpp)
    {
        if (bpp != 1 && bpp != 4)
            return 0;
        var t = new Target
        {
            Ptr = (byte*)target, Width = width, Height = height, Stride = stride, Bpp = bpp
        };
        return ToHandle(t);
    }

    public static void FreeContext(long ctx) => FreeHandle(ctx);

    // Target dimension probes for the synthesized CPU device context
    // (get_PixelSize/get_Size/get_PixelFormat). Primitives only, like the
    // rest of this surface. Unknown/zero handles answer 0 (callers treat an
    // empty size as nothing-to-draw, matching the native default path).
    public static int GetTargetWidth(long ctx) => ctx == 0 ? 0 : FromHandle<Target>(ctx).Width;
    public static int GetTargetHeight(long ctx) => ctx == 0 ? 0 : FromHandle<Target>(ctx).Height;
    public static int GetTargetStride(long ctx) => ctx == 0 ? 0 : FromHandle<Target>(ctx).Stride;
    public static int GetTargetBpp(long ctx) => ctx == 0 ? 0 : FromHandle<Target>(ctx).Bpp;

    public static void SetTransform(long ctx, float m11, float m12, float m21, float m22, float m31, float m32)
    {
        if (ctx == 0)
            return;
        var t = FromHandle<Target>(ctx);
        t.M11 = m11; t.M12 = m12; t.M21 = m21; t.M22 = m22; t.M31 = m31; t.M32 = m32;
    }

    public static void GetTransform(long ctx, out float m11, out float m12, out float m21, out float m22, out float m31, out float m32)
    {
        if (ctx == 0)
        {
            m11 = 1f; m12 = 0f; m21 = 0f; m22 = 1f; m31 = 0f; m32 = 0f;
            return;
        }
        var t = FromHandle<Target>(ctx);
        m11 = t.M11; m12 = t.M12; m21 = t.M21; m22 = t.M22; m31 = t.M31; m32 = t.M32;
    }

    public static long CreateBitmap(IntPtr src, int stride, int width, int height)
    {
        if (src == IntPtr.Zero || width <= 0 || height <= 0)
            return 0;
        var pixels = new uint[width * height];
        fixed (uint* dst = pixels)
        {
            byte* d = (byte*)dst;
            byte* s = (byte*)src;
            for (int y = 0; y < height; y++)
                Buffer.MemoryCopy(s + (nint)y * stride, d + (nint)y * width * 4, width * 4, width * 4);
        }
        return ToHandle(new Bitmap { Pixels = pixels, Width = width, Height = height });
    }

    // Uploads a 64-bit Half lock as a shared CPU bitmap (Half ->
    // Bgra32, then the packed 32-bit upload). Lets ImageStrip item
    // images (layer/history thumbnails) display instead of throwing
    // "no headless backend (pixel format)".
    public static long UploadHalfSharedBitmap(IntPtr buf, int stride, int w, int h)
    {
        if (buf == IntPtr.Zero || w <= 0 || h <= 0 || stride < w * 8)
            return 0;
        byte[] half = new byte[checked(stride * h)];
        Marshal.Copy(buf, half, 0, half.Length);
        byte[] bgra = ThumbHalfToBgra32Bytes(half, stride, w, h);
        GCHandle pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            return CreateBitmap(pin.AddrOfPinnedObject(), checked(w * 4), w, h);
        }
        finally
        {
            pin.Free();
        }
    }

    public static long CreateBitmapEmpty(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return 0;
        return ToHandle(new Bitmap { Pixels = new uint[width * height], Width = width, Height = height });
    }

    public static void FreeBitmap(long bmp)
    {
        if (bmp == 0)
            return;
        var b = FromHandle<Bitmap>(bmp);
        if (b.IsPinned)
        {
            b.Pin.Free();
            b.IsPinned = false;
        }
        FreeHandle(bmp);
    }

    // Command-list redirect scratch: a pinned managed BGRA surface
    // collecting the fills issued while a command list is the dc
    // target (headless SetTarget redirect). DrawImage(list) blits it
    // back, Dispose frees it. Identity matrix on creation; the
    // SetTarget rewrite copies the live matrix in (GetTransform /
    // SetTransform) so redirected fills map exactly like direct ones.
    public static long CreateScratchTarget(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return 0;
        }
        var pixels = new uint[width * height];
        var gch = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        var t = new Target
        {
            Ptr = (byte*)gch.AddrOfPinnedObject().ToPointer(),
            Width = width,
            Height = height,
            Stride = width * 4,
            Bpp = 4,
            Pin = gch,
            IsPinned = true
        };
        long h = ToHandle(t);
        return h;
    }

    // SourceOver blit of a scratch surface onto a BGRA target
    // (identity mapping; same-size by construction, clipped to the
    // overlap). Non-BGRA targets skip: mask/scratch mismatches stay
    // inert rather than mispaint.
    public static void BlitScratchOver(long dst, long src)
    {
        if (dst == 0 || src == 0)
        {
            return;
        }
        var d = FromHandle<Target>(dst);
        var s = FromHandle<Target>(src);
        if (d.Bpp != 4 || s.Bpp != 4)
        {
            return;
        }
        int w = Math.Min(d.Width, s.Width);
        int h = Math.Min(d.Height, s.Height);
        for (int y = 0; y < h; y++)
        {
            uint* srow = (uint*)(s.Ptr + (nint)y * s.Stride);
            for (int x = 0; x < w; x++)
            {
                uint p = srow[x];
                float sa = ((p >> 24) & 0xFF) / 255f;
                if (sa <= 0f)
                    continue;
                BlendPixelBgra(d, x, y, p, sa);
            }
        }
    }

    public static void FreeScratchTarget(long scratch)
    {
        if (scratch == 0)
            return;
        var t = FromHandle<Target>(scratch);
        if (t.IsPinned)
        {
            t.Pin.Free();
            t.IsPinned = false;
        }
        FreeHandle(scratch);
    }

    // Pins the pixel buffer and answers its address so PDN source
    // pixels can be uploaded with IBitmapSource.CopyPixels. Every
    // LockBitmap needs exactly one UnlockBitmap.
    public static IntPtr LockBitmap(long bmp)
    {
        if (bmp == 0)
            return IntPtr.Zero;
        var b = FromHandle<Bitmap>(bmp);
        if (!b.IsPinned)
        {
            b.Pin = GCHandle.Alloc(b.Pixels, GCHandleType.Pinned);
            b.IsPinned = true;
        }
        return b.Pin.AddrOfPinnedObject();
    }

    public static void UnlockBitmap(long bmp)
    {
        if (bmp == 0)
            return;
        var b = FromHandle<Bitmap>(bmp);
        if (b.IsPinned)
        {
            b.Pin.Free();
            b.IsPinned = false;
        }
    }

    // Row-copy download into a caller buffer (stride may differ from
    // the packed width*4): EndDraw/Dispose write-back for FromBitmap
    // contexts uses this so panel/preview pixels reach the source.
    public static void DownloadBitmap(long bmp, IntPtr dst, int dstStride)
    {
        if (bmp == 0 || dst == IntPtr.Zero || dstStride <= 0)
            return;
        var b = FromHandle<Bitmap>(bmp);
        int w = b.Width, h = b.Height;
        if (w <= 0 || h <= 0 || b.Pixels.Length < w * h)
            return;
        int rowBytes = w * 4;
        int copy = Math.Min(rowBytes, dstStride);
        fixed (uint* src = b.Pixels)
        {
            byte* s = (byte*)src;
            byte* d = (byte*)dst;
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy(s + (nint)y * rowBytes, d + (nint)y * dstStride, copy, copy);
        }
    }

    public static int BitmapWidth(long bmp) => FromHandle<Bitmap>(bmp).Width;

    public static int BitmapHeight(long bmp) => FromHandle<Bitmap>(bmp).Height;

    private static uint SampleNearest(Bitmap b, float x, float y)
    {
        int ix = Math.Clamp((int)MathF.Floor(x), 0, b.Width - 1);
        int iy = Math.Clamp((int)MathF.Floor(y), 0, b.Height - 1);
        return b.Pixels[iy * b.Width + ix];
    }

    private static uint SampleBilinear(Bitmap b, float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;
        uint BlendChannel(uint c00, uint c10, uint c01, uint c11, int shift)
        {
            float a = (c00 >> shift) & 0xFF;
            float bv = (c10 >> shift) & 0xFF;
            float c = (c01 >> shift) & 0xFF;
            float d = (c11 >> shift) & 0xFF;
            float top = a + (bv - a) * fx;
            float bot = c + (d - c) * fx;
            return (uint)(top + (bot - top) * fy + 0.5f);
        }
        uint p00 = SampleNearest(b, x0, y0);
        uint p10 = SampleNearest(b, x0 + 1, y0);
        uint p01 = SampleNearest(b, x0, y0 + 1);
        uint p11 = SampleNearest(b, x0 + 1, y0 + 1);
        // BGRA memory order: B=bits0-7, G=8-15, R=16-23, A=24-31.
        return BlendChannel(p00, p10, p01, p11, 0)
            | (BlendChannel(p00, p10, p01, p11, 8) << 8)
            | (BlendChannel(p00, p10, p01, p11, 16) << 16)
            | (BlendChannel(p00, p10, p01, p11, 24) << 24);
    }

    public static void DrawBitmap(
        long ctx,
        long bmp,
        float dx, float dy, float dw, float dh, bool hasDest,
        float sx, float sy, float sw, float sh, bool hasSrc,
        float opacity, bool bilinear)
    {
        if (ctx == 0 || bmp == 0)
            return;
        var t = FromHandle<Target>(ctx);
        var b = FromHandle<Bitmap>(bmp);
        if (opacity <= 0f || b.Width <= 0 || b.Height <= 0)
            return;

        float destX = hasDest ? dx : 0f;
        float destY = hasDest ? dy : 0f;
        float destW = hasDest ? dw : b.Width;
        float destH = hasDest ? dh : b.Height;
        float srcX = hasSrc ? sx : 0f;
        float srcY = hasSrc ? sy : 0f;
        float srcW = hasSrc ? sw : b.Width;
        float srcH = hasSrc ? sh : b.Height;
        if (destW <= 0f || destH <= 0f || srcW <= 0f || srcH <= 0f)
            return;

        float det = t.M11 * t.M22 - t.M12 * t.M21;
        if (det == 0f)
            return;

        float x0 = destX * t.M11 + destY * t.M21 + t.M31;
        float y0 = destX * t.M12 + destY * t.M22 + t.M32;
        float x1 = (destX + destW) * t.M11 + (destY + destH) * t.M21 + t.M31;
        float y1 = (destX + destW) * t.M12 + (destY + destH) * t.M22 + t.M32;
        int ix0 = Math.Max(0, (int)MathF.Floor(Math.Min(x0, x1)));
        int iy0 = Math.Max(0, (int)MathF.Floor(Math.Min(y0, y1)));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(Math.Max(x0, x1)));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(Math.Max(y0, y1)));

        for (int y = iy0; y <= iy1; y++)
        {
            for (int x = ix0; x <= ix1; x++)
            {
                float lx = ((x + 0.5f - t.M31) * t.M22 - (y + 0.5f - t.M32) * t.M21) / det;
                float ly = ((y + 0.5f - t.M32) * t.M11 - (x + 0.5f - t.M31) * t.M12) / det;
                float px = srcX + (lx - destX) * (srcW / destW);
                float py = srcY + (ly - destY) * (srcH / destH);
                uint s = bilinear ? SampleBilinear(b, px, py) : SampleNearest(b, px, py);
                float sa = (((s >> 24) & 0xFF) / 255f) * opacity;
                if (sa <= 0f)
                    continue;
                if (t.Bpp == 4)
                    BlendPixelBgra(t, x, y, s, sa);
                else
                    BlendPixelMask(t, x, y, sa);
            }
        }
    }

    private static void BlendPixelMask(Target t, int x, int y, float alpha)
    {
        if ((uint)x >= (uint)t.Width || (uint)y >= (uint)t.Height || alpha <= 0f)
            return;
        if (alpha > 1f)
            alpha = 1f;
        byte* row = t.Ptr + (nint)y * t.Stride;
        float d = row[x] / 255f;
        row[x] = (byte)Math.Clamp((int)((alpha + d * (1f - alpha)) * 255f + 0.5f), 0, 255);
    }

    // Straight-alpha source-over onto a BGRA target. Brush stamps are
    // white, so the channels converge; the formula is general.
    private static void BlendPixelBgra(Target t, int x, int y, uint src, float alpha)
    {
        if ((uint)x >= (uint)t.Width || (uint)y >= (uint)t.Height || alpha <= 0f)
            return;
        if (alpha > 1f)
            alpha = 1f;
        uint* row = (uint*)(t.Ptr + (nint)y * t.Stride);
        uint d = row[x];
        float sb = (src & 0xFF) / 255f;
        float sg = ((src >> 8) & 0xFF) / 255f;
        float sr = ((src >> 16) & 0xFF) / 255f;
        float db = (d & 0xFF) / 255f;
        float dg = ((d >> 8) & 0xFF) / 255f;
        float dr = ((d >> 16) & 0xFF) / 255f;
        float da = ((d >> 24) & 0xFF) / 255f;
        float inv = 1f - alpha;
        uint ob = (uint)Math.Clamp((int)((sb * alpha + db * inv) * 255f + 0.5f), 0, 255);
        uint og = (uint)Math.Clamp((int)((sg * alpha + dg * inv) * 255f + 0.5f), 0, 255);
        uint orr = (uint)Math.Clamp((int)((sr * alpha + dr * inv) * 255f + 0.5f), 0, 255);
        uint oa = (uint)Math.Clamp((int)((alpha + da * inv) * 255f + 0.5f), 0, 255);
        row[x] = ob | (og << 8) | (orr << 16) | (oa << 24);
    }

    private static void BlendPixel(Target t, int x, int y, float alpha, float r, float g, float b)
    {
        if (t.Bpp == 4)
        {
            // BGRA byte order: low byte is blue.
            uint bb = (uint)Math.Clamp((int)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f), 0, 255);
            uint gg = (uint)Math.Clamp((int)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f), 0, 255);
            uint rr = (uint)Math.Clamp((int)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f), 0, 255);
            BlendPixelBgra(t, x, y, bb | (gg << 8) | (rr << 16) | (255u << 24), alpha);
        }
        else
        {
            BlendPixelMask(t, x, y, alpha);
        }
    }

    // In-place straight<->premultiplied conversion over a BGRA buffer.
    // Alpha is exact; rounded channels can drift by a unit from WIC.
    public static void PremultiplyInPlace(IntPtr buf, int width, int height, int stride)
    {
        if (buf == IntPtr.Zero || width <= 0 || height <= 0 || stride < width * 4)
            return;
        byte* b = (byte*)buf;
        for (int y = 0; y < height; y++)
        {
            byte* row = b + (nint)y * stride;
            for (int x = 0; x < width; x++)
            {
                byte a = row[x * 4 + 3];
                row[x * 4 + 0] = (byte)((row[x * 4 + 0] * a + 127) / 255);
                row[x * 4 + 1] = (byte)((row[x * 4 + 1] * a + 127) / 255);
                row[x * 4 + 2] = (byte)((row[x * 4 + 2] * a + 127) / 255);
            }
        }
    }

    public static void UnpremultiplyInPlace(IntPtr buf, int width, int height, int stride)
    {
        if (buf == IntPtr.Zero || width <= 0 || height <= 0 || stride < width * 4)
            return;
        byte* b = (byte*)buf;
        for (int y = 0; y < height; y++)
        {
            byte* row = b + (nint)y * stride;
            for (int x = 0; x < width; x++)
            {
                byte a = row[x * 4 + 3];
                if (a == 0)
                {
                    row[x * 4 + 0] = row[x * 4 + 1] = row[x * 4 + 2] = 0;
                    continue;
                }
                row[x * 4 + 0] = (byte)Math.Min(255, (row[x * 4 + 0] * 255 + a / 2) / a);
                row[x * 4 + 1] = (byte)Math.Min(255, (row[x * 4 + 1] * 255 + a / 2) / a);
                row[x * 4 + 2] = (byte)Math.Min(255, (row[x * 4 + 2] * 255 + a / 2) / a);
            }
        }
    }

    // Straight BGRA bytes to premultiplied-float RGBA (ColorPrgba128Float
    // layout: r, g, b, a singles). Pure channel math like WIC without
    // color contexts: no gamma curve, dither parameters ignored.
    public static void ConvertBgra32ToPrgba128Float(
        IntPtr src, int srcStride, IntPtr dst, int dstStride, int width, int height)
    {
        if (src == IntPtr.Zero || dst == IntPtr.Zero
            || width <= 0 || height <= 0
            || srcStride < width * 4 || dstStride < width * 16)
            return;
        byte* s = (byte*)src;
        byte* d = (byte*)dst;
        for (int y = 0; y < height; y++)
        {
            byte* srow = s + (nint)y * srcStride;
            float* drow = (float*)(d + (nint)y * dstStride);
            for (int x = 0; x < width; x++)
            {
                float a = srow[x * 4 + 3] / 255f;
                drow[x * 4 + 0] = (srow[x * 4 + 2] / 255f) * a;
                drow[x * 4 + 1] = (srow[x * 4 + 1] / 255f) * a;
                drow[x * 4 + 2] = (srow[x * 4 + 0] / 255f) * a;
                drow[x * 4 + 3] = a;
            }
        }
    }

    // Premultiplied BGRA bytes to premultiplied-float RGBA: scale only.
    public static void ConvertPbgra32ToPrgba128Float(
        IntPtr src, int srcStride, IntPtr dst, int dstStride, int width, int height)
    {
        if (src == IntPtr.Zero || dst == IntPtr.Zero
            || width <= 0 || height <= 0
            || srcStride < width * 4 || dstStride < width * 16)
            return;
        byte* s = (byte*)src;
        byte* d = (byte*)dst;
        for (int y = 0; y < height; y++)
        {
            byte* srow = s + (nint)y * srcStride;
            float* drow = (float*)(d + (nint)y * dstStride);
            for (int x = 0; x < width; x++)
            {
                drow[x * 4 + 0] = srow[x * 4 + 2] / 255f;
                drow[x * 4 + 1] = srow[x * 4 + 1] / 255f;
                drow[x * 4 + 2] = srow[x * 4 + 0] / 255f;
                drow[x * 4 + 3] = srow[x * 4 + 3] / 255f;
            }
        }
    }

    // Premultiplied-float RGBA to premultiplied BGRA bytes (brush
    // sprite download: float mip levels convert to Pbgra32 for
    // DrawBitmap). Clamp + round to nearest, like WIC without color
    // contexts; alpha exact up to byte rounding.
    public static void ConvertPrgba128FloatToPbgra32(
        IntPtr src, int srcStride, IntPtr dst, int dstStride, int width, int height)
    {
        if (src == IntPtr.Zero || dst == IntPtr.Zero
            || width <= 0 || height <= 0
            || srcStride < width * 16 || dstStride < width * 4)
            return;
        byte* s = (byte*)src;
        byte* d = (byte*)dst;
        for (int y = 0; y < height; y++)
        {
            float* srow = (float*)(s + (nint)y * srcStride);
            byte* drow = d + (nint)y * dstStride;
            for (int x = 0; x < width; x++)
            {
                float r = Math.Clamp(srow[x * 4 + 0], 0f, 1f);
                float g = Math.Clamp(srow[x * 4 + 1], 0f, 1f);
                float b = Math.Clamp(srow[x * 4 + 2], 0f, 1f);
                float a = Math.Clamp(srow[x * 4 + 3], 0f, 1f);
                drow[x * 4 + 0] = (byte)(b * 255f + 0.5f);
                drow[x * 4 + 1] = (byte)(g * 255f + 0.5f);
                drow[x * 4 + 2] = (byte)(r * 255f + 0.5f);
                drow[x * 4 + 3] = (byte)(a * 255f + 0.5f);
            }
        }
    }

    // Straight BGRA bytes to premultiplied-half RGBA (ColorPrgba64Half
    // layout: r, g, b, a halves). Pure channel math like WIC without
    // color contexts: no gamma curve. System.Half carries the
    // float-to-half rounding (banker's, like WIC's codec path).
    public static void ConvertBgra32ToPrgba64Half(
        IntPtr src, int srcStride, IntPtr dst, int dstStride, int width, int height)
    {
        if (src == IntPtr.Zero || dst == IntPtr.Zero
            || width <= 0 || height <= 0
            || srcStride < width * 4 || dstStride < width * 8)
            return;
        byte* s = (byte*)src;
        byte* d = (byte*)dst;
        for (int y = 0; y < height; y++)
        {
            byte* srow = s + (nint)y * srcStride;
            Half* drow = (Half*)(d + (nint)y * dstStride);
            for (int x = 0; x < width; x++)
            {
                float a = srow[x * 4 + 3] / 255f;
                drow[x * 4 + 0] = (Half)((srow[x * 4 + 2] / 255f) * a);
                drow[x * 4 + 1] = (Half)((srow[x * 4 + 1] / 255f) * a);
                drow[x * 4 + 2] = (Half)((srow[x * 4 + 0] / 255f) * a);
                drow[x * 4 + 3] = (Half)a;
            }
        }
    }

    // Premultiplied BGRA bytes to premultiplied-half RGBA: scale only.
    public static void ConvertPbgra32ToPrgba64Half(
        IntPtr src, int srcStride, IntPtr dst, int dstStride, int width, int height)
    {
        if (src == IntPtr.Zero || dst == IntPtr.Zero
            || width <= 0 || height <= 0
            || srcStride < width * 4 || dstStride < width * 8)
            return;
        byte* s = (byte*)src;
        byte* d = (byte*)dst;
        for (int y = 0; y < height; y++)
        {
            byte* srow = s + (nint)y * srcStride;
            Half* drow = (Half*)(d + (nint)y * dstStride);
            for (int x = 0; x < width; x++)
            {
                drow[x * 4 + 0] = (Half)(srow[x * 4 + 2] / 255f);
                drow[x * 4 + 1] = (Half)(srow[x * 4 + 1] / 255f);
                drow[x * 4 + 2] = (Half)(srow[x * 4 + 0] / 255f);
                drow[x * 4 + 3] = (Half)(srow[x * 4 + 3] / 255f);
            }
        }
    }

    public static void ClearTarget(long ctx, float r, float g, float b, float a, bool hasColor)
    {
        if (ctx == 0)
            return;
        var t = FromHandle<Target>(ctx);
        if (!hasColor)
            r = g = b = a = 0f;
        if (t.Bpp == 4)
        {
            float clA = Math.Clamp(a, 0f, 1f);
            uint pa = (uint)(clA * 255f + 0.5f);
            // Straight input into the premultiplied target.
            uint pr = (uint)(Math.Clamp(r, 0f, 1f) * clA * 255f + 0.5f);
            uint pg = (uint)(Math.Clamp(g, 0f, 1f) * clA * 255f + 0.5f);
            uint pb = (uint)(Math.Clamp(b, 0f, 1f) * clA * 255f + 0.5f);
            uint px = pb | (pg << 8) | (pr << 16) | (pa << 24);
            unsafe
            {
                uint* d = (uint*)t.Ptr;
                for (int y = 0; y < t.Height; y++)
                {
                    uint* row = (uint*)((byte*)d + (nint)y * t.Stride);
                    for (int x = 0; x < t.Width; x++)
                        row[x] = px;
                }
            }
        }
        else
        {
            byte cov = (byte)(Math.Clamp(a, 0f, 1f) * 255f + 0.5f);
            unsafe
            {
                byte* d = (byte*)t.Ptr;
                for (int y = 0; y < t.Height; y++)
                {
                    byte* row = d + (nint)y * t.Stride;
                    for (int x = 0; x < t.Width; x++)
                        row[x] = cov;
                }
            }
        }
    }

    public static void FillRect(
        long ctx, float x, float y, float w, float h,
        float opacity, float r, float g, float b, float a)
    {
        if (ctx == 0 || opacity <= 0f || w <= 0f || h <= 0f)
            return;
        FillRectT(FromHandle<Target>(ctx), x, y, w, h, opacity, r, g, b, a);
    }

    private static void FillRectT(
        Target t, float x, float y, float w, float h,
        float opacity, float r, float g, float b, float a)
    {
        // Mask targets (bpp 1) are alpha-only; BGRA targets blend the
        // full color.
        float alpha = Math.Clamp(a, 0f, 1f) * opacity;
        float x0 = x * t.M11 + y * t.M21 + t.M31;
        float y0 = x * t.M12 + y * t.M22 + t.M32;
        float x1 = (x + w) * t.M11 + (y + h) * t.M21 + t.M31;
        float y1 = (x + w) * t.M12 + (y + h) * t.M22 + t.M32;
        int ix0 = Math.Max(0, (int)MathF.Floor(Math.Min(x0, x1)));
        int iy0 = Math.Max(0, (int)MathF.Floor(Math.Min(y0, y1)));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(Math.Max(x0, x1)));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(Math.Max(y0, y1)));
        for (int yy = iy0; yy <= iy1; yy++)
            for (int xx = ix0; xx <= ix1; xx++)
                BlendPixel(t, xx, yy, alpha, r, g, b);
    }

    public static void FillEllipse(
        long ctx, float cx, float cy, float rx, float ry,
        float opacity, float r, float g, float b, float a)
    {
        if (ctx == 0 || opacity <= 0f || rx <= 0f || ry <= 0f)
            return;
        FillEllipseT(FromHandle<Target>(ctx), cx, cy, rx, ry, opacity, r, g, b, a);
    }

    private static void FillEllipseT(
        Target t, float cx, float cy, float rx, float ry,
        float opacity, float r, float g, float b, float a)
    {
        float alpha = Math.Clamp(a, 0f, 1f) * opacity;
        // Device-space center and radii (translation + axis scale; rotation
        // beyond that is out of scope for brush stamps, which translate).
        float dcx = cx * t.M11 + cy * t.M21 + t.M31;
        float dcy = cx * t.M12 + cy * t.M22 + t.M32;
        float drx = MathF.Abs(rx * t.M11) + MathF.Abs(ry * t.M21);
        float dry = MathF.Abs(rx * t.M12) + MathF.Abs(ry * t.M22);
        int ix0 = Math.Max(0, (int)MathF.Floor(dcx - drx - 1f));
        int iy0 = Math.Max(0, (int)MathF.Floor(dcy - dry - 1f));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(dcx + drx + 1f));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(dcy + dry + 1f));
        for (int yy = iy0; yy <= iy1; yy++)
        {
            for (int xx = ix0; xx <= ix1; xx++)
            {
                float nx = (xx + 0.5f - dcx) / drx;
                float ny = (yy + 0.5f - dcy) / dry;
                float q = nx * nx + ny * ny;
                if (q >= 1.3225f) // outside the 1px AA rim (1.15^2)
                    continue;
                float cover = q <= 1f ? 1f : 1f - (MathF.Sqrt(q) - 1f) / 0.15f;
                BlendPixel(t, xx, yy, alpha * cover, r, g, b);
            }
        }
    }

    // Vector-geometry fill for icon/cursor rasterization (Shape image
    // resources): the D2D geometry backend is absent headless, so fills
    // run on the CPU. The provider is duck-typed by getter shape
    // (reflection-only, no PDN references): path figures, rect,
    // ellipse center, or a wrapped inner geometry. Coordinates are
    // DIPs; the target transform maps them to device pixels.
    public static void FillGeometry(
        long ctx, object? geoProvider, float opacity, float r, float g, float b, float a)
    {
        if (ctx == 0 || geoProvider is null || opacity <= 0f || a <= 0f)
            return;
        var t = FromHandle<Target>(ctx);
        FillGeometryProvider(t, geoProvider, Math.Clamp(a, 0f, 1f) * opacity, r, g, b);
    }

    private static void FillGeometryProvider(
        Target t, object provider, float alpha, float r, float g, float b)
    {
        // The fill rule ranges over every figure's edges at once
        // (nested rings punch holes), so batch all loops into one
        // scan fill rather than painting figure by figure.
        int rule = 0;
        var fr = provider.GetType().GetMethod("get_FillRule", Type.EmptyTypes);
        if (fr is not null)
            rule = Convert.ToInt32(fr.Invoke(provider, null));
        // Boolean (combined) geometries flatten each side, then
        // combine: union/xor concatenate, intersect/exclude clip.
        Type ptype = provider.GetType();
        var g1m = ptype.GetMethod("get_Geometry1", Type.EmptyTypes);
        var g2m = ptype.GetMethod("get_Geometry2", Type.EmptyTypes);
        if (g1m is not null && g2m is not null)
        {
            FillCombined(t, provider, g1m, g2m, alpha, r, g, b);
            return;
        }
        var loops = FlattenProvider(provider);
        if (loops.Count > 0)
            ScanFillLoops(t, loops, rule, alpha, r, g, b);
    }

    private static List<List<(double X, double Y)>> FlattenChild(object? geo)
    {
        if (geo is null)
            return new List<List<(double X, double Y)>>();
        try
        {
            return FlattenProvider(geo);
        }
        catch (NotSupportedException ex)
        {
            throw new NotSupportedException(
                "CpuPaint combine child has no headless backend: " + geo.GetType().FullName, ex);
        }
    }
    // Boolean geometry combine (radio nubs): WPF mode numbers
    // (Union=0, Intersect=1, Xor=2, Exclude=3). Sides pre-flatten
    // once into a predicate tree, so nested combines evaluate
    // exactly; taps use the same 2x2 coverage so edges match
    // plain fills.
    private abstract class GeoNode { }

    private sealed class LeafNode : GeoNode
    {
        public List<(float X, float Y)> Dev = new();
        public List<int> Starts = new();
        public int Rule;
        public float MinX = float.MaxValue, MinY = float.MaxValue;
        public float MaxX = float.MinValue, MaxY = float.MinValue;
    }

    private sealed class CombineNode : GeoNode
    {
        public GeoNode? Left;
        public GeoNode? Right;
        public int Mode;
    }

    private static GeoNode? BuildNode(object? geo, Target t, int inheritedRule)
    {
        if (geo is null)
            return null;
        Type gt = geo.GetType();
        var g1m = gt.GetMethod("get_Geometry1", Type.EmptyTypes);
        var g2m = gt.GetMethod("get_Geometry2", Type.EmptyTypes);
        if (g1m is not null && g2m is not null)
        {
            int crule = inheritedRule;
            var fr = gt.GetMethod("get_FillRule", Type.EmptyTypes);
            if (fr is not null)
                crule = Convert.ToInt32(fr.Invoke(geo, null));
            return new CombineNode
            {
                Left = BuildNode(g1m.Invoke(geo, null), t, crule),
                Right = BuildNode(g2m.Invoke(geo, null), t, crule),
                Mode = ReadCombineMode(geo),
            };
        }
        return LeafFromLoops(FlattenChild(geo), t, inheritedRule);
    }

    private static LeafNode LeafFromLoops(List<List<(double X, double Y)>> loops, Target t, int rule)
    {
        var leaf = new LeafNode { Rule = rule };
        foreach (var loop in loops)
        {
            leaf.Starts.Add(leaf.Dev.Count);
            foreach (var (lx, ly) in loop)
            {
                float dx = (float)(lx * t.M11 + ly * t.M21 + t.M31);
                float dy = (float)(lx * t.M12 + ly * t.M22 + t.M32);
                leaf.Dev.Add((dx, dy));
                if (dx < leaf.MinX) leaf.MinX = dx;
                if (dy < leaf.MinY) leaf.MinY = dy;
                if (dx > leaf.MaxX) leaf.MaxX = dx;
                if (dy > leaf.MaxY) leaf.MaxY = dy;
            }
        }
        return leaf;
    }

    private static bool EvalNode(GeoNode? node, float x, float y)
    {
        if (node is null)
            return false;
        if (node is LeafNode leaf)
            return PointInPoly(leaf.Dev, leaf.Starts, x, y, leaf.Rule);
        var c = (CombineNode)node;
        bool in1 = EvalNode(c.Left, x, y);
        bool in2 = EvalNode(c.Right, x, y);
        return c.Mode == 0 ? (in1 || in2)
            : c.Mode == 1 ? (in1 && in2)
            : c.Mode == 2 ? (in1 ^ in2)
            : (in1 && !in2);
    }

    private static void FillCombined(
        Target t, object provider,
        System.Reflection.MethodInfo g1m, System.Reflection.MethodInfo g2m,
        float alpha, float r, float g, float b)
    {
        int rule = 0;
        var fr = provider.GetType().GetMethod("get_FillRule", Type.EmptyTypes);
        if (fr is not null)
            rule = Convert.ToInt32(fr.Invoke(provider, null));
        GeoNode root = new CombineNode
        {
            Left = BuildNode(g1m.Invoke(provider, null), t, rule),
            Right = BuildNode(g2m.Invoke(provider, null), t, rule),
            Mode = ReadCombineMode(provider),
        };
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        UnionBox(root, ref minX, ref minY, ref maxX, ref maxY);
        if (minX > maxX || minY > maxY)
            return;
        int ix0 = Math.Max(0, (int)MathF.Floor(minX));
        int iy0 = Math.Max(0, (int)MathF.Floor(minY));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(maxX));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(maxY));
        (float OX, float OY)[] taps = [(0.25f, 0.25f), (0.75f, 0.25f), (0.25f, 0.75f), (0.75f, 0.75f)];
        for (int yy = iy0; yy <= iy1; yy++)
            for (int xx = ix0; xx <= ix1; xx++)
            {
                int cover = 0;
                foreach (var (ox, oy) in taps)
                    if (EvalNode(root, xx + ox, yy + oy))
                        cover++;
                if (cover > 0)
                    BlendPixel(t, xx, yy, alpha * cover / 4f, r, g, b);
            }
    }

    private static int ReadCombineMode(object provider)
    {
        var mm = provider.GetType().GetMethod("get_GeometryCombineMode", Type.EmptyTypes);
        return mm is null ? 0 : Convert.ToInt32(mm.Invoke(provider, null));
    }

    private static void UnionBox(GeoNode? node, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        if (node is LeafNode leaf)
        {
            if (leaf.Dev.Count == 0)
                return;
            if (leaf.MinX < minX) minX = leaf.MinX;
            if (leaf.MinY < minY) minY = leaf.MinY;
            if (leaf.MaxX > maxX) maxX = leaf.MaxX;
            if (leaf.MaxY > maxY) maxY = leaf.MaxY;
        }
        else if (node is CombineNode c)
        {
            UnionBox(c.Left, ref minX, ref minY, ref maxX, ref maxY);
            UnionBox(c.Right, ref minX, ref minY, ref maxX, ref maxY);
        }
    }

    // D2D fill closes open figures implicitly; unfilled figures
    // contribute no edges. Returns fewer than 3 points when empty.
    private static List<(double X, double Y)> BuildLoop(object fig)
    {
        var loop = new List<(double X, double Y)>();
        if (!GetBool(fig, "get_IsFilled"))
            return loop;
        (double cx, double cy) = ReadPoint(fig, "get_StartPoint");
        loop.Add((cx, cy));
        // Segments duck-type by getter shape (Point3 = cubic,
        // Point2 = quadratic, Size = arc, Point = line), mirroring
        // the provider dispatch above.
        Type[] empty = Type.EmptyTypes;
        foreach (object seg in (System.Collections.IEnumerable)
            fig.GetType().GetMethod("get_Segments", Type.EmptyTypes)!.Invoke(fig, null)!)
        {
            Type st = seg.GetType();
            if (st.GetMethod("get_Point3", empty) is not null)
            {
                var b3 = ReadPoint(seg, "get_Point3");
                FlattenCubic(loop, cx, cy,
                    ReadPoint(seg, "get_Point1"), ReadPoint(seg, "get_Point2"), b3);
                (cx, cy) = b3;
            }
            else if (st.GetMethod("get_Point2", empty) is not null)
            {
                var q2 = ReadPoint(seg, "get_Point2");
                FlattenQuad(loop, cx, cy, ReadPoint(seg, "get_Point1"), q2);
                (cx, cy) = q2;
            }
            else if (st.GetMethod("get_Size", empty) is not null)
            {
                (cx, cy) = FlattenArc(loop, cx, cy, seg);
            }
            else if (st.GetMethod("get_Points", empty) is not null)
            {
                // PolyLineSegment: a point run continuing the loop.
                foreach (object pt in (System.Collections.IEnumerable)
                    st.GetMethod("get_Points", empty)!.Invoke(seg, null)!)
                {
                    (cx, cy) = (GetDouble(pt, "get_X"), GetDouble(pt, "get_Y"));
                    loop.Add((cx, cy));
                }
            }
            else if (st.GetMethod("get_Point", empty) is not null)
            {
                (cx, cy) = ReadPoint(seg, "get_Point");
                loop.Add((cx, cy));
            }
            else
            {
                throw new NotSupportedException(
                    "CpuPaint.FillFigure has no headless backend for segment "
                    + st.FullName);
            }
        }
        return loop;
    }

    private static (double X, double Y) ReadPoint(object o, string getter)
    {
        object p = o.GetType().GetMethod(getter, Type.EmptyTypes)!.Invoke(o, null)!;
        return (GetDouble(p, "get_X"), GetDouble(p, "get_Y"));
    }

    private static double GetDouble(object o, string getter)
        => Convert.ToDouble(o.GetType().GetMethod(getter, Type.EmptyTypes)!.Invoke(o, null));

    private static bool GetBool(object o, string getter)
        => Convert.ToBoolean(o.GetType().GetMethod(getter, Type.EmptyTypes)!.Invoke(o, null));

    private static void FlattenCubic(
        List<(double X, double Y)> loop,
        double x0, double y0,
        (double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3)
    {
        const int n = 12;
        for (int i = 1; i <= n; i++)
        {
            double u = (double)i / n;
            double v = 1 - u;
            loop.Add((
                v * v * v * x0 + 3 * v * v * u * p1.X + 3 * v * u * u * p2.X + u * u * u * p3.X,
                v * v * v * y0 + 3 * v * v * u * p1.Y + 3 * v * u * u * p2.Y + u * u * u * p3.Y));
        }
    }

    private static void FlattenQuad(
        List<(double X, double Y)> loop,
        double x0, double y0,
        (double X, double Y) p1, (double X, double Y) p2)
    {
        const int n = 10;
        for (int i = 1; i <= n; i++)
        {
            double u = (double)i / n;
            double v = 1 - u;
            loop.Add((
                v * v * x0 + 2 * v * u * p1.X + u * u * p2.X,
                v * v * y0 + 2 * v * u * p1.Y + u * u * p2.Y));
        }
    }

    // SVG F.6.5 endpoint-to-center arc expansion, subdivided to lines.
    private static (double X, double Y) FlattenArc(
        List<(double X, double Y)> loop, double x0, double y0, object seg)
    {
        (double x1, double y1) = ReadPoint(seg, "get_Point");
        object size = seg.GetType().GetMethod("get_Size", Type.EmptyTypes)!.Invoke(seg, null)!;
        double rx = Math.Abs(GetDouble(size, "get_Width"));
        double ry = Math.Abs(GetDouble(size, "get_Height"));
        if (rx <= 0 || ry <= 0)
        {
            loop.Add((x1, y1));
            return (x1, y1);
        }
        double phi = GetDouble(seg, "get_RotationAngle") * Math.PI / 180.0;
        bool large = GetBool(seg, "get_IsLargeArc");
        // SweepDirection mirrors D2D: 0 counterclockwise, 1 clockwise.
        bool sweep = Convert.ToInt32(
            seg.GetType().GetMethod("get_SweepDirection", Type.EmptyTypes)!.Invoke(seg, null)) == 1;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);
        double dx = (x0 - x1) / 2;
        double dy = (y0 - y1) / 2;
        double x1p = cosPhi * dx + sinPhi * dy;
        double y1p = -sinPhi * dx + cosPhi * dy;
        double lam = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lam > 1)
        {
            double s = Math.Sqrt(lam);
            rx *= s;
            ry *= s;
        }
        double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double f = den <= 0 ? 0 : Math.Sqrt(Math.Max(0, num / den));
        if (large == sweep)
            f = -f;
        double cxp = f * rx * y1p / ry;
        double cyp = -f * ry * x1p / rx;
        double cx = cosPhi * cxp - sinPhi * cyp + (x0 + x1) / 2;
        double cy = sinPhi * cxp + cosPhi * cyp + (y0 + y1) / 2;
        double a1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double da = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && da > 0)
            da -= 2 * Math.PI;
        else if (sweep && da < 0)
            da += 2 * Math.PI;
        int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(da) / (Math.PI / 18)));
        for (int i = 1; i <= n; i++)
        {
            double a = a1 + da * i / n;
            double ex = rx * Math.Cos(a);
            double ey = ry * Math.Sin(a);
            loop.Add((
                cosPhi * ex - sinPhi * ey + cx,
                sinPhi * ex + cosPhi * ey + cy));
        }
        return (x1, y1);
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        double d = Math.Clamp((ux * vx + uy * vy)
            / (Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy)), -1, 1);
        double a = Math.Acos(d);
        return ux * vy - uy * vx < 0 ? -a : a;
    }

    private static void FillRoundedRectDip(
        Target t, double x, double y, double w, double h,
        double rx, double ry, float alpha, float r, float g, float b)
    {
        var loop = BuildRoundedRectLoop(x, y, w, h, rx, ry);
        if (loop.Count >= 3)
            ScanFill(t, loop, 0, alpha, r, g, b);
    }

    private static List<(double X, double Y)> BuildRoundedRectLoop(
        double x, double y, double w, double h, double rx, double ry)
    {
        var loop = new List<(double X, double Y)>();
        if (w <= 0 || h <= 0)
            return loop;
        rx = Math.Max(0, Math.Min(rx, w / 2));
        ry = Math.Max(0, Math.Min(ry, h / 2));
        if (rx <= 0 || ry <= 0)
        {
            loop.Add((x, y));
            loop.Add((x + w, y));
            loop.Add((x + w, y + h));
            loop.Add((x, y + h));
            return loop;
        }
        // Corner arcs (6 subdivisions each), clockwise from top-left.
        (double ccx, double ccy, double a0, double a1)[] corners =
        [
            (x + rx, y + ry, Math.PI, Math.PI * 1.5),
            (x + w - rx, y + ry, Math.PI * 1.5, Math.PI * 2),
            (x + w - rx, y + h - ry, 0, Math.PI / 2),
            (x + rx, y + h - ry, Math.PI / 2, Math.PI),
        ];
        const int n = 6;
        foreach (var (ccx, ccy, a0, a1) in corners)
            for (int i = 0; i <= n; i++)
            {
                double a = a0 + (a1 - a0) * i / n;
                loop.Add((ccx + rx * Math.Cos(a), ccy + ry * Math.Sin(a)));
            }
        return loop;
    }

    private static List<(double X, double Y)> BuildEllipseLoop(
        double cx, double cy, double rx, double ry)
    {
        var loop = new List<(double X, double Y)>();
        if (rx <= 0 || ry <= 0)
            return loop;
        const int n = 48;
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            loop.Add((cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return loop;
    }

    // Every supported provider shape reduced to DIP loops. Shared by
    // fills (which additionally need the path fill rule) and strokes.
    private static List<List<(double X, double Y)>> FlattenProvider(object provider)
    {
        var loops = new List<List<(double X, double Y)>>();
        Type type = provider.GetType();
        if (type.GetMethod("get_Figures", Type.EmptyTypes) is not null)
        {
            foreach (object fig in (System.Collections.IEnumerable)
                type.GetMethod("get_Figures", Type.EmptyTypes)!.Invoke(provider, null)!)
            {
                var loop = BuildLoop(fig);
                if (loop.Count >= 3)
                    loops.Add(loop);
            }
            return loops;
        }
        if (type.GetMethod("get_Children", Type.EmptyTypes) is not null)
        {
            foreach (object child in (System.Collections.IEnumerable)
                type.GetMethod("get_Children", Type.EmptyTypes)!.Invoke(provider, null)!)
            {
                if (child is not null)
                    loops.AddRange(FlattenProvider(child));
            }
            return loops;
        }
        if (type.GetMethod("get_Rect", Type.EmptyTypes) is not null)
        {
            object box = type.GetMethod("get_Rect", Type.EmptyTypes)!.Invoke(provider, null)!;
            var loop = BuildRoundedRectLoop(
                GetDouble(box, "get_X"), GetDouble(box, "get_Y"),
                GetDouble(box, "get_Width"), GetDouble(box, "get_Height"),
                GetDouble(provider, "get_RadiusX"), GetDouble(provider, "get_RadiusY"));
            if (loop.Count >= 3)
                loops.Add(loop);
            return loops;
        }
        if (type.GetMethod("get_Center", Type.EmptyTypes) is not null)
        {
            object c = type.GetMethod("get_Center", Type.EmptyTypes)!.Invoke(provider, null)!;
            var loop = BuildEllipseLoop(
                GetDouble(c, "get_X"), GetDouble(c, "get_Y"),
                GetDouble(provider, "get_RadiusX"), GetDouble(provider, "get_RadiusY"));
            if (loop.Count >= 3)
                loops.Add(loop);
            return loops;
        }
        if (type.GetMethod("get_Geometry", Type.EmptyTypes) is not null)
        {
            object? wrapped = type.GetMethod("get_Geometry", Type.EmptyTypes)!.Invoke(provider, null);
            if (wrapped is not null)
                loops.AddRange(FlattenProvider(wrapped));
            return loops;
        }
        throw new NotSupportedException(
            "CpuPaint geometry has no headless backend for " + type.FullName);
    }

    // Icon lettering (tool glyphs with text): DirectWrite is absent
    // headless, so short strings render in an embedded 5x7 bitmap
    // font scaled to the layout's FontSize. Letterforms approximate
    // (Segoe UI parity is out of scope); color/position are exact.
    public static void DrawText(
        long ctx, object? layoutProv, float x, float y,
        float opacity, float r, float g, float b, float a)
    {
        if (ctx == 0 || layoutProv is null || opacity <= 0f || a <= 0f)
            return;
        Type lt = layoutProv.GetType();
        var getText = lt.GetMethod("get_Text", Type.EmptyTypes);
        if (getText is null)
            throw new NotSupportedException(
                "CpuPaint.DrawText has no headless backend for " + lt.FullName);
        string text = (string)getText.Invoke(layoutProv, null)!;
        if (string.IsNullOrEmpty(text))
            return;
        double size = 12;
        var getSize = lt.GetMethod("get_FontSize", Type.EmptyTypes);
        if (getSize is not null)
            size = Convert.ToDouble(getSize.Invoke(layoutProv, null));
        if (size <= 0)
            return;
        var t = FromHandle<Target>(ctx);
        float alpha = Math.Clamp(a, 0f, 1f) * opacity;
        float scale = (float)(size / 7.0);
        float cx = x;
        float cy = y;
        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                cx = x;
                cy += 8 * scale;
                continue;
            }
            if (ch == '\r')
                continue;
            if (ch == ' ' || !Font5x7.TryGetValue(char.ToUpperInvariant(ch), out string[]? rows))
            {
                cx += 6 * scale;
                continue;
            }
            for (int py = 0; py < 7; py++)
                for (int px = 0; px < 5; px++)
                    if (rows[py][px] == '#')
                        FillRectT(t, cx + px * scale, cy + py * scale,
                            scale, scale, alpha, r, g, b, 1f);
            cx += 6 * scale;
        }
    }

    // Browser-raster blit for the DrawTextLayout path: white-with-alpha
    // RGBA (BrowserText convention) tinted by the ink color, straight-
    // alpha source-over per pixel. Supported transform: translation +
    // axis-aligned scale (panel text is axis-aligned; this covers
    // non-default DPI and mirrored layouts). Rotation/shear (M12/M21
    // nonzero), zero scale, and non-finite scales are explicitly
    // rejected: the call is a silent no-op, like the other guards.
    // Destination pixels inverse-map to source texels (nearest), so
    // upscale covers without holes and downscale/mirror stay exact.
    // Primitives only, like the rest of this contract.
    public static void BlitRaster(
        long ctx, byte[]? rgba, int w, int h, float x, float y,
        float opacity, float r, float g, float b, float a)
    {
        if (ctx == 0 || rgba is null || rgba.Length < w * h * 4
            || w <= 0 || h <= 0 || opacity <= 0f || a <= 0f)
            return;
        var t = FromHandle<Target>(ctx);
        float ink = Math.Clamp(a, 0f, 1f) * opacity;
        if (t.M12 != 0f || t.M21 != 0f)
            return;
        float sx = t.M11, sy = t.M22;
        if (!float.IsFinite(sx) || !float.IsFinite(sy) || sx == 0f || sy == 0f)
            return;
        int dx0 = (int)MathF.Round(x * sx + t.M31);
        int dy0 = (int)MathF.Round(y * sy + t.M32);
        int loX = sx > 0f ? dx0 : dx0 + (int)MathF.Floor(w * sx) + 1;
        int hiX = sx > 0f ? dx0 + (int)MathF.Ceiling(w * sx) - 1 : dx0;
        int loY = sy > 0f ? dy0 : dy0 + (int)MathF.Floor(h * sy) + 1;
        int hiY = sy > 0f ? dy0 + (int)MathF.Ceiling(h * sy) - 1 : dy0;
        int cx0 = Math.Max(loX, 0), cx1 = Math.Min(hiX, t.Width - 1);
        int cy0 = Math.Max(loY, 0), cy1 = Math.Min(hiY, t.Height - 1);
        if (cx0 > cx1 || cy0 > cy1)
            return;
        for (int dy = cy0; dy <= cy1; dy++)
        {
            int siy = (int)MathF.Floor((dy - dy0) / sy);
            if ((uint)siy >= (uint)h)
                continue;
            for (int dx = cx0; dx <= cx1; dx++)
            {
                int six = (int)MathF.Floor((dx - dx0) / sx);
                if ((uint)six >= (uint)w)
                    continue;
                float sa = rgba[(siy * w + six) * 4 + 3] / 255f;
                if (sa <= 0f)
                    continue;
                BlendPixel(t, dx, dy, sa * ink, r, g, b);
            }
        }
    }

    // Effect OK-apply resume (Levels and friends): our ShowDialog
    // returns None immediately (no nested pump on one thread), so
    // EffectMenuBase.RunEffectImpl tears its preview pipeline down
    // before the user ever clicks OK. The rewriter notes the menu
    // for each config dialog (NoteEffectMenu) and routes FormClosed
    // here; an OK settle reruns the product's own DoEffect with a
    // fresh layer snapshot. Everything PDN-typed is reflection-only
    // (names, never references), like the DrawText layout path.
    // Answers false (no-op) on any shape mismatch; real failures
    // still trip the first-chance inventory at their source.

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> s_effectMenus = new();


    public static void NoteEffectMenu(object? dialog, object? menu)
    {
        try
        {
            if (dialog is null || menu is null)
                return;
            lock (s_effectMenus)
            {
                s_effectMenus.Remove(dialog);
                s_effectMenus.Add(dialog, menu);
            }
            // The noted dialog is often a host wrapper
            // (EffectConfigFormWrapper): FormClosed fires on the hosted
            // inner form, so note it under the same menu. Field-shape
            // only, fails silent.
            try
            {
                System.Type? dt = dialog.GetType();
                while (dt is not null)
                {
                    foreach (var f in dt.GetFields(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        object? val;
                        try { val = f.GetValue(dialog); }
                        catch { continue; }
                        if (val is null || ReferenceEquals(val, dialog) || ReferenceEquals(val, menu))
                            continue;
                        System.Type vt = val.GetType();
                        bool isForm = vt.FullName?.Contains("IEffectConfigForm",
                            System.StringComparison.Ordinal) == true;
                        if (!isForm)
                        {
                            foreach (var it in vt.GetInterfaces())
                            {
                                if (it.Name.Contains("IEffectConfigForm",
                                    System.StringComparison.Ordinal))
                                { isForm = true; break; }
                            }
                        }
                        if (!isForm)
                            continue;
                        lock (s_effectMenus)
                        {
                            s_effectMenus.Remove(val);
                            s_effectMenus.Add(val, menu);
                        }
                    }
                    dt = dt.BaseType;
                }
            }
            catch
            {
            }
        }
        catch
        {
        }
    }

    // FormClosed resume path: an OK settle reruns the product's own
    // DoEffect with a fresh snapshot (ShowDialog settles immediately
    // headless, so the normal post-dialog apply never runs). Result is
    // logged ([FXAPPLY]) so live probes can tell OK-applied apart from
    // the silent no-op without TEMP diagnostics.
    public static bool ResumeEffectApply(object? dialog)
    {
        try
        {
            if (dialog is null)
            {
                System.Console.WriteLine("[FXAPPLY] no-dialog");
                return false;
            }
            object? menu;
            lock (s_effectMenus)
            {
                if (!s_effectMenus.TryGetValue(dialog, out menu) || menu is null)
                {
                    System.Console.WriteLine("[FXAPPLY] no-menu-note for "
                        + dialog.GetType().Name);
                    return false;
                }
                s_effectMenus.Remove(dialog);
            }
            // DialogResult.OK == 1; Cancel/X/None settle is a no-op.
            var getResult = dialog.GetType().GetMethod("get_DialogResult", Type.EmptyTypes);
            int dr = getResult is null ? -99 : Convert.ToInt32(getResult.Invoke(dialog, null));
            if (getResult is null || dr != 1)
            {
                System.Console.WriteLine("[FXAPPLY] settle-result=" + dr);
                return false;
            }
            var getEffect = dialog.GetType().GetMethod("get_Effect", Type.EmptyTypes);
            var getToken = dialog.GetType().GetMethod("get_Token", Type.EmptyTypes);
            if (getEffect is null || getToken is null)
            {
                System.Console.WriteLine("[FXAPPLY] no-effect-or-token-accessor");
                return false;
            }
            object? effect = getEffect.Invoke(dialog, null);
            object? token = getToken.Invoke(dialog, null);
            if (effect is null || token is null)
            {
                System.Console.WriteLine("[FXAPPLY] null-effect-or-token");
                return false;
            }
            object cloned = CloneEffectToken(token);
            // The preview never rendered headless, so the live layer
            // surface still holds pre-dialog pixels: snapshot it for
            // the memento, then let DoEffect render into the layer.
            object appWs = GetProp(menu, "get_AppWorkspace");
            object dw = GetProp(appWs, "get_ActiveDocumentWorkspace");
            object layer = GetProp(dw, "get_ActiveLayer");
            object surface = GetProp(layer, "get_Surface");
            var cloneSurface = surface.GetType().GetMethod("Clone", Type.EmptyTypes)
                ?? throw new NotSupportedException("effect resume needs Surface.Clone");
            object snapshot = cloneSurface.Invoke(surface, null)!;
            // A second clone is the sacrificial render intermediate:
            // DoEffect's copy phase renders into its `original` arg,
            // so passing the pristine snapshot would contaminate the
            // very bytes the undo memento must save (undo then
            // restores post-effect pixels: a silent no-op). The
            // snapshot below stays pristine for the tile stash and
            // the replicated push; only renderOriginal is handed to
            // DoEffect. Same pre-apply content either way, so render
            // behavior (tile stash + copy target) is unchanged.
            object renderOriginal = cloneSurface.Invoke(surface, null)!;
            // Stash the pre-effect pixels for the CPU tile renderer
            // (the product tile loop reads through our OnCopyPixels
            // hook; see TryRenderGpuTileCpu). Best-effort: a miss falls
            // back to the product D2D path as before.
            NoteTileSource(effect, snapshot);
            // Private base methods are invisible through a derived menu
            // type: walk up (AdjustmentsMenu -> EffectMenuBase).
            System.Reflection.MethodInfo? doEffect = null;
            for (System.Type? mt = menu.GetType(); mt is not null; mt = mt.BaseType)
            {
                doEffect = mt.GetMethod("DoEffect",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);
                if (doEffect is not null)
                    break;
            }
            doEffect ??= menu.GetType().GetMethod("DoEffect",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (doEffect is null)
                throw new NotSupportedException("effect resume needs EffectMenuBase.DoEffect");
            // DoEffect renders + copies + pushes the undo memento over the
            // render-scan lists (lazy Results). The dialog preview owned
            // those; the resume must supply its own: a full-surface scan
            // (the layer still holds pre-dialog pixels headless, and the
            // CPU tile path re-renders from the stashed snapshot). Nulls
            // NRE inside the queued memento builder and the copy phase,
            // which DoEffect absorbs: applied=True with no push and no
            // pixels (rev120 probe).
            object scans = BuildFullSurfaceScans(snapshot, out object scanList);
            object?[] args = new object?[] { effect, cloned, scans, scans, renderOriginal, null };
            object? applied = doEffect.Invoke(menu, args);
            RefreshRepeatEffect(menu, effect, cloned);
            bool ok = applied is true;
            // DoEffect's own memento builder (b__0) runs on the work
            // dispatcher, which never pumps headless: the render/copy
            // phases complete but bitmapMemento stays null, so the push
            // is skipped. Replicate the push here from the same inputs
            // (pre-effect snapshot + full-surface scans): the memento
            // saves pre-effect pixels, so undo restores correctly.
            string rowName = "-";
            string push = ok ? PushHistoryMemento(dw, effect, snapshot, scanList, out rowName) : "skipped";
            System.Console.WriteLine("[FXAPPLY] doeffect-applied=" + ok + " push=" + push + " row=" + rowName);
            return ok;
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine("[FXAPPLY] threw " + ex.GetType().Name
                + ": " + (ex.Message ?? "").Split('\n')[0]);
            return false;
        }
    }

    // A successful Result<IReadOnlyList<RectInt32>> holding one
    // full-surface scan, built reflection-only (names, never
    // references): ResultValueImpl<T>(T) derives Result<T>, and
    // List<RectInt32> answers IReadOnlyList<RectInt32>.
    // Best-effort replica of DoEffect's dispatcher-built undo memento
    // (see b__0): BitmapHistoryMemento(name, image, dw, layer, scans,
    // original, null) pushed on dw.History. Returns a short status for
    // the FXAPPLY log; unit-test fakes (no PDN history types) report
    // "unavailable" without failing the apply.
    private static string PushHistoryMemento(object dw,
        object effect, object snapshot, object scanList, out string rowName)
    {
        rowName = effect.GetType().Name;
        string iconDesc = "n/a";
        try
        {
            Type dwType = dw.GetType();
            object? history = dwType.GetMethod("get_History", Type.EmptyTypes)?.Invoke(dw, null);
            object? layerIndexObj = dwType.GetMethod("get_ActiveLayerIndex", Type.EmptyTypes)?.Invoke(dw, null);
            if (history is null || layerIndexObj is null)
                return "unavailable-history";
            int layerIndex = System.Convert.ToInt32(layerIndexObj);
            string name = effect.GetType().Name;
            rowName = name;
            object? image = null;
            try
            {
                // IEffect.EffectInfo is often explicit: the public
                // lookup misses, so fall back to the interface map
                // (desktop b__0 binds it statically and always sees it;
                // without this the row shows the class name, e.g.
                // "LevelsGpuEffect" instead of "Levels").
                object? info = effect.GetType().GetMethod("get_EffectInfo",
                    Type.EmptyTypes)?.Invoke(effect, null);
                if (info is null)
                {
                    foreach (var iface in effect.GetType().GetInterfaces())
                    {
                        if (iface.FullName != "PaintDotNet.Effects.IEffect")
                            continue;
                        var getter = iface.GetMethod("get_EffectInfo", Type.EmptyTypes);
                        if (getter is null)
                            break;
                        try { info = getter.Invoke(effect, null); }
                        catch { info = null; }
                        break;
                    }
                }
                if (info is not null)
                {
                    object? n = info.GetType().GetMethod("get_Name", Type.EmptyTypes)?.Invoke(info, null);
                    if (n is string s && s.Length > 0)
                        name = s;
                    // The row icon stays null even when the effect has one:
                    // a non-null ImageResource blanks the whole history
                    // row headless (band paints, icon+text do not, no
                    // error). The 16x16 source Bitmap exists, but its
                    // device image carries no pixels, so the row's
                    // DrawImage is a silent no-op and the text never
                    // lands. Text-only rows paint ("Levels" verified
                    // live); icons wait on the device-image gap.
                    iconDesc = "suppressed-row-paint-gap";
                }
            }
            catch
            {
            }
            Type? mementoType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try { mementoType = asm.GetType("PaintDotNet.HistoryMementos.BitmapHistoryMemento"); }
                catch { continue; }
                if (mementoType is not null)
                    break;
            }
            if (mementoType is null)
                return "unavailable-memento-type";
            System.Reflection.ConstructorInfo? mctor = null;
            foreach (var c in mementoType.GetConstructors(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance))
            {
                var ps = c.GetParameters();
                if (ps.Length == 7 && ps[0].ParameterType == typeof(string))
                {
                    mctor = c;
                    break;
                }
            }
            if (mctor is null)
                return "unavailable-memento-ctor";
            rowName = name;
            object?[] margs = new object?[] { name, image, dw, layerIndex, scanList, snapshot, null };
            object memento;
            try
            {
                memento = mctor.Invoke(margs)!;
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            {
                return "ctor-threw-" + tie.InnerException.GetType().Name;
            }
            var push = history.GetType().GetMethod("PushNewMemento");
            if (push is null)
                return "unavailable-push";
            push.Invoke(history, new[] { memento });
            // Immediate read-back: distinguishes a memento that is
            // born unreadable (wrong copy/ctor: get_Data dies here)
            // from one that dies later (collected/disposed: get_Data
            // dies only at undo). Also names the source assembly so
            // a duplicate-type copy cannot hide.
            string born = "born-unknown";
            try
            {
                object? data = mementoType.GetMethod("get_Data",
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)?.Invoke(memento, null);
                born = "born-" + (data is null ? "null" : "live");
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                born = "born-threw-" + (tie.InnerException?.GetType().Name ?? "null");
            }
            string asmName = "asm-unknown";
            try { asmName = "asm-" + mementoType.Assembly.GetName().Name; }
            catch { }
            return "pushed icon=" + iconDesc + " " + born + " " + asmName;
        }
        catch (System.Exception ex)
        {
            return "threw-" + ex.GetType().Name;
        }
    }

    private static object BuildFullSurfaceScans(object surface, out object scanList)
    {
        Type st = surface.GetType();
        int w = System.Convert.ToInt32(st.GetMethod("get_Width",
            Type.EmptyTypes)?.Invoke(surface, null));
        int h = System.Convert.ToInt32(st.GetMethod("get_Height",
            Type.EmptyTypes)?.Invoke(surface, null));
        if (w <= 0 || h <= 0)
            throw new NotSupportedException("effect resume needs a sized surface");
        // Exact full-name match across loaded assemblies (live: the
        // PDN assemblies; unit tests stand in with same-named fakes).
        Type? rectType = null;
        Type? implOpen = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            try { rectType ??= asm.GetType("PaintDotNet.Rendering.RectInt32"); }
            catch { }
            try { implOpen ??= asm.GetType("PaintDotNet.Functional.ResultValueImpl`1"); }
            catch { }
            if (rectType is not null && implOpen is not null)
                break;
        }
        if (rectType is null || implOpen is null)
            throw new NotSupportedException("effect resume needs RectInt32+ResultValueImpl");
        object rect;
        try
        {
            rect = System.Activator.CreateInstance(rectType, 0, 0, w, h)!;
        }
        catch (System.MissingMethodException)
        {
            throw new NotSupportedException("effect resume needs RectInt32(x,y,w,h)");
        }
        // Pin the (x, y, width, height) order: a swapped ctor would
        // silently render the wrong region.
        int rx = System.Convert.ToInt32(rectType.GetMethod("get_X",
            Type.EmptyTypes)?.Invoke(rect, null));
        int ry = System.Convert.ToInt32(rectType.GetMethod("get_Y",
            Type.EmptyTypes)?.Invoke(rect, null));
        int rw = System.Convert.ToInt32(rectType.GetMethod("get_Width",
            Type.EmptyTypes)?.Invoke(rect, null));
        int rh = System.Convert.ToInt32(rectType.GetMethod("get_Height",
            Type.EmptyTypes)?.Invoke(rect, null));
        if (rx != 0 || ry != 0 || rw != w || rh != h)
            throw new NotSupportedException(
                $"effect resume RectInt32 order mismatch ({rx},{ry},{rw},{rh}) vs (0,0,{w},{h})");
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(rectType);
        object list = System.Activator.CreateInstance(listType)!;
        listType.GetMethod("Add")!.Invoke(list, new[] { rect });
        scanList = list;
        Type roType = typeof(System.Collections.Generic.IReadOnlyList<>).MakeGenericType(rectType);
        Type impl = implOpen.MakeGenericType(roType);
        // ResultValueImpl<T>..ctor(T) is internal live (public on the
        // unit-test stand-in): take the single-arg ctor regardless of
        // visibility instead of the public-only Activator path.
        System.Reflection.ConstructorInfo? ctor = null;
        foreach (var c in impl.GetConstructors(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance))
        {
            if (c.GetParameters().Length == 1)
            {
                ctor = c;
                break;
            }
        }
        if (ctor is null)
            throw new NotSupportedException("effect resume needs a ResultValueImpl value ctor");
        return ctor.Invoke(new object[] { list })!;
    }

    // Abandoned kernel event handles finalize-crash the browser PAL
    // (WaitSubsystem.DeleteHandle NRE is fatal on a finalizer thread:
    // the tab dies at the first gen-2 GC once stroke/tile pressure
    // trips a collection). The rewrite calls this right after every
    // PDN `new ManualResetEvent/AutoResetEvent/EventWaitHandle`, so a
    // handle that is never disposed leaks benignly instead of killing
    // the session. Safe by construction: Dispose still runs
    // ReleaseHandle normally (it suppresses the finalizer itself), and
    // the headless single-threaded paths never wait on these events.
    // Mutexes/semaphores are deliberately excluded (abandonment has
    // ownership semantics; events are pure signaling).
    public static void DefuseWaitHandle(object? handle)
    {
        try
        {
            if (handle is not System.Threading.WaitHandle wh)
                return;
            try
            {
                var h = wh.SafeWaitHandle;
                if (h is not null)
                    System.GC.SuppressFinalize(h);
            }
            catch
            {
            }
            try
            {
                System.GC.SuppressFinalize(wh);
            }
            catch
            {
            }
        }
        catch
        {
        }
    }

    private static object CloneEffectToken(object token)
    {
        // Instance CloneT first (test fakes); product tokens clone
        // through the static CloneableExtensions.CloneT generic.
        var inst = token.GetType().GetMethod("CloneT", Type.EmptyTypes);
        if (inst is not null)
            return inst.Invoke(token, null)!;
        Type tokenType = token.GetType();
        // The helper lives wherever the platform put it (Core/Base),
        // not necessarily beside the token: scan loaded assemblies.
        System.Type? ext = tokenType.Assembly.GetType("PaintDotNet.CloneableExtensions");
        if (ext is null)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name?.StartsWith("PaintDotNet",
                    System.StringComparison.Ordinal) != true)
                    continue;
                try { ext = asm.GetType("PaintDotNet.CloneableExtensions"); }
                catch { continue; }
                if (ext is not null)
                    break;
            }
        }
        if (ext is null)
            throw new NotSupportedException("effect resume needs CloneableExtensions");
        var generic = ext.GetMethod("CloneT")
            ?? throw new NotSupportedException("effect resume needs CloneT");
        return generic.MakeGenericMethod(tokenType).Invoke(null, new object[] { token })!;
    }

    private static object GetProp(object target, string getter)
    {
        var m = target.GetType().GetMethod(getter, Type.EmptyTypes)
            ?? throw new NotSupportedException("effect resume needs " + getter);
        return m.Invoke(target, null)!;
    }

    // Repeat-effect (Ctrl+F) cache, best-effort after a real apply:
    // mirrors RunEffectImpl's post-DoEffect bookkeeping.
    private static void RefreshRepeatEffect(object menu, object effect, object clonedToken)
    {
        try
        {
            Type mt = menu.GetType();
            var getEffect = effect.GetType().GetMethod("get_EffectInfo");
            var populate = mt.GetMethod("PopulateMenu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (populate is null)
                return;
            var infoField = mt.GetField("lastEffectInfo",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var tokenField = mt.GetField("lastEffectToken",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (infoField is null || tokenField is null || getEffect is null)
                return;
            infoField.SetValue(menu, getEffect.Invoke(effect, null));
            tokenField.SetValue(menu, CloneEffectToken(clonedToken));
            populate.Invoke(menu, null);
        }
        catch
        {
        }
    }

    // CPU tile renderer for GPU effects (Levels first): the product
    // tile loop reads rendered tiles through Renderer.OnCopyPixels,
    // which our rewriter routes here. We answer Levels tiles from the
    // stashed pre-effect surface through the effect token's transfer
    // function; anything else returns false and the product D2D path
    // runs as before. All PDN-typed access is reflection-only.
    private sealed class TileSource
    {
        public byte[] Pixels = System.Array.Empty<byte>();
        public int Width;
        public int Height;
        public int Stride;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, TileSource> s_tileSources = new();

    public static void NoteTileSource(object? effect, object? surface)
    {
        try
        {
            if (effect is null || surface is null)
                return;
            System.Type st = surface.GetType();
            int w = System.Convert.ToInt32(st.GetProperty("Width")?.GetValue(surface));
            int h = System.Convert.ToInt32(st.GetProperty("Height")?.GetValue(surface));
            int stride = System.Convert.ToInt32(st.GetProperty("Stride")?.GetValue(surface));
            if (w <= 0 || h <= 0 || stride < w * 4)
                return;
            var addr = st.GetMethod("GetPointAddress", new[] { typeof(int), typeof(int) });
            if (addr is null)
                return;
            IntPtr scan0 = UnboxPointer(addr.Invoke(surface, new object[] { 0, 0 }));
            if (scan0 == IntPtr.Zero)
                return;
            byte[] pixels = new byte[stride * h];
            System.Runtime.InteropServices.Marshal.Copy(scan0, pixels, 0, pixels.Length);
            NoteTileSourceBytes(effect, pixels, w, h, stride);
        }
        catch
        {
        }
    }

    public static void NoteTileSourceBytes(object effect, byte[] pixels, int w, int h, int stride)
    {
        lock (s_tileSources)
        {
            s_tileSources.Remove(effect);
            s_tileSources.Add(effect, new TileSource { Pixels = pixels, Width = w, Height = h, Stride = stride });
        }
    }

    public static double SrgbByteToLinear(int c)
    {
        double s = c / 255.0;
        return s <= 0.04045 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    public static byte LinearToSrgbByte(double l)
    {
        double s = l <= 0.0031308 ? 12.92 * l : 1.055 * System.Math.Pow(l, 1.0 / 2.4) - 0.055;
        int b = (int)System.Math.Round(s * 255.0);
        return (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
    }

    public static double SrgbFloatToLinear(double s)
    {
        return s <= 0.04045 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    // One transfer-function channel. Conventions (v2, documented):
    // ApplyLevelsChannel itself works in linear-light 0..1, but the
    // LevelsParameters token floats are sRGB 0..1 (exactly what the
    // dialog shows): the tile renderer linearizes the black/white
    // points at the boundary below. v1 wrongly fed the sRGB token
    // values as linear divisors (live oracle: white=100 mapped 85 to
    // 132 instead of 220). Gamma applies as pow(t, 1/gamma) so higher
    // brightens (Photoshop-compatible); out-of-range clamps;
    // degenerate input range maps below-low to 0, at-or-above to 1.
    public static byte ApplyLevelsChannel(double linear, double inBlack, double inWhite, double gamma, double outBlack, double outWhite)
    {
        double t = inWhite > inBlack ? (linear - inBlack) / (inWhite - inBlack) : (linear < inBlack ? 0.0 : 1.0);
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;
        double g = gamma > 0.0 ? 1.0 / gamma : 1.0;
        double o = outBlack + (outWhite - outBlack) * System.Math.Pow(t, g);
        return LinearToSrgbByte(o);
    }

    private static double FloatProp(object target, string name)
    {
        object? v = target.GetType().GetProperty(name)?.GetValue(target);
        return System.Convert.ToDouble(v);
    }

    private static double ChannelOf(object rgb, int c)
    {
        return FloatProp(rgb, c == 0 ? "R" : c == 1 ? "G" : "B");
    }

    // CPU tile renderer for GPU effects: renders the product renderer's
    // tile through the effect LUT from the stashed pre-effect surface.
    public static bool TryRenderGpuTileCpu(object? renderer, object? dst, object? src)
    {
        try
        {
            if (renderer is null || dst is null || src is null)
                return false;
            string? rn = renderer.GetType().FullName;
            if (rn is null || !rn.Contains("GpuEffect")
                || (!rn.EndsWith("+Renderer", System.StringComparison.Ordinal)
                    && !rn.EndsWith("/Renderer", System.StringComparison.Ordinal)))
                return false;
            object? owner = null;
            for (System.Type? ot = renderer.GetType(); ot is not null; ot = ot.BaseType)
            {
                var f = ot.GetField("owner",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);
                if (f is not null) { owner = f.GetValue(renderer); break; }
            }
            if (owner is null)
                return false;
            if (!owner.GetType().Name.Contains("LevelsGpuEffect", System.StringComparison.Ordinal))
                return false;
            object? token = null;
            try
            {
                // EffectBase.Token is protected, and GpuEffect<TToken>
                // shadows it with new: a flat lookup is ambiguous, so
                // walk most-derived first (the generic token wins).
                for (System.Type? tt = owner.GetType(); tt is not null; tt = tt.BaseType)
                {
                    var tp = tt.GetProperty("Token",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly);
                    if (tp is not null) { token = tp.GetValue(owner); break; }
                }
            }
            catch
            {
                return false;
            }
            if (token is null)
                return false;
            object? levels = null;
            try
            {
                levels = token.GetType().GetProperty("Levels")?.GetValue(token);
            }
            catch
            {
                return false;
            }
            if (levels is null)
                return false;
            double[] inB = new double[3];
            double[] inW = new double[3];
            double[] gam = new double[3];
            double[] outB = new double[3];
            double[] outW = new double[3];
            object? ib = levels.GetType().GetProperty("InputBlackPoint")?.GetValue(levels);
            object? iw = levels.GetType().GetProperty("InputWhitePoint")?.GetValue(levels);
            object? gm = levels.GetType().GetProperty("Gamma")?.GetValue(levels);
            object? ob = levels.GetType().GetProperty("OutputBlackPoint")?.GetValue(levels);
            object? ow = levels.GetType().GetProperty("OutputWhitePoint")?.GetValue(levels);
            if (ib is null || iw is null || gm is null || ob is null || ow is null)
                return false;
            for (int c = 0; c < 3; c++)
            {
                // Token points are sRGB 0..1 (dialog values); the
                // channel math below is linear-light. Gamma is a
                // power, encoding-agnostic.
                inB[c] = SrgbFloatToLinear(ChannelOf(ib, c));
                inW[c] = SrgbFloatToLinear(ChannelOf(iw, c));
                gam[c] = ChannelOf(gm, c);
                outB[c] = SrgbFloatToLinear(ChannelOf(ob, c));
                outW[c] = SrgbFloatToLinear(ChannelOf(ow, c));
            }
            TileSource? ts;
            lock (s_tileSources)
            {
                if (!s_tileSources.TryGetValue(owner, out ts) || ts is null)
                    return false;
            }
            System.Type dt = dst.GetType();
            int tw = System.Convert.ToInt32(dt.GetProperty("Width")?.GetValue(dst));
            int th = System.Convert.ToInt32(dt.GetProperty("Height")?.GetValue(dst));
            int tstride = System.Convert.ToInt32(dt.GetProperty("Stride")?.GetValue(dst));
            IntPtr dptr = UnboxPointer(dt.GetProperty("Ptr")?.GetValue(dst));
            System.Type pt = src.GetType();
            int tx = System.Convert.ToInt32(pt.GetProperty("X")?.GetValue(src));
            int ty = System.Convert.ToInt32(pt.GetProperty("Y")?.GetValue(src));
            if (tw <= 0 || th <= 0 || tstride <= 0 || dptr == IntPtr.Zero)
                return false;
            if (tx < 0 || ty < 0 || tx + tw > ts.Width || ty + th > ts.Height)
                return false;
            byte[] srow = new byte[tw * 4];
            unsafe
            {
                byte* dp = (byte*)dptr.ToPointer();
                for (int y = 0; y < th; y++)
                {
                    System.Buffer.BlockCopy(ts.Pixels, (ty + y) * ts.Stride + tx * 4, srow, 0, srow.Length);
                    byte* row = dp + (nint)y * (nint)tstride;
                    for (int x = 0; x < tw; x++)
                    {
                        int so = x * 4;
                        byte b = srow[so];
                        byte g = srow[so + 1];
                        byte r = srow[so + 2];
                        byte a = srow[so + 3];
                        byte ob2;
                        byte og;
                        byte or;
                        if (a == 0)
                        {
                            // Matches the graph's ReplaceTransparent prep:
                            // void pixels take the output floor.
                            ob2 = LinearToSrgbByte(outB[0]);
                            og = LinearToSrgbByte(outB[1]);
                            or = LinearToSrgbByte(outB[2]);
                        }
                        else
                        {
                            ob2 = ApplyLevelsChannel(SrgbByteToLinear(b), inB[0], inW[0], gam[0], outB[0], outW[0]);
                            og = ApplyLevelsChannel(SrgbByteToLinear(g), inB[1], inW[1], gam[1], outB[1], outW[1]);
                            or = ApplyLevelsChannel(SrgbByteToLinear(r), inB[2], inW[2], gam[2], outB[2], outW[2]);
                        }
                        row[so] = ob2;
                        row[so + 1] = og;
                        row[so + 2] = or;
                        row[so + 3] = a;
                    }
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static readonly Dictionary<char, string[]> Font5x7 = new()
    {
        ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
        ['C'] = [".####", "#....", "#....", "#....", "#....", "#....", ".####"],
        ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
        ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
        ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
        ['G'] = [".####", "#....", "#....", "#.###", "#...#", "#...#", ".###."],
        ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
        ['I'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
        ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#...#", ".###."],
        ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
        ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
        ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
        ['N'] = ["#...#", "##..#", "##..#", "#.#.#", "#..##", "#..##", "#...#"],
        ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
        ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
        ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
        ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
        ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
        ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
        ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
        ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
        ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
        ['0'] = [".###.", "#..##", "#.#.#", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
        ['.'] = [".....", ".....", ".....", ".....", ".....", "..##.", "..##."],
        [','] = [".....", ".....", ".....", ".....", "..#..", "..#..", ".#..."],
        [':'] = [".....", "..#..", ".....", ".....", "..#..", ".....", "..#.."],
        [';'] = [".....", "..#..", ".....", ".....", "..#..", "..#..", ".#..."],
        ['!'] = ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
        ['?'] = [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
        ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
        ['+'] = [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
        ['*'] = [".....", ".#.#.", "..#..", "#####", "..#..", ".#.#.", "....."],
        ['/'] = ["....#", "....#", "...#.", "..#..", ".#...", "#....", "#...."],
        ['('] = ["...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#."],
        [')'] = [".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..."],
        ['%'] = ["##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##"],
        ['&'] = [".##..", "#..#.", "#..#.", ".#...", "#.#.#", "#..#.", ".##.#"],
        ['\''] = ["..#..", "..#..", ".....", ".....", ".....", ".....", "....."],
        ['"'] = [".#.#.", ".#.#.", ".....", ".....", ".....", ".....", "....."],
        ['='] = [".....", ".....", "#####", ".....", "#####", ".....", "....."],
        ['_'] = [".....", ".....", ".....", ".....", ".....", ".....", "#####"],
        ['<'] = ["...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#."],
        ['>'] = [".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#..."],
        ['['] = [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
        [']'] = [".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###."],
        ['{'] = ["...#.", "..#..", "..#..", ".#...", "..#..", "..#..", "...#."],
        ['}'] = [".#...", "..#..", "..#..", "...#.", "..#..", "..#..", ".#..."],
    };

    // Outline stroke for icon outlines: round-capped discs stamped
    // along every flattened loop (dash/cap styles render solid).
    public static void StrokeGeometry(
        long ctx, object? geoProvider, float width, float opacity,
        float r, float g, float b, float a)
    {
        if (ctx == 0 || geoProvider is null || width <= 0f || opacity <= 0f || a <= 0f)
            return;
        var t = FromHandle<Target>(ctx);
        float alpha = Math.Clamp(a, 0f, 1f) * opacity;
        float radius = width / 2f;
        double step = Math.Max(0.5, width / 2);
        foreach (var loop in FlattenProvider(geoProvider))
        {
            for (int i = 0; i < loop.Count; i++)
            {
                var (x0, y0) = loop[i];
                var (x1, y1) = loop[(i + 1) % loop.Count];
                double dx = x1 - x0;
                double dy = y1 - y0;
                double len = Math.Sqrt(dx * dx + dy * dy);
                int n = Math.Max(1, (int)Math.Ceiling(len / step));
                for (int k = 0; k <= n; k++)
                {
                    float px = (float)(x0 + dx * k / n);
                    float py = (float)(y0 + dy * k / n);
                    float dx0 = px * t.M11 + py * t.M21 + t.M31;
                    float dy0 = px * t.M12 + py * t.M22 + t.M32;
                    float dr = radius * (Math.Abs(t.M11) + Math.Abs(t.M21)
                        + Math.Abs(t.M12) + Math.Abs(t.M22)) / 2f;
                    if (dr > 0)
                        FillEllipseT(t, dx0, dy0, dr, dr, alpha, r, g, b, 1f);
                }
            }
        }
    }

    // Device-space scanline fill with 2x2 coverage AA. Rule 0 is
    // even-odd, 1 is nonzero (PathGeometry.FillRule values). All
    // loops share one edge soup so the rule spans figures.
    private static void ScanFill(
        Target t, List<(double X, double Y)> loop, int fillRule,
        float alpha, float r, float g, float b)
        => ScanFillLoops(t, [loop], fillRule, alpha, r, g, b);

    private static void ScanFillLoops(
        Target t, List<List<(double X, double Y)>> loops, int fillRule,
        float alpha, float r, float g, float b)
    {
        var dev = new List<(float X, float Y)>();
        var starts = new List<int>();
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var loop in loops)
        {
            starts.Add(dev.Count);
            foreach (var (lx, ly) in loop)
            {
                float dx = (float)(lx * t.M11 + ly * t.M21 + t.M31);
                float dy = (float)(lx * t.M12 + ly * t.M22 + t.M32);
                dev.Add((dx, dy));
                if (dx < minX) minX = dx;
                if (dy < minY) minY = dy;
                if (dx > maxX) maxX = dx;
                if (dy > maxY) maxY = dy;
            }
        }
        int ix0 = Math.Max(0, (int)MathF.Floor(minX));
        int iy0 = Math.Max(0, (int)MathF.Floor(minY));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(maxX));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(maxY));
        (float OX, float OY)[] taps = [(0.25f, 0.25f), (0.75f, 0.25f), (0.25f, 0.75f), (0.75f, 0.75f)];
        for (int yy = iy0; yy <= iy1; yy++)
            for (int xx = ix0; xx <= ix1; xx++)
            {
                int cover = 0;
                foreach (var (ox, oy) in taps)
                    if (PointInPoly(dev, starts, xx + ox, yy + oy, fillRule))
                        cover++;
                if (cover > 0)
                    BlendPixel(t, xx, yy, alpha * cover / 4f, r, g, b);
            }
    }

    // Each loop closes on itself; parity/winding accumulate across
    // loops so one rule spans all figures.
    private static bool PointInPoly(
        List<(float X, float Y)> dev, List<int> starts, float x, float y, int fillRule)
    {
        if (fillRule == 1)
        {
            int winding = 0;
            for (int k = 0; k < starts.Count; k++)
            {
                int s = starts[k];
                int e = k + 1 < starts.Count ? starts[k + 1] : dev.Count;
                for (int i = s, j = e - 1; i < e; j = i++)
                {
                    float xi = dev[i].X, yi = dev[i].Y;
                    float xj = dev[j].X, yj = dev[j].Y;
                    if (yi <= y)
                    {
                        if (yj > y && (xj - xi) * (y - yi) - (x - xi) * (yj - yi) > 0)
                            winding++;
                    }
                    else
                    {
                        if (yj <= y && (xj - xi) * (y - yi) - (x - xi) * (yj - yi) < 0)
                            winding--;
                    }
                }
            }
            return winding != 0;
        }
        bool inside = false;
        for (int k = 0; k < starts.Count; k++)
        {
            int s = starts[k];
            int e = k + 1 < starts.Count ? starts[k + 1] : dev.Count;
            for (int i = s, j = e - 1; i < e; j = i++)
            {
                float xi = dev[i].X, yi = dev[i].Y;
                float xj = dev[j].X, yj = dev[j].Y;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }
        }
        return inside;
    }

    private static float SrgbToLinear(float c)
        => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float LinearToSrgb(float c)
        => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    // Separable Gaussian blur over premultiplied-float RGBA, clamp edges.
    private static void GaussBlur(float[] buf, int width, int height, double sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3.0));
        float[] kernel = new float[radius * 2 + 1];
        float sum = 0f;
        for (int i = -radius; i <= radius; i++)
        {
            float v = MathF.Exp(-0.5f * (float)(i * i) / (float)(sigma * sigma));
            kernel[i + radius] = v;
            sum += v;
        }
        for (int i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;
        float[] tmp = new float[buf.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float acc = 0f;
                    for (int i = -radius; i <= radius; i++)
                    {
                        int sx = Math.Clamp(x + i, 0, width - 1);
                        acc += buf[(y * width + sx) * 4 + c] * kernel[i + radius];
                    }
                    tmp[(y * width + x) * 4 + c] = acc;
                }
            }
        }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float acc = 0f;
                    for (int i = -radius; i <= radius; i++)
                    {
                        int sy = Math.Clamp(y + i, 0, height - 1);
                        acc += tmp[(sy * width + x) * 4 + c] * kernel[i + radius];
                    }
                    buf[(y * width + x) * 4 + c] = acc;
                }
            }
        }
    }

    // CPU replacement for BasicSampledBrushRenderer.CreateSpriteImage:
    // hard ellipse + sRGB->linear + Gaussian blur (sigma = radius/3) +
    // linear->sRGB + optional invert + copy-blue-to-alpha, all in
    // premultiplied float like the D2D graph. Returns a Pbgra32 Bitmap
    // handle (0 when degenerate) ready for DrawBitmap.
    public static long CreateSprite(
        int width, int height,
        double cx, double cy,
        double softDiameter, double blurRadius,
        float clearR, float clearG, float clearB, float clearA,
        float spriteR, float spriteG, float spriteB, float spriteA,
        int invert, double hardness)
    {
        if (width <= 0 || height <= 0 || softDiameter <= 0)
            return 0;
        var pixels = new uint[width * height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var t = new Target
            {
                Ptr = (byte*)pin.AddrOfPinnedObject(),
                Width = width, Height = height, Stride = width * 4, Bpp = 4
            };
            long target = ToHandle(t);
            try
            {
                bool transparent =
                    clearR == 0f && clearG == 0f && clearB == 0f && clearA == 0f;
                ClearTarget(target, clearR, clearG, clearB, clearA, !transparent);
                float rad = (float)(softDiameter / 2.0);
                FillEllipse(target, (float)cx, (float)cy, rad, rad,
                    1f, spriteR, spriteG, spriteB, spriteA);
            }
            finally
            {
                FreeHandle(target);
            }
            float[] buf = new float[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                uint p = pixels[i];
                buf[i * 4 + 0] = (p & 0xFF) / 255f;
                buf[i * 4 + 1] = ((p >> 8) & 0xFF) / 255f;
                buf[i * 4 + 2] = ((p >> 16) & 0xFF) / 255f;
                buf[i * 4 + 3] = ((p >> 24) & 0xFF) / 255f;
            }
            if (hardness < 1.0 && blurRadius > 0.0)
            {
                for (int i = 0; i < width * height; i++)
                {
                    buf[i * 4 + 0] = SrgbToLinear(buf[i * 4 + 0]);
                    buf[i * 4 + 1] = SrgbToLinear(buf[i * 4 + 1]);
                    buf[i * 4 + 2] = SrgbToLinear(buf[i * 4 + 2]);
                }
                GaussBlur(buf, width, height, blurRadius / 3.0);
                for (int i = 0; i < width * height; i++)
                {
                    buf[i * 4 + 0] = LinearToSrgb(buf[i * 4 + 0]);
                    buf[i * 4 + 1] = LinearToSrgb(buf[i * 4 + 1]);
                    buf[i * 4 + 2] = LinearToSrgb(buf[i * 4 + 2]);
                }
            }
            if (invert != 0)
            {
                for (int i = 0; i < width * height; i++)
                {
                    buf[i * 4 + 0] = 1f - buf[i * 4 + 0];
                    buf[i * 4 + 1] = 1f - buf[i * 4 + 1];
                    buf[i * 4 + 2] = 1f - buf[i * 4 + 2];
                }
            }
            for (int i = 0; i < width * height; i++)
            {
                float r = Math.Clamp(buf[i * 4 + 0], 0f, 1f);
                float g = Math.Clamp(buf[i * 4 + 1], 0f, 1f);
                float b = Math.Clamp(buf[i * 4 + 2], 0f, 1f);
                // CopyBToA: RGB passes through, alpha comes from blue
                // (the blurred alpha channel is dropped, like D2D).
                uint bb = (uint)(b * 255f + 0.5f);
                pixels[i] = bb
                    | ((uint)(g * 255f + 0.5f) << 8)
                    | ((uint)(r * 255f + 0.5f) << 16)
                    | (bb << 24);
            }
        }
        finally
        {
            pin.Free();
        }
        return ToHandle(new Bitmap { Pixels = pixels, Width = width, Height = height });
    }

    private static byte[]? s_controlScratch;
    private static GCHandle s_controlScratchPin;
    private static readonly object s_scratchLock = new();

    // Resource creation (wheel bitmaps, effect inputs) needs a device
    // context object but never draws to it: hand out throwaway targets
    // over one shared scratch buffer. Handles are freed by the context
    // Dispose; the buffer stays pinned for process lifetime.
    public static long GetControlScratchTarget()
    {
        lock (s_scratchLock)
        {
            if (s_controlScratch is null)
            {
                s_controlScratch = new byte[64 * 64 * 4];
                s_controlScratchPin = GCHandle.Alloc(s_controlScratch, GCHandleType.Pinned);
            }
            return CreateTarget(s_controlScratchPin.AddrOfPinnedObject(), 64, 64, 64 * 4, 4);
        }
    }

    // Procedural transparency checkerboard for the neutered D2D
    // CheckerboardEffect: alternating tiles over a rect. Colors are
    // BGRA32 uints; tiles tile from the target origin like fills, so
    // control wells align with the desktop layout.
    // Target extents for effect fallback: the crop rect of a device
    // effect is unreadable headless, and the crop always covers the
    // control the host sized the target for.
    public static void GetTargetSize(long ctx, out int width, out int height)
    {
        if (ctx == 0)
        {
            width = 0;
            height = 0;
            return;
        }
        Target t = FromHandle<Target>(ctx);
        width = t.Width;
        height = t.Height;
    }

    // Gradient fills (LinearGradientBrush providers). Stops arrive as
    // a duck-typed object (the PDN GradientStopCollection): this
    // assembly stays reference-free by reading Color/Offset/R/G/B/A
    // through corlib reflection only, never binding PDN types. The
    // sampled LUT is straight RGBA with opacity baked; spread wraps
    // per pixel at stamp time. interp: 0 = sRGB lerp, 1 = linear-light.
    private const int GradientLutSize = 256;

    private static float ToFloat(object? v) =>
        v is null ? 0f : Convert.ToSingle(v);

    private static List<(float Offset, float R, float G, float B, float A)> ReadGradientStops(object? stops)
    {
        var pts = new List<(float, float, float, float, float)>();
        if (stops is not System.Collections.IEnumerable items)
            return pts;
        foreach (object? item in items)
        {
            if (item is null)
                continue;
            Type it = item.GetType();
            var offProp = it.GetProperty("Offset");
            var colProp = it.GetProperty("Color");
            if (offProp is null || colProp is null)
                continue;
            float off;
            try { off = (float)Convert.ToDouble(offProp.GetValue(item)); }
            catch { continue; }
            object? c = null;
            try { c = colProp.GetValue(item); }
            catch { continue; }
            if (c is null)
                continue;
            Type ct = c.GetType();
            float r, g, b, a;
            try
            {
                r = ToFloat(ct.GetProperty("R")?.GetValue(c));
                g = ToFloat(ct.GetProperty("G")?.GetValue(c));
                b = ToFloat(ct.GetProperty("B")?.GetValue(c));
                a = ToFloat(ct.GetProperty("A")?.GetValue(c));
            }
            catch { continue; }
            pts.Add((off, r, g, b, a));
        }
        return pts;
    }

    public static long CreateGradientLut(object? stops, float opacity, int interp)
    {
        float[] lut = new float[GradientLutSize * 4];
        var pts = ReadGradientStops(stops);
        if (pts.Count == 0)
            return ToHandle(lut);
        pts.Sort((p, q) => p.Offset.CompareTo(q.Offset));
        float op = Math.Clamp(opacity, 0f, 1f);
        bool linear = interp == 1;
        int n = pts.Count;
        for (int i = 0; i < GradientLutSize; i++)
        {
            float t = i / (float)(GradientLutSize - 1);
            (float Offset, float R, float G, float B, float A) p0 = pts[0], p1 = pts[n - 1];
            if (t <= p0.Offset) { p1 = p0; }
            else if (t >= p1.Offset) { p0 = p1; }
            else
            {
                for (int s = 0; s < n - 1; s++)
                {
                    if (t >= pts[s].Offset && t <= pts[s + 1].Offset)
                    {
                        p0 = pts[s];
                        p1 = pts[s + 1];
                        break;
                    }
                }
            }
            float span = p1.Offset - p0.Offset;
            float f = span <= 0f ? 0f : (t - p0.Offset) / span;
            float r, g, b;
            if (linear)
            {
                r = LinearToSrgb(SrgbToLinear(p0.R) + (SrgbToLinear(p1.R) - SrgbToLinear(p0.R)) * f);
                g = LinearToSrgb(SrgbToLinear(p0.G) + (SrgbToLinear(p1.G) - SrgbToLinear(p0.G)) * f);
                b = LinearToSrgb(SrgbToLinear(p0.B) + (SrgbToLinear(p1.B) - SrgbToLinear(p0.B)) * f);
            }
            else
            {
                r = p0.R + (p1.R - p0.R) * f;
                g = p0.G + (p1.G - p0.G) * f;
                b = p0.B + (p1.B - p0.B) * f;
            }
            float a = (p0.A + (p1.A - p0.A) * f) * op;
            lut[i * 4] = Math.Clamp(r, 0f, 1f);
            lut[i * 4 + 1] = Math.Clamp(g, 0f, 1f);
            lut[i * 4 + 2] = Math.Clamp(b, 0f, 1f);
            lut[i * 4 + 3] = Math.Clamp(a, 0f, 1f);
        }
        return ToHandle(lut);
    }

    private static float WrapSpread(float t, int spread)
    {
        if (spread == 1)
            return t - MathF.Floor(t);
        if (spread == 2)
        {
            float m = t % 2f;
            if (m < 0f)
                m += 2f;
            return m > 1f ? 2f - m : m;
        }
        return Math.Clamp(t, 0f, 1f);
    }

    public static void FillRectGradient(
        long ctx, float x, float y, float w, float h,
        long lut, float x0, float y0, float x1, float y1, int spread)
    {
        if (ctx == 0 || lut == 0 || w <= 0f || h <= 0f)
            return;
        Target t = FromHandle<Target>(ctx);
        float[] table = FromHandle<float[]>(lut);
        float rx0 = x * t.M11 + y * t.M21 + t.M31;
        float ry0 = x * t.M12 + y * t.M22 + t.M32;
        float rx1 = (x + w) * t.M11 + (y + h) * t.M21 + t.M31;
        float ry1 = (x + w) * t.M12 + (y + h) * t.M22 + t.M32;
        int ix0 = Math.Max(0, (int)MathF.Floor(Math.Min(rx0, rx1)));
        int iy0 = Math.Max(0, (int)MathF.Floor(Math.Min(ry0, ry1)));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(Math.Max(rx0, rx1)));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(Math.Max(ry0, ry1)));
        float det = t.M11 * t.M22 - t.M12 * t.M21;
        if (det == 0f)
            return;
        float ia = t.M22 / det, ib = -t.M12 / det, ic = -t.M21 / det, id = t.M11 / det;
        float dx = x1 - x0, dy = y1 - y0;
        float len2 = dx * dx + dy * dy;
        for (int yy = iy0; yy <= iy1; yy++)
        {
            for (int xx = ix0; xx <= ix1; xx++)
            {
                float qx = (xx + 0.5f) - t.M31, qy = (yy + 0.5f) - t.M32;
                float ux = ia * qx + ic * qy, uy = ib * qx + id * qy;
                float tt = len2 <= 0f ? 0f : ((ux - x0) * dx + (uy - y0) * dy) / len2;
                tt = WrapSpread(tt, spread);
                float pos = tt * (GradientLutSize - 1);
                int i0 = Math.Clamp((int)pos, 0, GradientLutSize - 1);
                int i1 = Math.Min(i0 + 1, GradientLutSize - 1);
                float f = pos - (int)pos;
                float r = table[i0 * 4] + (table[i1 * 4] - table[i0 * 4]) * f;
                float g = table[i0 * 4 + 1] + (table[i1 * 4 + 1] - table[i0 * 4 + 1]) * f;
                float b = table[i0 * 4 + 2] + (table[i1 * 4 + 2] - table[i0 * 4 + 2]) * f;
                float a = table[i0 * 4 + 3] + (table[i1 * 4 + 3] - table[i0 * 4 + 3]) * f;
                if (t.Bpp == 4)
                    BlendPixel(t, xx, yy, a, r, g, b);
                else
                    BlendPixelMask(t, xx, yy, a);
            }
        }
    }

    private static bool s_proxyShapeLogged;

    // Effect-proxy source unwrap for DrawImage (palette swatches draw
    // through a ColorManagementEffect proxy): pass-through effects
    // (color management, crop, bitmap source) carry the painted source
    // at input 0, so drawing it raw is exact headless. Anything else
    // answers null and the synth stays loud. Never throws.
    private static bool IsEffectProxy(object? o)
    {
        if (o is null) return false;
        Type t = o.GetType();
        if (t.FullName is string fn && fn.Contains("DeviceEffectProxy")) return true;
        bool hasEffect = false, hasProxy = false;
        foreach (var ii in t.GetInterfaces())
        {
            if (ii.IsGenericType && ii.Name == "IDeviceEffectProxy`1") return true;
            if (ii.Name == "IDeviceEffectProxy") return true;
            if (ii.Name == "IDeviceEffect") hasEffect = true;
            if (ii.Name == "IObjectRefProxy") hasProxy = true;
        }
        return hasEffect && hasProxy;
    }

    // Latest canvas composite bitmap (ColorManagementEffect-rooted
    // unwrap): the only post-commit stroke holder reachable from our
    // code. The strip mixer thumbnails it when the provider overlay is
    // content-free (single-layer: exact; multi-layer: composite
    // approximation). One object reference; PDN owns the pixels.
    // Live doc composite fed by the host (RGBA bytes, full doc dims)
    // for layer strip thumbs when the provider overlay is content-free.
    // Single slot, latest wins; no copy (holds the upload buffer ref).
    private sealed class DocRgba
    {
        public byte[] Rgba = System.Array.Empty<byte>();
        public int W;
        public int H;
    }

    // Blank-doc feed (undo-to-empty): all-white or all-transparent
    // full-doc buffer so thumbs clear instead of going stale (surface
    // reads return null for uniform surfaces).
    public static bool NoteDocBlank(int w, int h, bool white)
    {
        try
        {
            if (w <= 0 || h <= 0 || (long)w * h * 4 > 64L * 1024 * 1024)
                return false;
            byte[] rgba = new byte[w * h * 4];
            if (white)
                System.Array.Fill<byte>(rgba, 0xFF);
            s_docComposite = new DocRgba { Rgba = rgba, W = w, H = h };
            int sig = white ? 1 : 2;
            int dark = 0;
            int prev = System.Threading.Interlocked.Exchange(ref s_docFeedSig, sig);
            int prevDark = System.Threading.Interlocked.Exchange(ref s_docFeedDark, dark);
            return sig != prev || dark != prevDark;
        }
        catch { return false; }
    }

    private static DocRgba? s_docComposite;
    private static int s_docFeedSig;
    private static int s_docFeedDark = -1;

    // Returns true when the fed composite differs from the previously
    // fed one (the caller re-invalidates layer thumbs on change).
    public static bool NoteDocComposite(byte[] rgba, int w, int h)
    {
        try
        {
            if (rgba is null || w <= 0 || h <= 0 || rgba.Length != w * h * 4)
                return false;
            s_docComposite = new DocRgba { Rgba = rgba, W = w, H = h };
            int sig = 0;
            int dark = 0;
            int step = rgba.Length / 16384 + 1;
            for (int i = 0; i + 3 < rgba.Length; i += step)
            {
                sig = (sig * 31) ^ rgba[i];
                if (rgba[i] + rgba[i + 1] + rgba[i + 2] < 300 && rgba[i + 3] > 128)
                    dark++;
            }
            int prev = System.Threading.Interlocked.Exchange(ref s_docFeedSig, sig);
            int prevDark = System.Threading.Interlocked.Exchange(ref s_docFeedDark, dark);
            return sig != prev || dark != prevDark;
        }
        catch { return false; }
    }

    private static int s_thumbLayerW;
    private static int s_thumbLayerH;

    private static bool OverlayHasContent(long handle)
    {
        try
        {
            Bitmap ob = FromHandle<Bitmap>(handle);
            uint[] px = ob.Pixels;
            int n = px.Length;
            if (n <= 0 || ob.Width <= 0 || ob.Height <= 0)
                return false;
            if (n != ob.Width * ob.Height)
                return false;
            for (int i = 0; i < n; i++)
            {
                uint p = px[i];
                uint a = p >> 24;
                if (a <= 128)
                    continue;
                uint lum = (((p >> 16) & 0xFF) + ((p >> 8) & 0xFF) + (p & 0xFF)) / 3;
                if (lum < 128)
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static object? GetProxySourceImage(object? image)
    {
        try
        {
            if (image is null)
                return null;
            Type t = image.GetType();
            if (!s_proxyShapeLogged)
            {
                s_proxyShapeLogged = true;
                try
                {
                    string ifs = string.Join(";", t.GetInterfaces().Select(i => i.FullName));
                    Console.WriteLine("[PROXYSHAPE] " + t.FullName + " ifs=" + ifs);
                }
                catch { }
            }
            string? effectName = null;
            foreach (var ii in t.GetInterfaces())
            {
                if (ii.IsGenericType && ii.Name == "IDeviceEffectProxy`1")
                {
                    var props = ii.GetGenericArguments()[0];
                    effectName = props.DeclaringType?.FullName ?? props.FullName;
                    break;
                }
            }
            object? input = null;
            try
            {
                var propsProp = t.GetMethod("get_Properties", Type.EmptyTypes);
                object? bag = propsProp?.Invoke(image, null);
                var srcProp = bag?.GetType().GetMethod("get_Source", Type.EmptyTypes);
                object? acc = srcProp?.Invoke(bag, null);
                var get = acc?.GetType().GetMethod("Get", Type.EmptyTypes);
                input = get?.Invoke(acc, null);
                if (input is not null)
                    effectName ??= bag?.GetType().DeclaringType?.FullName
                        ?? bag?.GetType().FullName;
            }
            catch { }
            if (input is null)
            {
                var getInput = t.GetMethod("GetInput", [typeof(int)])
                    ?? t.GetMethod("GetInput", Type.EmptyTypes);
                if (getInput is null)
                    return null;
                try
                {
                    input = getInput.GetParameters().Length == 0
                        ? getInput.Invoke(image, null)
                        : getInput.Invoke(image, [0]);
                }
                catch { return null; }
            }
            effectName ??= t.FullName;
            if (effectName is null)
                return null;
            bool knownPassThrough = effectName.Contains("ColorManagementEffect")
                || effectName.Contains("CropEffect")
                || effectName.Contains("BitmapSourceEffect");
            if (!knownPassThrough)
            {
                try
                {
                    Console.WriteLine("[PROXYDRAW] drew input for " + effectName);
                }
                catch { }
            }
            // Unwrap shells, proxies, and BitmapImage wrappers to a raw
            // device bitmap: DrawImage only blits those, so an
            // effect-drawn upload (e.g. the color wheel) otherwise falls
            // through to the loud unknown-image throw. Layers repeat
            // until fixpoint (BitmapImage.image is itself a proxy that
            // needs another shell/proxy round); every inner loop is
            // guard-capped and the pass exits when nothing moves.
            for (int pass = 0; pass < 3; pass++)
            {
                object? before = input;
                int guard = 0;
                while (input is not null && guard++ < 4)
                {
                    Type it = input.GetType();
                    string? fn = it.FullName;
                    if (fn is null
                        || (!fn.Contains("ComObjectRef") && !fn.Contains("ObjectRef")
                        && !fn.Contains("Proxy")))
                        break;
                    var inner = it.GetField("_inner",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic);
                    object? innerVal = null;
                    try { innerVal = inner?.GetValue(input); }
                    catch { break; }
                    if (innerVal is null)
                    {
                        // ObjectRefProxy<T> shells (e.g. IDeviceImageProxy)
                        // expose InnerRef instead of an _inner field. Any
                        // single lookup here can throw AmbiguousMatch
                        // (explicit interface re-implementations), so
                        // enumerate and take the first index-free hit.
                        System.Reflection.PropertyInfo? innerProp = null;
                        try
                        {
                            foreach (var cand in it.GetProperties(
                                System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.NonPublic))
                            {
                                if (cand.Name == "InnerRef"
                                    && cand.GetIndexParameters().Length == 0)
                                { innerProp = cand; break; }
                            }
                        }
                        catch { break; }
                        if (innerProp is null)
                            break;
                        try { innerVal = innerProp.GetValue(input); }
                        catch { break; }
                    }
                    if (innerVal is null || ReferenceEquals(innerVal, input))
                        break;
                    input = innerVal;
                }
                guard = 0;
                while (input is not null && guard++ < 4 && IsEffectProxy(input))
                {
                    object? deeper = GetProxySourceImage(input);
                    if (deeper is null || ReferenceEquals(deeper, input))
                        break;
                    input = deeper;
                }
                guard = 0;
                while (input is not null && guard++ < 4
                    && (input.GetType().FullName?.Contains("BitmapImage") ?? false))
                {
                    var imageField = input.GetType().GetField("image",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic);
                    object? innerBmp = null;
                    try { innerBmp = imageField?.GetValue(input); }
                    catch { break; }
                    if (innerBmp is null || ReferenceEquals(innerBmp, input))
                        break;
                    input = innerBmp;
                }
                // Mid-chain effects (e.g. PrecisionEffect behind a proxy)
                // need a fresh props-chain round: recurse so the entry
                // scan re-runs for the new object. Gated to non-entry
                // objects so the original image never re-enters here.
                guard = 0;
                while (input is not null && guard++ < 2
                    && !ReferenceEquals(input, image)
                    && input.GetType().GetMethod("get_Properties", Type.EmptyTypes) is not null)
                {
                    object? deeper2 = GetProxySourceImage(input);
                    if (deeper2 is null || ReferenceEquals(deeper2, input))
                        break;
                    input = deeper2;
                }
                if (ReferenceEquals(input, before))
                    break;
            }
            try { Console.WriteLine("[PROXYOUT] in=" + t.FullName + " out=" + (input is null ? "null" : input.GetType().FullName)); }
            catch { }
            return input;
        }
        catch
        {
            return null;
        }
    }

    // Half-source device upload (checkbox bitmaps): our device
    // Bitmap is Bgra32-only, so widen premultiplied halves to bytes
    // here instead of teaching every blit a second pixel layout.
    // Pooled sources ARE their own lock (Buffer/BufferStride off
    // IBitmapLock, pool memory is stable); anything else answers
    // CopyPixels like any IBitmapSource. The result is an opaque
    // device handle, 0 when unreadable.
    private static bool s_halfUpLogged;

    private static object? InvokeGetter(System.Reflection.PropertyInfo p, object target)
    {
        try
        {
            var g = p.GetGetMethod(true) ?? p.GetGetMethod();
            if (g is null) return p.GetValue(target);
            return g.Invoke(target, null);
        }
        catch { return null; }
    }

    private static IntPtr UnboxPointer(object? v)
    {
        if (v is IntPtr ip) return ip;
        if (v is System.Reflection.Pointer)
        {
            try { unsafe { void* p = System.Reflection.Pointer.Unbox(v); return new IntPtr(p); } }
            catch { }
        }
        return IntPtr.Zero;
    }

    public static long CreateBitmapFromHalfSource(object? source, int w, int h)
    {
        string? why = null;
        GCHandle tempPin = default;
        bool tempPinned = false;
        try
        {
            if (source is null || w <= 0 || h <= 0)
            {
                why = "null-or-empty";
                return 0;
            }
            Type st = source.GetType();
            IntPtr halfPtr = IntPtr.Zero;
            int halfStride = 0;
            foreach (var ii in st.GetInterfaces())
            {
                if ((ii.Name != "IBitmapLock" && ii.Name != "ILockedBitmap") || ii.IsGenericType)
                    continue;
                var bp = ii.GetProperty("Buffer");
                var sp = ii.GetProperty("BufferStride");
                if (bp is null || sp is null)
                    continue;
                try
                {
                    IntPtr ptr = UnboxPointer(InvokeGetter(bp, source));
                    object? sv = sp.GetValue(source);
                    if (ptr != IntPtr.Zero
                        && sv is int s && s >= w * 8)
                    {
                        halfPtr = ptr;
                        halfStride = s;
                        break;
                    }
                }
                catch { }
            }
            if (halfPtr == IntPtr.Zero)
            {
                try
                {
                    var bp = st.GetProperty("Buffer");
                    var sp = st.GetProperty("BufferStride");
                    IntPtr ptr = bp is null ? IntPtr.Zero : UnboxPointer(InvokeGetter(bp, source));
                    object? sv = sp?.GetValue(source);
                    if (ptr != IntPtr.Zero
                        && sv is int s && s >= w * 8)
                    {
                        halfPtr = ptr;
                        halfStride = s;
                    }
                }
                catch { }
            }
            if (halfPtr == IntPtr.Zero)
            {
                System.Reflection.MethodInfo? cp = null;
                foreach (var ii in st.GetInterfaces())
                {
                    if (ii.Name != "IBitmapSource" || ii.IsGenericType)
                        continue;
                    cp = ii.GetMethods().FirstOrDefault(m => m.Name == "CopyPixels"
                        && m.GetParameters().Length == 4
                        && m.GetParameters()[0].ParameterType == typeof(IntPtr));
                    if (cp is not null)
                        break;
                }
                cp ??= st.GetMethods(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name.EndsWith("CopyPixels", StringComparison.Ordinal)
                        && m.GetParameters().Length == 4
                        && m.GetParameters()[0].ParameterType == typeof(IntPtr));
                if (cp is null)
                {
                    why = "nocopypixels:" + st.FullName;
                    return 0;
                }
                halfStride = checked(w * 8);
                byte[] temp = new byte[checked(halfStride * h)];
                tempPin = GCHandle.Alloc(temp, GCHandleType.Pinned);
                tempPinned = true;
                halfPtr = tempPin.AddrOfPinnedObject();
                try
                {
                    cp.Invoke(source, [halfPtr, halfStride, halfStride * h, null]);
                }
                catch (Exception ex)
                {
                    why = "copythrow:" + ex.GetType().Name;
                    return 0;
                }
            }
            uint[] bgra = new uint[w * h];
            unsafe
            {
                byte* basePtr = (byte*)halfPtr;
                for (int y = 0; y < h; y++)
                {
                    Half* s = (Half*)(basePtr + (nint)y * halfStride);
                    for (int x = 0; x < w; x++)
                    {
                        float a = Math.Clamp((float)s[x * 4 + 3], 0f, 1f);
                        float inv = a > 1e-6f ? 1f / a : 0f;
                        uint A = (uint)(a * 255f + 0.5f);
                        uint R = (uint)(Math.Clamp((float)s[x * 4 + 0] * inv, 0f, 1f) * 255f + 0.5f);
                        uint G = (uint)(Math.Clamp((float)s[x * 4 + 1] * inv, 0f, 1f) * 255f + 0.5f);
                        uint B = (uint)(Math.Clamp((float)s[x * 4 + 2] * inv, 0f, 1f) * 255f + 0.5f);
                        bgra[y * w + x] = (A << 24) | (R << 16) | (G << 8) | B;
                    }
                }
            }
            return ToHandle(new Bitmap { Pixels = bgra, Width = w, Height = h });
        }
        catch (Exception ex)
        {
            why = "throw:" + ex.GetType().Name + ":" + ex.Message;
            return 0;
        }
        finally
        {
            if (tempPinned)
                tempPin.Free();
            if (!s_halfUpLogged)
            {
                s_halfUpLogged = true;
                try
                {
                    string sst = source?.GetType().FullName ?? "?";
                    string ifs = source is null ? "" : string.Join(";",
                        source.GetType().GetInterfaces().Select(i => i.Name));
                    Console.WriteLine("[HALFUP] why=" + (why ?? "ok")
                        + " src=" + sst + " ifs=" + ifs + " w=" + w + " h=" + h);
                }
                catch { }
            }
        }
    }

    // Managed Bgra32 <-> Prgba64Half bridge for layer thumbnails
    // (rev134): every PDN pixel-format conversion funnels into the
    // WIC imaging factory, whose native COM sidecars cannot load in
    // the browser (OOB trap observed live in ToScRgb during the
    // inline thumbnail drain). Both directions below are pure
    // managed code with the SAME no-EOTF convention as
    // CreateBitmapFromHalfSource, so a render->consume roundtrip is
    // the identity (modulo Half precision): thumbnails show exact
    // layer bytes. All PDN-typed access in the wrappers is
    // reflection-only; failures return null (blank thumb, no crash).
    public static byte[] ThumbBgra32ToHalfBytes(byte[] bgra, int stride, int width, int height)
    {
        if (bgra is null)
            throw new ArgumentNullException(nameof(bgra));
        if (width <= 0 || height <= 0 || stride < width * 4)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (bgra.Length < checked(stride * height))
            throw new ArgumentException("short buffer", nameof(bgra));
        byte[] half = new byte[checked(width * height * 8)];
        for (int y = 0; y < height; y++)
        {
            int s = y * stride;
            int d = y * width * 8;
            for (int x = 0; x < width; x++)
            {
                int si = s + x * 4;
                float a = bgra[si + 3] / 255f;
                float r = bgra[si + 2] / 255f * a;
                float g = bgra[si + 1] / 255f * a;
                float b = bgra[si + 0] / 255f * a;
                int di = d + x * 8;
                BitConverter.GetBytes((Half)r).CopyTo(half, di);
                BitConverter.GetBytes((Half)g).CopyTo(half, di + 2);
                BitConverter.GetBytes((Half)b).CopyTo(half, di + 4);
                BitConverter.GetBytes((Half)a).CopyTo(half, di + 6);
            }
        }
        return half;
    }

    public static byte[] ThumbHalfToBgra32Bytes(byte[] half, int stride, int width, int height)
    {
        if (half is null)
            throw new ArgumentNullException(nameof(half));
        if (width <= 0 || height <= 0 || stride < width * 8)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (half.Length < checked(stride * height))
            throw new ArgumentException("short buffer", nameof(half));
        byte[] bgra = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int s = y * stride;
            int d = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int si = s + x * 8;
                float r = (float)BitConverter.ToHalf(half, si);
                float g = (float)BitConverter.ToHalf(half, si + 2);
                float b = (float)BitConverter.ToHalf(half, si + 4);
                float a = Math.Clamp((float)BitConverter.ToHalf(half, si + 6), 0f, 1f);
                float inv = a > 1e-6f ? 1f / a : 0f;
                uint A = (uint)(a * 255f + 0.5f);
                uint R = (uint)(Math.Clamp(r * inv, 0f, 1f) * 255f + 0.5f);
                uint G = (uint)(Math.Clamp(g * inv, 0f, 1f) * 255f + 0.5f);
                uint B = (uint)(Math.Clamp(b * inv, 0f, 1f) * 255f + 0.5f);
                int di = d + x * 4;
                bgra[di] = (byte)B;
                bgra[di + 1] = (byte)G;
                bgra[di + 2] = (byte)R;
                bgra[di + 3] = (byte)A;
            }
        }
        return bgra;
    }

    private static readonly object s_pdnTypeLock = new();
    private static readonly System.Collections.Generic.Dictionary<string, System.Type?> s_pdnTypes = new();

    private static System.Type? FindPdnType(string fullName)
    {
        lock (s_pdnTypeLock)
        {
            if (s_pdnTypes.TryGetValue(fullName, out System.Type? t))
                return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName, false); }
                catch { continue; }
                if (t is not null)
                    break;
            }
            s_pdnTypes[fullName] = t;
            return t;
        }
    }

    private static byte[] ReadLockedBytes(object bmp, string readWrite, int bpp)
    {
        System.Type bt = bmp.GetType();
        object? sizeObj = bt.GetMethod("get_Size", System.Type.EmptyTypes)?.Invoke(bmp, null)
            ?? throw new InvalidOperationException("nosize");
        int w = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Width", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
        int h = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Height", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
        if (w <= 0 || h <= 0 || bpp <= 0)
            throw new InvalidOperationException("badwh");
        System.Type? rectT = FindPdnType("PaintDotNet.Rendering.RectInt32")
            ?? throw new InvalidOperationException("norect");
        object? rect = Activator.CreateInstance(rectT, 0, 0, sizeObj)
            ?? throw new InvalidOperationException("norectobj");
        System.Reflection.MethodInfo? lockM = null;
        foreach (var m in bt.GetMethods())
        {
            if (m.Name == "Lock" && m.GetParameters().Length == 2)
            {
                lockM = m;
                break;
            }
        }
        if (lockM is null)
            throw new InvalidOperationException("nolock");
        object opt = Enum.Parse(lockM.GetParameters()[1].ParameterType, readWrite);
        object? lk = lockM.Invoke(bmp, new[] { rect, opt })
            ?? throw new InvalidOperationException("nolockobj");
        try
        {
            int lstride = System.Convert.ToInt32(lk.GetType().GetProperty("BufferStride")?.GetValue(lk));
            uint lsize = System.Convert.ToUInt32(lk.GetType().GetProperty("BufferSize")?.GetValue(lk));
            IntPtr ptr = IntPtr.Zero;
            foreach (var m in lk.GetType().GetMethods())
            {
                if (m.Name == "get_Buffer" && m.GetParameters().Length == 0)
                {
                    ptr = UnboxPointer(m.Invoke(lk, null));
                    break;
                }
            }
            if (ptr == IntPtr.Zero)
                throw new InvalidOperationException("nobuf");
            // Proxy locks (IBitmapLockProxy) alias a packed subrect:
            // BufferSize can be smaller than stride*h while still
            // holding every row contiguously. Trust the smaller of
            // the two, but never less than the packed pixels.
            long need = (long)lstride * h;
            long packed = (long)w * bpp * h;
            if (need <= 0 || (ulong)lsize < (ulong)packed)
                throw new InvalidOperationException("badsize w=" + w + " h=" + h
                    + " stride=" + lstride + " bufsize=" + lsize
                    + " lock=" + lk.GetType().FullName + " bmp=" + bt.FullName);
            int readLen = (int)Math.Min(need, (long)lsize);
            byte[] bytes = new byte[readLen];
            Marshal.Copy(ptr, bytes, 0, readLen);
            return bytes;
        }
        finally
        {
            (lk as IDisposable)?.Dispose();
        }
    }

    // Managed Half bridge: builds an IccBitmap<ColorPrgba64Half> from
    // half-float bytes for provider thumbnails (headless has no native
    // scaler/WIC path, so delivery goes through this).
    private static object? BuildHalfBitmap(byte[] half, int w, int h, object? sizeObj)
    {
        try
        {
            System.Type? halfPx = FindPdnType("PaintDotNet.Imaging.ColorPrgba64Half");
            System.Type? iccOpen = FindPdnType("PaintDotNet.Imaging.IccBitmap`1");
            System.Type? profiles = FindPdnType("PaintDotNet.Imaging.ColorProfiles");
            if (halfPx is null || iccOpen is null || profiles is null)
                return null;
            object? scrgb = profiles.GetMethod("get_ScRgb", System.Type.EmptyTypes)?.Invoke(null, null);
            if (sizeObj is null)
            {
                System.Type? sizeT = FindPdnType("PaintDotNet.Rendering.SizeInt32");
                if (sizeT is null)
                    return null;
                sizeObj = Activator.CreateInstance(sizeT, w, h);
            }
            if (scrgb is null || sizeObj is null)
                return null;
            object? halfBmp = Activator.CreateInstance(iccOpen.MakeGenericType(halfPx), sizeObj, scrgb);
            if (halfBmp is null)
                return null;
            System.Reflection.MethodInfo? wlock = null;
            foreach (var m in halfBmp.GetType().GetMethods())
            {
                if (m.Name == "Lock" && m.GetParameters().Length == 2)
                {
                    wlock = m;
                    break;
                }
            }
            if (wlock is null)
                return null;
            System.Type? rectT = FindPdnType("PaintDotNet.Rendering.RectInt32");
            if (rectT is null)
                return null;
            object? rect = Activator.CreateInstance(rectT, 0, 0, sizeObj);
            if (rect is null)
                return null;
            object wopt = Enum.Parse(wlock.GetParameters()[1].ParameterType, "Write");
            object? wlk = wlock.Invoke(halfBmp, new[] { rect, wopt });
            if (wlk is null)
                return null;
            try
            {
                uint lsize = System.Convert.ToUInt32(wlk.GetType().GetProperty("BufferSize")?.GetValue(wlk));
                IntPtr ptr = IntPtr.Zero;
                foreach (var m in wlk.GetType().GetMethods())
                {
                    if (m.Name == "get_Buffer" && m.GetParameters().Length == 0)
                    {
                        ptr = UnboxPointer(m.Invoke(wlk, null));
                        break;
                    }
                }
                if (ptr == IntPtr.Zero || (ulong)lsize < (ulong)half.Length)
                    return null;
                Marshal.Copy(half, 0, ptr, half.Length);
            }
            finally
            {
                (wlk as IDisposable)?.Dispose();
            }
            return halfBmp;
        }
        catch
        {
            return null;
        }
    }

    // CPU downscale of the layer surface for the LayersStrip provider
    // (its native scaler OOBs at any size). Falls back to null (caller
    // emits blank) on any failure.
    private static object? RenderLayerSurfaceThumb(object provider, int side)
    {
        try
        {
            object? layer = null;
            foreach (var f in provider.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
            {
                if (f.Name == "layer")
                {
                    try { layer = f.GetValue(provider); } catch { }
                    break;
                }
            }
            if (layer is null)
                return null;
            // Desktop renders thumbs from the layer renderer (live device
            // pixels); layer.Surface goes stale headless (stamps land in
            // the device bitmap via write-back, never syncing back), so a
            // surface-read thumb never shows edits. Prefer the renderer
            // source; the surface path below stays as fallback.
            try
            {
                if (TryReadLayerRendererPixels(provider, out byte[]? rsrc, out int rsw, out int rsh, out int rsstride)
                    && rsrc is not null)
                    return DownscaleSurfaceToHalfThumb(rsrc, rsstride, rsw, rsh, side);
            }
            catch { }
            System.Reflection.PropertyInfo? surfProp = layer.GetType().GetProperty("Surface");
            if (surfProp is null)
                return null;
            object? surf = null;
            try { surf = surfProp.GetValue(layer, null); } catch { }
            if (surf is null)
                return null;
            System.Type st = surf.GetType();
            int sw = System.Convert.ToInt32(st.GetProperty("Width")?.GetValue(surf, null));
            int sh = System.Convert.ToInt32(st.GetProperty("Height")?.GetValue(surf, null));
            if (sw <= 0 || sh <= 0 || side <= 0)
                return null;
            // Classic CPU Surface: direct Scan0/Stride read, no lock.
            object? scanRaw = null;
            try { scanRaw = st.GetMethod("get_Scan0", System.Type.EmptyTypes)?.Invoke(surf, null); }
            catch { }
            int sstride = 0;
            try { sstride = System.Convert.ToInt32(st.GetMethod("get_Stride", System.Type.EmptyTypes)?.Invoke(surf, null)); }
            catch { }
            IntPtr scan0 = IntPtr.Zero;
            try { scan0 = UnboxPointer(scanRaw); }
            catch { }
            if (scan0 == IntPtr.Zero && scanRaw is not null)
            {
                // Scan0 may be a MemoryBlock wrapper: unwrap Pointer.
                try
                {
                    object? inner = scanRaw.GetType().GetProperty("Pointer")?.GetValue(scanRaw, null);
                    if (inner is not null)
                        scan0 = UnboxPointer(inner);
                }
                catch { }
            }
            if (scan0 == IntPtr.Zero || sstride < sw * 4)
                return null;
            byte[] src = new byte[sstride * sh];
            Marshal.Copy(scan0, src, 0, src.Length);
            return DownscaleSurfaceToHalfThumb(src, sstride, sw, sh, side);
        }
        catch
        {
            return null;
        }
    }

    // Box downscale of a Bgra32 source into a side x side Half thumb
    // (transparent pad). Shared by the renderer-first and surface
    // fallback sources.
    private static void RecordThumbLayerSize(int sw, int sh)
    {
        if (sw > 0 && sh > 0)
        {
            s_thumbLayerW = sw;
            s_thumbLayerH = sh;
        }
    }

    private static object? DownscaleSurfaceToHalfThumb(byte[] src, int sstride, int sw, int sh, int side)
    {
        RecordThumbLayerSize(sw, sh);
        try
        {
            if (src is null || sstride < sw * 4 || sw <= 0 || sh <= 0 || side <= 0)
                return null;
            // Fit inside side x side, transparent pad (zeros).
            int dw = side, dh = side;
            double k = System.Math.Min((double)side / sw, (double)side / sh);
            int iw = System.Math.Max(1, (int)(sw * k));
            int ih = System.Math.Max(1, (int)(sh * k));
            int ox = (dw - iw) / 2, oy = (dh - ih) / 2;
            byte[] dst = new byte[dw * dh * 4];
            for (int y = 0; y < ih; y++)
            {
                int sy = (int)((y + 0.5) / k);
                if (sy < 0) sy = 0; else if (sy >= sh) sy = sh - 1;
                for (int x = 0; x < iw; x++)
                {
                    int sx = (int)((x + 0.5) / k);
                    if (sx < 0) sx = 0; else if (sx >= sw) sx = sw - 1;
                    int si = sy * sstride + sx * 4, di = (((y + oy) * dw) + (x + ox)) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1]; dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
            byte[] half = ThumbBgra32ToHalfBytes(dst, dw * 4, dw, dh);
            return BuildHalfBitmap(half, dw, dh, null);
        }
        catch
        {
            return null;
        }
    }

    private static readonly object s_asBitmapSourceLock = new();
    private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo> s_asBitmapSourceCache = new();

    // Reads the layer renderer's live device pixels (Bgra32) via
    // RendererExtensions.AsBitmapSource + CopyPixels. This is the
    // desktop thumbnail source; layer.Surface goes stale headless.
    // Returns false (caller keeps the surface fallback) on any failure.
    private static bool TryReadLayerRendererPixels(object provider, out byte[]? bgra, out int w, out int h, out int stride)
    {
        bgra = null; w = 0; h = 0; stride = 0;
        try
        {
            object? renderer = null;
            foreach (var f in provider.GetType().GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public))
            {
                if (f.Name == "layerRenderer")
                {
                    try { renderer = f.GetValue(provider); } catch { }
                    break;
                }
            }
            if (renderer is null)
                return false;
            System.Type rt = renderer.GetType();
            System.Type? pixelT = null;
            foreach (var ii in rt.GetInterfaces())
            {
                if (ii.IsGenericType && ii.Name == "IRenderer`1")
                {
                    pixelT = ii.GetGenericArguments()[0];
                    break;
                }
            }
            if (pixelT is null)
                return false;
            string cacheKey = rt.FullName + "|" + pixelT.FullName;
            System.Reflection.MethodInfo? asSource;
            lock (s_asBitmapSourceLock)
            {
                if (!s_asBitmapSourceCache.TryGetValue(cacheKey, out asSource) || asSource is null)
                {
                    asSource = null;
                    System.Type? extT = FindPdnType("PaintDotNet.Rendering.RendererExtensions");
                    if (extT is not null)
                    {
                        System.Reflection.MethodInfo[] extMethods;
                        try { extMethods = extT.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); }
                        catch { extMethods = []; }
                        foreach (var m in extMethods)
                        {
                            string mname = "";
                            bool isGen = false;
                            try { mname = m.Name; isGen = m.IsGenericMethodDefinition; }
                            catch { continue; }
                            if (mname != "AsBitmapSource" || !isGen)
                                continue;
                            System.Reflection.ParameterInfo[] ps;
                            try { ps = m.GetParameters(); }
                            catch { continue; }
                            if (ps.Length != 1)
                                continue;
                            System.Reflection.MethodInfo? gm = null;
                            try
                            {
                                var gargs = m.GetGenericArguments();
                                if (gargs.Length == 1)
                                    gm = m.MakeGenericMethod(pixelT);
                                else if (gargs.Length == 2)
                                    gm = m.MakeGenericMethod(pixelT, pixelT);
                                else
                                    continue;
                            }
                            catch { continue; }
                            try
                            {
                                object? probe = gm.Invoke(null, [renderer]);
                                if (probe is not null)
                                {
                                    asSource = gm;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    if (asSource is not null)
                        s_asBitmapSourceCache[cacheKey] = asSource;
                }
            }
            if (asSource is null)
                return false;
            object? source = null;
            try { source = asSource.Invoke(null, [renderer]); }
            catch { return false; }
            if (source is null)
                return false;
            // Bgra32 only: the pixel generic arg must name a Bgra type.
            bool isBgra = false;
            try
            {
                foreach (var ii in source.GetType().GetInterfaces())
                {
                    if (ii.IsGenericType && ii.Name.StartsWith("IBitmapSource", StringComparison.Ordinal))
                    {
                        foreach (var ga in ii.GetGenericArguments())
                        {
                            if ((ga.FullName ?? ga.Name).Contains("Bgra"))
                                isBgra = true;
                        }
                    }
                }
            }
            catch { return false; }
            if (!isBgra)
                return false;
            object? sizeObj = null;
            try { sizeObj = source.GetType().GetMethod("get_Size", System.Type.EmptyTypes)?.Invoke(source, null); }
            catch { return false; }
            if (sizeObj is null)
                return false;
            int sw = 0, sh = 0;
            try
            {
                sw = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Width", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
                sh = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Height", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
            }
            catch { return false; }
            if (sw <= 0 || sh <= 0 || (long)sw * sh > 16777216L)
                return false;
            // Prefer Lock (Scan0 read, same as ReadLockedBytes): the
            // adapter inherits BitmapSourceBase, whose CopyPixels
            // overloads take typed pointers, not IntPtr.
            try
            {
                object? wlk = null;
                try
                {
                    System.Reflection.MethodInfo? lockM = null;
                    foreach (var m in source.GetType().GetMethods(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic))
                    {
                        if (m.Name == "Lock" && m.GetParameters().Length == 2)
                        {
                            lockM = m;
                            break;
                        }
                    }
                    if (lockM is not null)
                        wlk = lockM.Invoke(source, ["ReadOnly", null]);
                }
                catch { wlk = null; }
                if (wlk is not null)
                {
                    try
                    {
                        object? scanObj = wlk.GetType().GetProperty("Scan0")?.GetValue(wlk)
                            ?? wlk.GetType().GetMethod("get_Scan0", System.Type.EmptyTypes)?.Invoke(wlk, null);
                        object? strideObj = wlk.GetType().GetProperty("Stride")?.GetValue(wlk)
                            ?? wlk.GetType().GetMethod("get_Stride", System.Type.EmptyTypes)?.Invoke(wlk, null);
                        IntPtr s0 = scanObj is IntPtr p ? p : IntPtr.Zero;
                        if (scanObj is not null && s0 == IntPtr.Zero)
                        {
                            try { s0 = (IntPtr)System.Convert.ToInt64(scanObj); } catch { }
                        }
                        int sst = strideObj is null ? 0 : System.Convert.ToInt32(strideObj);
                        if (s0 != IntPtr.Zero && sst >= sw * 4)
                        {
                            byte[] lbuf = new byte[checked(sst * sh)];
                            Marshal.Copy(s0, lbuf, 0, lbuf.Length);
                            byte[] packed = new byte[checked(sw * 4 * sh)];
                            for (int yy = 0; yy < sh; yy++)
                                Buffer.BlockCopy(lbuf, yy * sst, packed, yy * sw * 4, sw * 4);
                            bgra = packed; w = sw; h = sh; stride = sw * 4;
                            return true;
                        }
                    }
                    finally
                    {
                        try { (wlk as IDisposable)?.Dispose(); } catch { }
                    }
                }
            }
            catch { }
            // 4-arg IntPtr CopyPixels into a pinned Bgra32 buffer (same
            // shape the Half upload path already uses).
            System.Reflection.MethodInfo? cp = null;
            try
            {
                foreach (var ii in source.GetType().GetInterfaces())
                {
                    if (ii.Name != "IBitmapSource" || ii.IsGenericType)
                        continue;
                    cp = ii.GetMethods().FirstOrDefault(m => m.Name == "CopyPixels"
                        && m.GetParameters().Length == 4
                        && m.GetParameters()[0].ParameterType == typeof(IntPtr));
                    if (cp is not null)
                        break;
                }
                cp ??= source.GetType().GetMethods(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name.EndsWith("CopyPixels", StringComparison.Ordinal)
                        && m.GetParameters().Length == 4
                        && m.GetParameters()[0].ParameterType == typeof(IntPtr));
            }
            catch { return false; }
            if (cp is null)
                return false;
            int sstride = checked(sw * 4);
            byte[] buf = new byte[checked(sstride * sh)];
            GCHandle pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                try { cp.Invoke(source, [pin.AddrOfPinnedObject(), sstride, buf.Length, null]); }
                catch { return false; }
            }
            finally
            {
                pin.Free();
            }
            bgra = buf; w = sw; h = sh; stride = sstride;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Render side: provider bitmap (Bgra32) -> Half bitmap, bypassing
    // ToScRgb/WIC. Owns and disposes the source bitmap.
    public static object? RenderLayerThumbnail(object? provider, int sideLength, object? priority, object? token)
    {
        object? bgra = null;
        try
        {
            if (provider is null || sideLength <= 0 || priority is null || token is null)
                return null;
            // The LayersStrip provider crashes at any size: CPU downscale
            // of the layer surface instead, blank 1x1 on any failure.
            string pname = provider.GetType().FullName ?? "?";
            if (pname.Contains("OurThumbnailProvider"))
            {
                object? cpuThumb = RenderLayerSurfaceThumb(provider, sideLength);
                if (cpuThumb is not null)
                    return cpuThumb;
                return BuildHalfBitmap(new byte[8], 1, 1, null);
            }
            int effSide = sideLength > 8 ? 8 : sideLength;
            System.Reflection.MethodInfo? render = null;
            foreach (var m in provider.GetType().GetMethods())
            {
                if (m.Name == "CreateThumbnailBitmap" && m.GetParameters().Length == 3
                    && m.GetParameters()[0].ParameterType == typeof(int))
                {
                    render = m;
                    break;
                }
            }
            if (render is null)
                return null;
            bgra = render.Invoke(provider, new object?[] { effSide, priority, token });
            if (bgra is null)
                return null;
            object? sizeObj = bgra.GetType().GetMethod("get_Size", System.Type.EmptyTypes)?.Invoke(bgra, null);
            if (sizeObj is null)
                return null;
            int w = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Width", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
            int h = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Height", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
            if (w <= 0 || h <= 0)
                return null;
            byte[] src = ReadLockedBytes(bgra, "Read", 4);
            int srcStride = src.Length / h;
            byte[] half = ThumbBgra32ToHalfBytes(src, srcStride, w, h);
            return BuildHalfBitmap(half, w, h, sizeObj);
        }
        catch
        {
            return null;
        }
        finally
        {
            (bgra as IDisposable)?.Dispose();
        }
    }

    // Consumption side: Half source -> Bgra32 bitmap for the device
    // upload, bypassing the WIC color transformer.
    public static object? ConvertHalfSourceToBgra(object? halfSrc)
    {
        try
        {
            if (halfSrc is null)
                return null;
            object? sizeObj = halfSrc.GetType().GetMethod("get_Size", System.Type.EmptyTypes)?.Invoke(halfSrc, null);
            if (sizeObj is null)
                return null;
            int w = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Width", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
            int h = System.Convert.ToInt32(sizeObj.GetType().GetMethod("get_Height", System.Type.EmptyTypes)?.Invoke(sizeObj, null));
            if (w <= 0 || h <= 0)
                return null;
            byte[] src = ReadLockedBytes(halfSrc, "Read", 8);
            int srcStride = src.Length / h;
            byte[] bgra = ThumbHalfToBgra32Bytes(src, srcStride, w, h);
            System.Type? bgraPx = FindPdnType("PaintDotNet.Imaging.ColorBgra32");
            System.Type? iccOpen = FindPdnType("PaintDotNet.Imaging.IccBitmap`1");
            if (bgraPx is null || iccOpen is null)
                return null;
            object? profile = null;
            try { profile = halfSrc.GetType().GetMethod("get_ColorProfile", System.Type.EmptyTypes)?.Invoke(halfSrc, null); }
            catch { }
            if (profile is null)
            {
                System.Type? profiles = FindPdnType("PaintDotNet.Imaging.ColorProfiles");
                profile = profiles?.GetMethod("get_Srgb", System.Type.EmptyTypes)?.Invoke(null, null);
            }
            if (profile is null)
                return null;
            object? bgraBmp = Activator.CreateInstance(iccOpen.MakeGenericType(bgraPx), sizeObj, profile);
            if (bgraBmp is null)
                return null;
            System.Reflection.MethodInfo? wlock = null;
            foreach (var m in bgraBmp.GetType().GetMethods())
            {
                if (m.Name == "Lock" && m.GetParameters().Length == 2)
                {
                    wlock = m;
                    break;
                }
            }
            System.Type? rectT = FindPdnType("PaintDotNet.Rendering.RectInt32");
            if (wlock is null || rectT is null)
                return null;
            object? rect = Activator.CreateInstance(rectT, 0, 0, sizeObj);
            if (rect is null)
                return null;
            object wopt = Enum.Parse(wlock.GetParameters()[1].ParameterType, "Write");
            object? wlk = wlock.Invoke(bgraBmp, new[] { rect, wopt });
            if (wlk is null)
                return null;
            try
            {
                uint lsize = System.Convert.ToUInt32(wlk.GetType().GetProperty("BufferSize")?.GetValue(wlk));
                IntPtr ptr = IntPtr.Zero;
                foreach (var m in wlk.GetType().GetMethods())
                {
                    if (m.Name == "get_Buffer" && m.GetParameters().Length == 0)
                    {
                        ptr = UnboxPointer(m.Invoke(wlk, null));
                        break;
                    }
                }
                if (ptr == IntPtr.Zero || (ulong)lsize < (ulong)bgra.Length)
                    return null;
                Marshal.Copy(bgra, 0, ptr, bgra.Length);
            }
            finally
            {
                (wlk as IDisposable)?.Dispose();
            }
            return bgraBmp;
        }
        catch
        {
            return null;
        }
    }

    public static bool DrawProceduralEffect(
        long ctx, object? effect, float x, float y, float w, float h)
    {
        // Procedural D2D effects (palette swatches draw a cropped
        // CheckerboardEffect through a proxy) have no bitmap to blit:
        // rasterize the known ones here instead of staying loud.
        // Unknown effects answer false and the synth keeps throwing.
        try
        {
            if (!s_procLogged) { s_procLogged = true; try { Console.WriteLine("[PROCEFFECT] ctx=" + ctx + " fx=" + (effect is null ? "null" : effect.GetType().FullName) + " x=" + x + " y=" + y + " w=" + w + " h=" + h); } catch { } }
            if (ctx == 0 || effect is null || w <= 0f || h <= 0f)
                return false;
            if (effect.GetType().FullName?.Contains("CheckerboardEffect") != true)
                return false;
            uint c1 = 0xFFFFFFFFu;
            uint c2 = 0xFFC8C8C8u;
            int tile = 8;
            try
            {
                object? props = effect.GetType()
                    .GetMethod("get_Properties", Type.EmptyTypes)
                    ?.Invoke(effect, null);
                if (props is not null)
                {
                    c1 = ReadEffectColor(props, "Color1", c1);
                    c2 = ReadEffectColor(props, "Color2", c2);
                    tile = ReadEffectTile(props, tile);
                }
            }
            catch { }
            DrawChecker(ctx, x, y, w, h, c1, c2, tile);
            return true;
        }
        catch { return false; }
    }

    // Draws a composite device image (ImageStrip item images are
    // CompositeEffect(checker-crop, thumbnail)): background inputs
    // procedurally, then the overlay bitmap. Answers false when the
    // image is not a resolvable composite so callers keep today's
    // procedural-or-throw behavior for everything else.
    public static bool DrawCompositeImage(
        long ctx, object? image,
        float dx, float dy, float dw, float dh, int hasDest,
        float sx, float sy, float sw, float sh, int hasSrc)
    {
        try
        {
            if (ctx == 0 || image is null)
                return false;
            // Strip item images are checker+thumbnail mixers: match ONLY
            // when a CPU upload and a checkerboard are both reachable.
            // Small-upload gate keeps canvas blends on today's path.
            object? overlay;
            object? checkerBg;
            try
            {
                bool matched = FindStripMixer(image, out overlay, out checkerBg)
                    && overlay is not null && checkerBg is not null;
                if (!matched)
                    return false;
            }
            catch { return false; }
            if (overlay is null || checkerBg is null)
                return false;
            int bw = 0, bh = 0;
            long handle = 0;
            try
            {
                bw = System.Convert.ToInt32(overlay.GetType().GetField("_width")?.GetValue(overlay));
                bh = System.Convert.ToInt32(overlay.GetType().GetField("_height")?.GetValue(overlay));
                handle = System.Convert.ToInt64(overlay.GetType().GetField("_handle")?.GetValue(overlay));
            }
            catch { }
            if (bw <= 0 || bh <= 0 || handle == 0 || bw > 128 || bh > 128)
                return false;
            // Live-doc thumb: when the provider overlay carries no
            // dark-opaque content, thumbnail the host-fed doc composite
            // instead. Downscale to the overlay dims so the dest math
            // below is unchanged; the upload is owned here and freed
            // after the blit.
            long upload = 0;
            try
            {
                DocRgba? live = s_docComposite;
                if (live is not null
                    && (live.Rgba.Length != live.W * live.H * 4
                        || (s_thumbLayerW > 0 && (live.W != s_thumbLayerW || live.H != s_thumbLayerH))))
                    live = null;
                if (live is not null && !OverlayHasContent(handle))
                {
                    // Darkest-of-footprint downscale so thin strokes
                    // survive the shrink to strip size.
                    uint[] small = new uint[bw * bh];
                    for (int yy = 0; yy < bh; yy++)
                    {
                        int y0 = yy * live.H / bh;
                        int y1 = (yy + 1) * live.H / bh;
                        if (y1 <= y0) y1 = y0 + 1;
                        if (y0 < 0) y0 = 0;
                        if (y1 > live.H) y1 = live.H;
                        for (int xx = 0; xx < bw; xx++)
                        {
                            int x0 = xx * live.W / bw;
                            int x1 = (xx + 1) * live.W / bw;
                            if (x1 <= x0) x1 = x0 + 1;
                            if (x0 < 0) x0 = 0;
                            if (x1 > live.W) x1 = live.W;
                            uint best = 0xFFFFFFFF;
                            uint bestSum = uint.MaxValue;
                            uint bestA = 0xFF;
                            for (int sy2 = y0; sy2 < y1; sy2++)
                            {
                                for (int sx2 = x0; sx2 < x1; sx2++)
                                {
                                    int si = (sy2 * live.W + sx2) * 4;
                                    uint r = live.Rgba[si];
                                    uint g = live.Rgba[si + 1];
                                    uint b2 = live.Rgba[si + 2];
                                    uint sum = r + g + b2;
                                    if (sum < bestSum)
                                    {
                                        bestSum = sum;
                                        best = b2 | (g << 8) | (r << 16);
                                        bestA = live.Rgba[si + 3];
                                    }
                                }
                            }
                            small[yy * bw + xx] = best | (bestA << 24);
                        }
                    }
                    GCHandle pin = GCHandle.Alloc(small, GCHandleType.Pinned);
                    try { upload = CreateBitmap(pin.AddrOfPinnedObject(), checked(bw * 4), bw, bh); }
                    finally { pin.Free(); }
                    if (upload != 0)
                        handle = upload;
                }
            }
            catch { }
            float destX = hasDest != 0 ? dx : 0f;
            float destY = hasDest != 0 ? dy : 0f;
            float destW = hasSrc != 0 ? sw : bw;
            float destH = hasSrc != 0 ? sh : bh;
            if (destW <= 0f || destH <= 0f)
                return false;
            DrawProceduralEffect(ctx, checkerBg, destX, destY, destW, destH);
            DrawBitmap(ctx, handle, destX, destY, destW, destH, true,
                0f, 0f, bw, bh, true, 1f, true);
            if (upload != 0)
            {
                try { FreeBitmap(upload); } catch { }
            }
            return true;
        }
        catch { return false; }
    }

    // Cycle-proof structural search for a checker+thumbnail mixer:
    // walks the effect graph iteratively (visited set + depth cap +
    // node budget, so hostile cycles terminate) and matches ONLY when
    // one node is a CPU upload and another a checkerboard. No unwrap
    // calls: identification is by type name alone. Canvas blends (no
    // checker node) never match.
    private static bool FindStripMixer(object root, out object? overlay, out object? checkerBg)
    {
        overlay = null;
        checkerBg = null;
        try
        {
            // Reference identity: PDN device objects may overload value
            // equality (headless null handles compare equal), which would
            // wrongly prune distinct chain links out of the walk.
            var seen = new System.Collections.Generic.HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            var stack = new System.Collections.Generic.Stack<(object Node, int Depth)>();
            stack.Push((root, 0));
            int budget = 60;
            while (stack.Count > 0 && budget-- > 0)
            {
                var (node, depth) = stack.Pop();
                if (node is null || !seen.Add(node))
                    continue;
                string? tn = null;
                try { tn = node.GetType().FullName; } catch { continue; }
                if (tn is not null && tn.Contains("CpuDeviceBitmap"))
                {
                    overlay ??= node;
                    continue;
                }
                if (tn is not null && tn.Contains("CheckerboardEffect"))
                {
                    checkerBg ??= node;
                    continue;
                }
                if (tn is not null && tn.Contains("Props"))
                {
                    // Property bags only repeat their wrapper's inputs
                    // (verified: bag visits resolve to the same effects):
                    // expanding them doubles the chain depth and burns
                    // the node budget for no new information.
                    continue;
                }
                if (tn is not null && tn.Contains("Proxy") && overlay is null)
                {
                    // The strip composite holds the thumb behind an
                    // IDeviceImageProxy: claim the underlying bitmap
                    // without spending walk depth on the wrapper. The
                    // proven proxy unwrap goes first (proxy fields do
                    // not always name an image type), then the field
                    // scan fallback.
                    try
                    {
                        object? unwrapped = null;
                        try { unwrapped = GetProxySourceImage(node); }
                        catch { }
                        string? utn = null;
                        try { utn = unwrapped?.GetType().FullName; } catch { }
                        if (utn is not null && utn.Contains("CpuDeviceBitmap"))
                            overlay = unwrapped;
                        else foreach (object? c in EffectInputs(node))
                        {
                            string? ctn = null;
                            try { ctn = c?.GetType().FullName; } catch { continue; }
                            if (ctn is not null && ctn.Contains("CpuDeviceBitmap"))
                            {
                                overlay = c;
                                break;
                            }
                        }
                    }
                    catch { }
                }
                // Depth 5: the spliced strip chain is outer crop ->
                // opacity -> [blur] -> composite -> (checker crop, thumb
                // bitmap), so the checker crop (depth 4) must still
                // expand. The node budget above bounds the total work.
                if (depth >= 5)
                    continue;
                System.Collections.Generic.List<object?> ins;
                try { ins = EffectInputs(node); }
                catch { continue; }
                foreach (object? c in ins)
                {
                    if (c is not null)
                        stack.Push((c, depth + 1));
                }
            }
            return overlay is not null && checkerBg is not null;
        }
        catch { overlay = null; checkerBg = null; return false; }
    }

    // Construction-time effect inputs, noted by the rewriter's
    // EffectInputAccessor.Set hook (see NoteEffectInput): the inner
    // device effect PDN constructs inputs against is ReplaceRef-swapped
    // for a fresh husk afterwards, so the husk's own input table is
    // empty at draw time. The Properties bag lives on the WRAPPER and
    // survives the swap, keyed per input index. Whole-body guarded:
    // a note must never fault effect construction.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Generic.Dictionary<int, object?>> s_effectInputNotes = new();

    public static void NoteEffectInput(object? storage, int index, object? image)
    {
        try
        {
            if (storage is null || index < 0 || index > 64)
                return;
            lock (s_effectInputNotes)
            {
                if (image is null)
                {
                    if (s_effectInputNotes.TryGetValue(storage, out var existing))
                        existing.Remove(index);
                    return;
                }
                if (!s_effectInputNotes.TryGetValue(storage, out var perIndex))
                {
                    perIndex = new System.Collections.Generic.Dictionary<int, object?>();
                    s_effectInputNotes.Add(storage, perIndex);
                }
                perIndex[index] = image;
            }
        }
        catch { }
    }

    // Effect inputs: the construction-time note table first (see
    // NoteEffectInput), then instance fields (whole hierarchy: sources
    // live in private base fields) whose type looks like an
    // image/effect, then the properties Source accessor, then GetInput
    // fallback. Field reads are pure data; the accessors are the same
    // ones the proven unwrap path uses every run.
    private static System.Collections.Generic.List<object?> EffectInputs(object effect)
    {
        var list = new System.Collections.Generic.List<object?>();
        try
        {
            // The Set hook keys notes by the inner husk (the accessor's
            // Storage resolves to the husk itself): a direct hit collapses
            // one chain level, keeping deep strip chains inside the
            // walk's depth budget.
            try
            {
                if (s_effectInputNotes.TryGetValue(effect, out var selfNotes))
                {
                    var orderedSelf = new System.Collections.Generic.List<int>(selfNotes.Keys);
                    orderedSelf.Sort();
                    foreach (int i in orderedSelf)
                    {
                        if (list.Count >= 8)
                            break;
                        object? notedSelf = selfNotes[i];
                        if (notedSelf is not null && !ReferenceEquals(notedSelf, effect))
                            list.Add(notedSelf);
                    }
                    if (list.Count > 0)
                        return list;
                }
            }
            catch { }
            try
            {
                var bagProp = effect.GetType().GetMethod("get_Properties", System.Type.EmptyTypes);
                object? bag = bagProp?.Invoke(effect, null);
                if (bag is not null && s_effectInputNotes.TryGetValue(bag, out var perIndex))
                {
                    var ordered = new System.Collections.Generic.List<int>(perIndex.Keys);
                    ordered.Sort();
                    foreach (int i in ordered)
                    {
                        if (list.Count >= 8)
                            break;
                        object? noted = perIndex[i];
                        if (noted is not null && !ReferenceEquals(noted, effect))
                            list.Add(noted);
                    }
                    if (list.Count > 0)
                        return list;
                }
            }
            catch { }
            for (System.Type? t = effect.GetType();
                t is not null && t != typeof(object) && list.Count < 8;
                t = t.BaseType)
            {
                System.Reflection.FieldInfo[] fields;
                try
                {
                    fields = t.GetFields(
                        System.Reflection.BindingFlags.DeclaredOnly
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic);
                }
                catch { break; }
                foreach (var f in fields)
                {
                    if (list.Count >= 8)
                        break;
                    // Declared-type pre-filter skips primitives only: the
                    // source often hides in object-typed fields, so the
                    // keep/drop decision uses the VALUE's runtime type.
                    string? tn = null;
                    try { tn = f.FieldType.FullName; } catch { continue; }
                    if (tn is null || tn == "System.String" || tn == "System.Int32"
                        || tn == "System.Single" || tn == "System.Boolean"
                        || tn == "System.Double" || tn == "System.Int64"
                        || tn == "System.Byte" || tn == "System.Char")
                        continue;
                    object? v;
                    try { v = f.GetValue(effect); } catch { continue; }
                    if (v is null || ReferenceEquals(v, effect))
                        continue;
                    if (v is System.Collections.IList il)
                    {
                        for (int i = 0; i < il.Count && list.Count < 8; i++)
                        {
                            try
                            {
                                object? e = il[i];
                                if (e is not null && !ReferenceEquals(e, effect))
                                    list.Add(e);
                            }
                            catch { break; }
                        }
                    }
                    else if (v is System.Collections.IDictionary dict)
                    {
                        // CPU effect stand-ins keep inputs in a Hashtable.
                        try
                        {
                            foreach (object? e in dict.Values)
                            {
                                if (list.Count >= 8)
                                    break;
                                if (e is not null && !ReferenceEquals(e, effect))
                                    list.Add(e);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        string? vtn = null;
                        try { vtn = v.GetType().FullName; } catch { continue; }
                        if (vtn is not null && (vtn.Contains("Image") || vtn.Contains("Effect")
                            || vtn.Contains("Bitmap") || vtn.Contains("Source")))
                        {
                            // Splice husk notes in place of the husk: the
                            // inner device effect carries its inputs in the
                            // note table (and its own _inputs), but every
                            // husk level costs walk depth. Replacing the
                            // husk with its noted inputs collapses the
                            // strip chain (crop->opacity->composite) to one
                            // level per wrapper. Falls back to the husk
                            // itself when nothing was noted.
                            bool spliced = false;
                            if (vtn.Contains("CpuDeviceEffect"))
                            {
                                try
                                {
                                    if (s_effectInputNotes.TryGetValue(v, out var huskNotes))
                                    {
                                        var orderedHusk = new System.Collections.Generic.List<int>(huskNotes.Keys);
                                        orderedHusk.Sort();
                                        int added = 0;
                                        foreach (int i in orderedHusk)
                                        {
                                            if (list.Count >= 8)
                                                break;
                                            object? notedHusk = huskNotes[i];
                                            if (notedHusk is not null && !ReferenceEquals(notedHusk, effect))
                                            { list.Add(notedHusk); added++; }
                                        }
                                        spliced = added > 0;
                                    }
                                }
                                catch { }
                            }
                            if (!spliced)
                                list.Add(v);
                        }
                    }
                }
            }
            if (list.Count == 0)
            {
                var propsProp = effect.GetType().GetMethod("get_Properties", System.Type.EmptyTypes);
                object? bag = propsProp?.Invoke(effect, null);
                var srcProp = bag?.GetType().GetMethod("get_Source", System.Type.EmptyTypes);
                object? acc = srcProp?.Invoke(bag, null);
                var get = acc?.GetType().GetMethod("Get", System.Type.EmptyTypes);
                object? via = get?.Invoke(acc, null);
                if (via is not null)
                    list.Add(via);
            }
            if (list.Count == 0)
            {
                var getInput = effect.GetType().GetMethod("GetInput", [typeof(int)]);
                if (getInput is not null)
                {
                    for (int i = 0; i < 8 && list.Count < 8; i++)
                    {
                        object? inp;
                        try { inp = getInput.Invoke(effect, [i]); }
                        catch { break; }
                        if (inp is null)
                            break;
                        list.Add(inp);
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private static bool s_procLogged;

    private static uint ReadEffectColor(object props, string name, uint fallback)
    {
        try
        {
            object? v = props.GetType()
                .GetMethod("get_" + name, Type.EmptyTypes)
                ?.Invoke(props, null);
            if (v is null)
                return fallback;
            if (v is uint u)
                return u;
            if (v is int i)
                return unchecked((uint)i);
            Type vt = v.GetType();
            // RGBA names (ColorBgra bytes, RGBA floats, Drawing.Color)
            // else XYZW fields (float4-style shaders).
            bool xyzw = GetMemberType(vt, "R") is null;
            string r = xyzw ? "X" : "R";
            string g = xyzw ? "Y" : "G";
            string b = xyzw ? "Z" : "B";
            string a = xyzw ? "W" : "A";
            if (GetMemberType(vt, r) is null)
                return fallback;
            uint R = ToEffectByte(v, vt, r);
            uint G = ToEffectByte(v, vt, g);
            uint B = ToEffectByte(v, vt, b);
            uint A = ToEffectByte(v, vt, a);
            return (A << 24) | (R << 16) | (G << 8) | B;
        }
        catch { return fallback; }
    }

    private static Type? GetMemberType(Type t, string name)
        => t.GetProperty(name)?.PropertyType ?? t.GetField(name)?.FieldType;

    private static uint ToEffectByte(object o, Type t, string name)
    {
        object? v = t.GetProperty(name)?.GetValue(o) ?? t.GetField(name)?.GetValue(o);
        double d = v is IConvertible c ? c.ToDouble(null) : 0.0;
        Type? mt = GetMemberType(t, name);
        bool scaled = mt == typeof(float) || mt == typeof(double) || mt == typeof(Half);
        return (uint)(Math.Clamp(scaled ? d : d / 255.0, 0.0, 1.0) * 255.0 + 0.5);
    }

    private static int ReadEffectTile(object props, int fallback)
    {
        try
        {
            object? v = props.GetType()
                .GetMethod("get_TileSize", Type.EmptyTypes)
                ?.Invoke(props, null);
            if (v is null)
                return fallback;
            if (v.GetType().IsPrimitive && v is IConvertible c)
                return Math.Clamp(c.ToInt32(null), 1, 256);
            double x = ReadEffectDouble(v, "X");
            if (x <= 0)
                x = ReadEffectDouble(v, "Width");
            if (x <= 0)
                return fallback;
            return Math.Clamp((int)x, 1, 256);
        }
        catch { return fallback; }
    }

    private static double ReadEffectDouble(object o, string name)
    {
        Type t = o.GetType();
        object? v = t.GetProperty(name)?.GetValue(o) ?? t.GetField(name)?.GetValue(o);
        return v is IConvertible c ? c.ToDouble(null) : 0.0;
    }

    public static void DrawChecker(
        long ctx, float x, float y, float w, float h,
        uint c1, uint c2, int tile)
    {
        if (ctx == 0 || w <= 0f || h <= 0f || tile <= 0)
            return;
        Target t = FromHandle<Target>(ctx);
        if (t.Bpp != 4)
            return;
        float x0 = x * t.M11 + y * t.M21 + t.M31;
        float y0 = x * t.M12 + y * t.M22 + t.M32;
        float x1 = (x + w) * t.M11 + (y + h) * t.M21 + t.M31;
        float y1 = (x + w) * t.M12 + (y + h) * t.M22 + t.M32;
        int ix0 = Math.Max(0, (int)MathF.Floor(Math.Min(x0, x1)));
        int iy0 = Math.Max(0, (int)MathF.Floor(Math.Min(y0, y1)));
        int ix1 = Math.Min(t.Width - 1, (int)MathF.Ceiling(Math.Max(x0, x1)));
        int iy1 = Math.Min(t.Height - 1, (int)MathF.Ceiling(Math.Max(y0, y1)));
        for (int yy = iy0; yy <= iy1; yy++)
        {
            int ty = (int)MathF.Floor(yy / (float)tile);
            for (int xx = ix0; xx <= ix1; xx++)
            {
                int tx = (int)MathF.Floor(xx / (float)tile);
                uint c = ((tx + ty) & 1) == 0 ? c1 : c2;
                float a = ((c >> 24) & 0xFF) / 255f;
                if (a <= 0f)
                    continue;
                BlendPixelBgra(t, xx, yy, c, a);
            }
        }
    }
}
