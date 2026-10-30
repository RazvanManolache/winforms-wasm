// Headless GDI bitmap realization (grid dashed-border milestone, 2026-10-28).
// Vendor grid code P/Invokes gdi32!CreateBitmap for small pattern bitmaps
// (Syncfusion GridBorderPaint dash/dot tiles: 8x8x1bpp from borderSpecs)
// and converts the HBITMAP via Image.FromHbitmap. The rewriter's generic
// IntPtr stub used to answer the 0x1000 sentinel, which FromHbitmap
// rejects as foreign (NotSupportedException, caught but the border never
// paints). CreateTracked decodes the common monochrome/color shapes into
// a managed Bitmap and registers it in BitmapHandleMap, so FromHbitmap
// resolves the exact pixels the TextureBrush tile needs. Anything outside
// the decoded shapes (palettized 4/8bpp without a palette, absurd sizes,
// short buffers) answers IntPtr.Zero: the documented GDI failure shape,
// which callers null-check (CreateBitmapFromBorderSpecs skips the tile).

namespace System.Drawing;

public static class WasmGdiBitmap
{
    private const int MaxDimension = 512;

    public static nint CreateTracked(int width, int height, int planes, int bitsPerPixel, short[]? bits)
    {
        try
        {
            Bitmap? bmp = Decode(width, height, planes, bitsPerPixel, bits);
            if (bmp is null)
                return IntPtr.Zero;
            // The decoded Bitmap has no other owner: root it in the map
            // until Release (GDI DeleteObject pairing), or a collection
            // would reap it while the handle is still live.
            return BitmapHandleMap.RegisterOwned(bmp);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static nint CreateTrackedPtr(int width, int height, int planes, int bitsPerPixel, nint bits)
    {
        try
        {
            if (bits == IntPtr.Zero)
                return IntPtr.Zero;
            int shortsPerRow = ShortsPerRow(width, bitsPerPixel);
            if (shortsPerRow <= 0)
                return IntPtr.Zero;
            int count = checked(shortsPerRow * height);
            var buf = new short[count];
            System.Runtime.InteropServices.Marshal.Copy(bits, buf, 0, count);
            return CreateTracked(width, height, planes, bitsPerPixel, buf);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static int ShortsPerRow(int width, int bitsPerPixel)
    {
        if (width < 1 || width > MaxDimension)
            return -1;
        // GDI packs CreateBitmap scanlines WORD-aligned (2 bytes): rows
        // are ceil(width * bpp / 16) shorts. A DWORD-aligned stride
        // wrongly rejects the WORD-aligned buffers vendors pass (2px x
        // 24bpp = 6 bytes/row = 3 shorts/row).
        return bitsPerPixel switch
        {
            1 or 16 or 24 or 32 => (width * bitsPerPixel + 15) / 16,
            _ => -1,
        };
    }

    private static Bitmap? Decode(int width, int height, int planes, int bitsPerPixel, short[]? bits)
    {
        if (planes != 1)
            return null;
        if (height < 1 || height > MaxDimension)
            return null;
        if (bits is null)
            return null;
        int shortsPerRow = ShortsPerRow(width, bitsPerPixel);
        if (shortsPerRow <= 0 || bits.Length < shortsPerRow * height)
            return null;
        var bmp = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
        {
            int row = y * shortsPerRow;
            for (int x = 0; x < width; x++)
            {
                bmp.SetPixel(x, y, bitsPerPixel switch
                {
                    // GDI monochrome: first pixel is the MSB; 0 is black.
                    1 => (((bits[row + x / 16] >> (15 - x % 16)) & 1) == 0) ? Color.Black : Color.White,
                    16 => Decode565(bits[row + x]),
                    24 => Decode24(bits, row, x),
                    32 => Decode32(bits, row + x * 2),
                    _ => Color.Magenta,
                });
            }
        }
        return bmp;
    }

    private static Color Decode565(short v)
    {
        int u = v & 0xFFFF;
        int r = (u >> 11) & 31;
        int g = (u >> 5) & 63;
        int b = u & 31;
        return Color.FromArgb((r * 255 + 15) / 31, (g * 255 + 31) / 63, (b * 255 + 15) / 31);
    }

    private static Color Decode24(short[] bits, int row, int x)
    {
        // GDI packs BGR triplets little-endian across the row shorts.
        byte At(int byteIndex)
        {
            int s = bits[row + byteIndex / 2] & 0xFFFF;
            return (byte)((byteIndex % 2 == 0) ? (s & 0xFF) : ((s >> 8) & 0xFF));
        }
        return Color.FromArgb(At(x * 3 + 2), At(x * 3 + 1), At(x * 3));
    }

    private static Color Decode32(short[] bits, int i)
    {
        int lo = bits[i] & 0xFFFF;
        int hi = bits[i + 1] & 0xFFFF;
        int b = lo & 0xFF;
        int g = (lo >> 8) & 0xFF;
        int r = hi & 0xFF;
        // GDI 32bpp bitmaps carry no alpha; keep the tile opaque.
        return Color.FromArgb(255, r, g, b);
    }
}
