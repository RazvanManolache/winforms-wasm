// BMP decode: minimal spec reader for Image.FromStream/FromFile.
// Supports BITMAPCOREHEADER (12), BITMAPINFOHEADER (40) and V4/V5
// (108/124) with 1/4/8-bit palettes, 16-bit 555/565, 24-bit and 32-bit
// pixels, BI_RGB and BI_BITFIELDS (including V4/V5 alpha masks),
// top-down (negative height) storage. RLE4/RLE8 and JPEG/PNG-embedded
// BMPs throw NotSupportedException (vanishingly rare in app art);
// corrupt data throws OutOfMemoryException (the GDI+ quirk, matching
// PngCodec). Mapped to 32bpp ARGB like every other decoded image.

namespace System.Drawing;

internal static class BmpCodec
{
    internal static Bitmap Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return DecodeCore(bytes);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OutOfMemoryException("Invalid image data.", ex);
        }
    }

    private static Bitmap DecodeCore(byte[] bytes)
    {
        if (bytes.Length < 26 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
            throw new OutOfMemoryException("Invalid image data.");
        int dataOffset = ReadInt32(bytes, 10);
        int headerSize = ReadInt32(bytes, 14);
        int width, height, bpp, compression;
        int[] palette = [];
        uint redMask = 0, greenMask = 0, blueMask = 0, alphaMask = 0;
        bool topDown = false;

        if (headerSize == 12)
        {
            width = ReadUInt16(bytes, 18);
            height = ReadUInt16(bytes, 20);
            if (ReadUInt16(bytes, 22) != 1)
                throw new OutOfMemoryException("Invalid image data.");
            bpp = ReadUInt16(bytes, 24);
            compression = 0;
            int colors = 1 << bpp;
            palette = ReadPalette(bytes, 26, colors, entrySize: 3);
        }
        else if (headerSize is 40 or 108 or 124)
        {
            if (bytes.Length < 14 + headerSize)
                throw new OutOfMemoryException("Invalid image data.");
            width = ReadInt32(bytes, 18);
            height = ReadInt32(bytes, 22);
            if (ReadUInt16(bytes, 26) != 1)
                throw new OutOfMemoryException("Invalid image data.");
            bpp = ReadUInt16(bytes, 28);
            compression = ReadInt32(bytes, 30);
            if (height < 0)
            {
                topDown = true;
                height = -height;
            }
            if (compression == 1 || compression == 2)
                throw new NotSupportedException("RLE-compressed BMP arrives with the codec milestone.");
            if (compression == 4 || compression == 5)
                throw new NotSupportedException("Embedded JPEG/PNG BMP arrives with the codec milestone.");
            if (compression != 0 && compression != 3)
                throw new OutOfMemoryException("Invalid image data.");

            int paletteAt = 14 + headerSize;
            if (compression == 3)
            {
                if (headerSize == 40)
                {
                    if (bytes.Length < paletteAt + 12)
                        throw new OutOfMemoryException("Invalid image data.");
                    redMask = ReadUInt32(bytes, paletteAt);
                    greenMask = ReadUInt32(bytes, paletteAt + 4);
                    blueMask = ReadUInt32(bytes, paletteAt + 8);
                    paletteAt += 12;
                }
            }
            if (headerSize >= 108)
            {
                redMask = ReadUInt32(bytes, 14 + 40);
                greenMask = ReadUInt32(bytes, 14 + 44);
                blueMask = ReadUInt32(bytes, 14 + 48);
                alphaMask = ReadUInt32(bytes, 14 + 52);
            }
            if (bpp <= 8)
            {
                int colorsUsed = ReadInt32(bytes, 46);
                int colors = colorsUsed > 0 ? colorsUsed : 1 << bpp;
                palette = ReadPalette(bytes, paletteAt, colors, entrySize: 4);
                if (compression == 3)
                    throw new OutOfMemoryException("Invalid image data.");
            }
        }
        else
        {
            throw new OutOfMemoryException("Invalid image data.");
        }

        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new OutOfMemoryException("Invalid image data.");
        if (bpp is not (1 or 4 or 8 or 16 or 24 or 32))
            throw new OutOfMemoryException("Invalid image data.");
        if (dataOffset < 0 || dataOffset >= bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");

        int rowBits = width * bpp;
        int stride = ((rowBits + 31) / 32) * 4;
        if ((long)stride * height > bytes.Length - dataOffset)
            throw new OutOfMemoryException("Invalid image data.");

        Mask r = Mask.Of(redMask), g = Mask.Of(greenMask), b = Mask.Of(blueMask), a = Mask.Of(alphaMask);
        bool hasAlpha = alphaMask != 0;
        if (bpp == 16 && compression == 0)
        {
            r = Mask.Of(0x7C00);
            g = Mask.Of(0x03E0);
            b = Mask.Of(0x001F);
        }
        if (bpp == 32 && compression == 0)
        {
            r = Mask.Of(0x00FF0000);
            g = Mask.Of(0x0000FF00);
            b = Mask.Of(0x000000FF);
        }

        var bitmap = new Bitmap(width, height);
        int[] dest = bitmap.Pixels;
        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : height - 1 - y;
            int rowAt = dataOffset + srcRow * stride;
            for (int x = 0; x < width; x++)
                dest[y * width + x] = Sample(bytes, rowAt, x, bpp, palette, r, g, b, a, hasAlpha);
        }
        return bitmap;
    }

    private static int Sample(byte[] bytes, int rowAt, int x, int bpp, int[] palette,
        Mask r, Mask g, Mask b, Mask a, bool hasAlpha)
    {
        switch (bpp)
        {
            case 1:
            {
                int bit = 7 - (x & 7);
                int index = (bytes[rowAt + (x >> 3)] >> bit) & 1;
                return palette.Length > index ? palette[index] : unchecked((int)0xFF000000);
            }
            case 4:
            {
                int nibble = (bytes[rowAt + (x >> 1)] >> ((x & 1) == 0 ? 4 : 0)) & 0xF;
                return palette.Length > nibble ? palette[nibble] : unchecked((int)0xFF000000);
            }
            case 8:
            {
                int index = bytes[rowAt + x];
                return palette.Length > index ? palette[index] : unchecked((int)0xFF000000);
            }
            case 16:
            {
                uint pixel = (uint)(bytes[rowAt + x * 2] | (bytes[rowAt + x * 2 + 1] << 8));
                return Color.FromArgb(255, r.Scale(pixel), g.Scale(pixel), b.Scale(pixel)).ToArgb();
            }
            case 24:
            {
                int o = rowAt + x * 3;
                return Color.FromArgb(255, bytes[o + 2], bytes[o + 1], bytes[o]).ToArgb();
            }
            default:
            {
                uint pixel = ReadUInt32(bytes, rowAt + x * 4);
                int alpha = hasAlpha ? a.Scale(pixel) : 255;
                return Color.FromArgb(alpha, r.Scale(pixel), g.Scale(pixel), b.Scale(pixel)).ToArgb();
            }
        }
    }

    private static int[] ReadPalette(byte[] bytes, int at, int colors, int entrySize)
    {
        if (colors < 0 || colors > 256 || at < 0 || (long)at + (long)colors * entrySize > bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        int[] palette = new int[colors];
        for (int i = 0; i < colors; i++)
        {
            int o = at + i * entrySize;
            palette[i] = Color.FromArgb(255, bytes[o + 2], bytes[o + 1], bytes[o]).ToArgb();
        }
        return palette;
    }

    private readonly struct Mask
    {
        private readonly int _shift;
        private readonly int _max;

        private Mask(int shift, int max)
        {
            _shift = shift;
            _max = max;
        }

        internal static Mask Of(uint mask)
        {
            if (mask == 0)
                return new Mask(0, 0);
            int shift = 0;
            while ((mask & 1) == 0)
            {
                shift++;
                mask >>= 1;
            }
            int bits = 0;
            while ((mask & 1) == 1)
            {
                bits++;
                mask >>= 1;
            }
            return new Mask(shift, (1 << bits) - 1);
        }

        internal int Scale(uint pixel)
        {
            if (_max <= 0)
                return 0;
            int value = (int)((pixel >> _shift) & (uint)_max);
            return (value * 255 + _max / 2) / _max;
        }
    }

    private static int ReadInt32(byte[] bytes, int at) =>
        bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);

    private static uint ReadUInt32(byte[] bytes, int at) => unchecked((uint)ReadInt32(bytes, at));

    private static int ReadUInt16(byte[] bytes, int at) => bytes[at] | (bytes[at + 1] << 8);
}
