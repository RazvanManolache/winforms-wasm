// GIF decode: minimal spec reader for Image.FromStream/FromFile.
// Decodes the FIRST frame only (the single-surface Bitmap has no
// frame navigation; animated GIFs report frame zero, like GDI+'s
// initial frame). Supports GIF87a/89a, global/local palettes,
// transparency (graphic control extension), interlaced (4-pass)
// frames, and sub-blocked image data; extension blocks (comment,
// application/NETSCAPE, plain text) are skipped. Later frames and
// their disposal methods are ignored, not composited. Mapped to
// 32bpp ARGB like every other decoded image; pixels the frame does
// not cover stay transparent (the background index is descriptive,
// not painted). Corrupt data throws OutOfMemoryException (the GDI+
// quirk, matching PngCodec/BmpCodec).

namespace System.Drawing;

internal static class GifCodec
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
        if (bytes.Length < 13
            || bytes[0] != (byte)'G' || bytes[1] != (byte)'I' || bytes[2] != (byte)'F'
            || bytes[3] != (byte)'8' || (bytes[4] != (byte)'7' && bytes[4] != (byte)'9')
            || bytes[5] != (byte)'a')
            throw new OutOfMemoryException("Invalid image data.");

        int screenWidth = ReadUInt16(bytes, 6);
        int screenHeight = ReadUInt16(bytes, 8);
        int packed = bytes[10];
        if (screenWidth <= 0 || screenHeight <= 0 || screenWidth > 16384 || screenHeight > 16384)
            throw new OutOfMemoryException("Invalid image data.");

        int pos = 13;
        byte[] globalPalette = [];
        if ((packed & 0x80) != 0)
            globalPalette = ReadPalette(bytes, ref pos, 2 << (packed & 0x07));

        // Pending graphic-control state for the next image descriptor.
        bool hasTransparent = false;
        int transparentIndex = 0;

        while (true)
        {
            if (pos >= bytes.Length)
                throw new OutOfMemoryException("Invalid image data.");
            byte introducer = bytes[pos++];
            if (introducer == 0x3B)
                throw new OutOfMemoryException("Invalid image data."); // Trailer with no frame.
            if (introducer == 0x21)
            {
                ReadExtension(bytes, ref pos, ref hasTransparent, ref transparentIndex);
                continue;
            }
            if (introducer != 0x2C)
                throw new OutOfMemoryException("Invalid image data.");
            return DecodeFrame(bytes, ref pos, screenWidth, screenHeight,
                globalPalette, hasTransparent, transparentIndex);
        }
    }

    private static void ReadExtension(
        byte[] bytes, ref int pos, ref bool hasTransparent, ref int transparentIndex)
    {
        if (pos >= bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        byte label = bytes[pos++];
        if (pos >= bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        if (label == 0xF9)
        {
            // Graphic Control Extension: block size 4, packed, delay,
            // transparent index, terminator. A non-4 size means a
            // writer-specific variant: skip its payload generically.
            int blockSize = bytes[pos++];
            if (blockSize == 4)
            {
                if (pos + 4 > bytes.Length)
                    throw new OutOfMemoryException("Invalid image data.");
                int packed = bytes[pos];
                hasTransparent = (packed & 0x01) != 0;
                transparentIndex = bytes[pos + 3];
                pos += 4;
            }
            else if (blockSize != 0)
            {
                Skip(bytes, ref pos, blockSize);
            }
        }
        SkipSubBlocks(bytes, ref pos);
    }

    private static Bitmap DecodeFrame(
        byte[] bytes, ref int pos, int screenWidth, int screenHeight,
        byte[] globalPalette, bool hasTransparent, int transparentIndex)
    {
        if (pos + 9 > bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        int left = ReadUInt16(bytes, pos);
        int top = ReadUInt16(bytes, pos + 2);
        int frameWidth = ReadUInt16(bytes, pos + 4);
        int frameHeight = ReadUInt16(bytes, pos + 6);
        int packed = bytes[pos + 8];
        pos += 9;
        if (frameWidth <= 0 || frameHeight <= 0 || frameWidth > 16384 || frameHeight > 16384)
            throw new OutOfMemoryException("Invalid image data.");

        byte[] palette = globalPalette;
        if ((packed & 0x80) != 0)
            palette = ReadPalette(bytes, ref pos, 2 << (packed & 0x07));
        if (palette.Length == 0)
            throw new OutOfMemoryException("Invalid image data.");
        bool interlaced = (packed & 0x40) != 0;

        if (pos >= bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        int minCodeSize = bytes[pos++];
        byte[] compressed = ReadSubBlocks(bytes, ref pos);

        byte[] indices = LzwDecompress(compressed, minCodeSize, (long)frameWidth * frameHeight);

        var bitmap = new Bitmap(screenWidth, screenHeight);
        int[] dest = bitmap.Pixels;
        int colorCount = palette.Length / 3;
        for (int row = 0; row < frameHeight; row++)
        {
            int srcRow = interlaced ? InterlaceRow(frameHeight, row) : row;
            int destY = top + srcRow;
            if (destY < 0 || destY >= screenHeight)
                continue;
            for (int x = 0; x < frameWidth; x++)
            {
                int destX = left + x;
                if (destX < 0 || destX >= screenWidth)
                    continue;
                int index = indices[row * frameWidth + x];
                if (index >= colorCount)
                    throw new OutOfMemoryException("Invalid image data.");
                int o = index * 3;
                // GDI+ keeps the palette RGB under a zero alpha for
                // transparent pixels (observed: 00090807, not 00000000).
                int alpha = hasTransparent && index == transparentIndex ? 0 : 255;
                dest[destY * screenWidth + destX] =
                    Color.FromArgb(alpha, palette[o], palette[o + 1], palette[o + 2]).ToArgb();
            }
        }
        return bitmap;
    }

    /// <summary>
    /// File-order scanline to display row for the GIF 4-pass pattern:
    /// starts 0/4/2/1 with steps 8/8/4/2.
    /// </summary>
    private static int InterlaceRow(int height, int fileRow)
    {
        int[] starts = [0, 4, 2, 1];
        int[] steps = [8, 8, 4, 2];
        for (int pass = 0; pass < 4; pass++)
        {
            for (int row = starts[pass]; row < height; row += steps[pass])
            {
                if (fileRow == 0)
                    return row;
                fileRow--;
            }
        }
        throw new OutOfMemoryException("Invalid image data.");
    }

    private static byte[] LzwDecompress(byte[] data, int minCodeSize, long pixelCount)
    {
        if (minCodeSize is < 1 or > 8)
            throw new OutOfMemoryException("Invalid image data.");
        if (pixelCount <= 0 || pixelCount > 16384L * 16384L)
            throw new OutOfMemoryException("Invalid image data.");

        int clear = 1 << minCodeSize;
        int eoi = clear + 1;
        int[] prefix = new int[4096];
        byte[] suffix = new byte[4096];
        for (int i = 0; i < clear; i++)
            suffix[i] = (byte)i;
        // Pixel stack: one entry per dictionary walk plus the KwKwK
        // seed, so 4096 + 1 covers the longest chain.
        byte[] stack = new byte[4097];

        byte[] output = new byte[pixelCount];
        long outPos = 0;
        int bytePos = 0;
        int bitBuffer = 0;
        int bitsInBuffer = 0;
        int codeSize = minCodeSize + 1;
        int next = eoi + 1;
        int oldCode = -1;
        int firstChar = 0;

        int ReadCode()
        {
            while (bitsInBuffer < codeSize)
            {
                if (bytePos >= data.Length)
                    throw new OutOfMemoryException("Invalid image data.");
                bitBuffer |= data[bytePos++] << bitsInBuffer;
                bitsInBuffer += 8;
            }
            int code = bitBuffer & ((1 << codeSize) - 1);
            bitBuffer >>= codeSize;
            bitsInBuffer -= codeSize;
            return code;
        }

        while (outPos < pixelCount)
        {
            int code = ReadCode();
            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                next = eoi + 1;
                oldCode = -1;
                continue;
            }
            if (code == eoi)
                break;
            if (oldCode == -1)
            {
                if (code >= clear)
                    throw new OutOfMemoryException("Invalid image data.");
                output[outPos++] = (byte)code;
                firstChar = code;
                oldCode = code;
                continue;
            }
            int inCode = code;
            int stackTop = 0;
            if (code >= next)
            {
                // KwKwK: the encoder's just-added entry; it always
                // replays the previous sequence plus its first byte.
                if (code != next)
                    throw new OutOfMemoryException("Invalid image data.");
                stack[stackTop++] = (byte)firstChar;
                code = oldCode;
            }
            while (code >= clear)
            {
                stack[stackTop++] = suffix[code];
                code = prefix[code];
            }
            firstChar = code;
            stack[stackTop++] = (byte)code;
            while (stackTop > 0 && outPos < pixelCount)
                output[outPos++] = stack[--stackTop];
            if (next < 4096)
            {
                prefix[next] = oldCode;
                suffix[next] = (byte)firstChar;
                next++;
            }
            // Reference (giflib) rule: grow once the next-available
            // index reaches the current width's capacity. Written as >=
            // rather than == so minimum-code-size-1 streams (whose first
            // entry already overflows width 2) grow on the first add.
            if (next >= (1 << codeSize) && codeSize < 12)
                codeSize++;
            oldCode = inCode;
        }

        if (outPos < pixelCount)
            throw new OutOfMemoryException("Invalid image data.");
        return output;
    }

    private static byte[] ReadPalette(byte[] bytes, ref int pos, int colors)
    {
        if (colors <= 0 || colors > 256 || (long)pos + (long)colors * 3 > bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        byte[] palette = new byte[colors * 3];
        Buffer.BlockCopy(bytes, pos, palette, 0, palette.Length);
        pos += palette.Length;
        return palette;
    }

    private static void SkipSubBlocks(byte[] bytes, ref int pos)
    {
        while (true)
        {
            if (pos >= bytes.Length)
                throw new OutOfMemoryException("Invalid image data.");
            int size = bytes[pos++];
            if (size == 0)
                return;
            Skip(bytes, ref pos, size);
        }
    }

    private static byte[] ReadSubBlocks(byte[] bytes, ref int pos)
    {
        // Bound by the remaining file bytes: every payload byte is
        // copied once, so pre-sizing to the tail cannot over-allocate
        // beyond the input itself.
        byte[] collected = new byte[bytes.Length - pos];
        int count = 0;
        while (true)
        {
            if (pos >= bytes.Length)
                throw new OutOfMemoryException("Invalid image data.");
            int size = bytes[pos++];
            if (size == 0)
                break;
            if (pos + size > bytes.Length)
                throw new OutOfMemoryException("Invalid image data.");
            Buffer.BlockCopy(bytes, pos, collected, count, size);
            pos += size;
            count += size;
        }
        Array.Resize(ref collected, count);
        return collected;
    }

    private static void Skip(byte[] bytes, ref int pos, int size)
    {
        if (size < 0 || pos + size > bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        pos += size;
    }

    private static int ReadUInt16(byte[] bytes, int at) => bytes[at] | (bytes[at + 1] << 8);
}
