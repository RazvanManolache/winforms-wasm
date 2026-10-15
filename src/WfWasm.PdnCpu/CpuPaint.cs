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
            int guard = 0;
            while (input is not null && guard++ < 4)
            {
                Type it = input.GetType();
                string? fn = it.FullName;
                if (fn is null
                    || (!fn.Contains("ComObjectRef") && !fn.Contains("ObjectRef")))
                    break;
                var inner = it.GetField("_inner",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (inner is null)
                    break;
                input = inner.GetValue(input);
            }
            guard = 0;
            while (input is not null && guard++ < 4 && IsEffectProxy(input))
            {
                object? deeper = GetProxySourceImage(input);
                if (deeper is null || ReferenceEquals(deeper, input))
                    break;
                input = deeper;
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
