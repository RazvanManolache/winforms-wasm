// PNG decode: minimal spec reader for Image.FromStream/FromFile.
// Supports non-interlaced PNG (color types 0/2/3/4/6, 1/2/4/8/16-bit) via
// DeflateStream + all five filters, mapped to 32bpp ARGB. Interlaced
// (Adam7) files and formats other than PNG/BMP/GIF/JPEG throw
// NotSupportedException; corrupt data throws OutOfMemoryException
// (the documented GDI+ quirk). BMP/GIF/JPEG route to BmpCodec/GifCodec/JpegCodec.

namespace System.Drawing;

using System.IO.Compression;

/// <summary>PNG reader for image codecs (cf. PNG decoding in GDI+).</summary>
internal static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static bool HasSignature(byte[] prefix)
    {
        if (prefix.Length < Signature.Length)
            return false;
        for (int i = 0; i < Signature.Length; i++)
            if (prefix[i] != Signature[i])
                return false;
        return true;
    }

    /// <summary>
    /// Decodes an image stream by format sniffing. PNG, BMP, GIF and JPEG decode
    /// fully; other recognized formats name their follow-up milestone;
    /// anything else reports corrupt data (the
    /// <see cref="OutOfMemoryException"/> GDI+ raises for unrecognized
    /// bytes).
    /// </summary>
    internal static Bitmap DecodeStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        // GDI+ parity: Image.FromStream reads from the start of the
        // stream however it is positioned (verified against
        // System.Drawing.Common 9.0.0: at-end and mid-stream positions
        // decode; a junk prefix with the position past it does not).
        // Syncfusion BitmapNode.Base64ToImage depends on this: it
        // builds a MemoryStream over the payload, writes the payload
        // again (leaving Position at the end), then calls FromStream.
        if (stream.CanSeek)
            stream.Position = 0;
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        if (HasSignature(bytes))
            return Decode(new MemoryStream(bytes, false));
        if (StartsWith(bytes, 0xFF, 0xD8, 0xFF))
            return JpegCodec.Decode(bytes);
        if (StartsWith(bytes, (byte)'B', (byte)'M'))
            return BmpCodec.Decode(bytes);
        if (StartsWith(bytes, (byte)'G', (byte)'I', (byte)'F'))
            return GifCodec.Decode(bytes);
        if (StartsWith(bytes, (byte)'I', (byte)'I', 0x2A, 0x00) || StartsWith(bytes, (byte)'M', (byte)'M', 0x00, 0x2A))
            throw new NotSupportedException("TIFF decode arrives with the codec milestone.");
        if (StartsWith(bytes, 0x00, 0x00, 0x01, 0x00))
            throw new NotSupportedException("Icon decode arrives with the codec milestone.");
        throw new OutOfMemoryException("Invalid image data.");
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
            return false;
        for (int i = 0; i < prefix.Length; i++)
            if (bytes[i] != prefix[i])
                return false;
        return true;
    }

    internal static Bitmap Decode(Stream stream)
    {
        try
        {
            return DecodeCore(stream);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            throw new OutOfMemoryException("Invalid image data.", ex);
        }
    }

    private static Bitmap DecodeCore(Stream stream)
    {
        byte[] header = new byte[8];
        Fill(stream, header);
        if (!HasSignature(header))
            throw new ArgumentException("Not a PNG stream.");

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        bool haveHeader = false;
        byte[] palette = [];
        byte[] transparency = [];
        var idat = new MemoryStream();

        while (true)
        {
            byte[] lenBytes = new byte[4];
            Fill(stream, lenBytes);
            int length = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
            if (length < 0)
                throw new OutOfMemoryException("Invalid image data.");
            byte[] typeBytes = new byte[4];
            Fill(stream, typeBytes);
            string type = System.Text.Encoding.ASCII.GetString(typeBytes);
            byte[] data = new byte[length];
            if (length != 0)
                Fill(stream, data);
            byte[] crc = new byte[4];
            Fill(stream, crc);

            switch (type)
            {
                case "IHDR":
                    width = ReadInt32(data, 0);
                    height = ReadInt32(data, 4);
                    bitDepth = data[8];
                    colorType = data[9];
                    if (data[10] != 0 || data[11] != 0)
                        throw new NotSupportedException("Unsupported PNG compression or filter method.");
                    if (data[12] != 0)
                        throw new NotSupportedException("Interlaced PNG arrives with the codec milestone.");
                    if (width <= 0 || height <= 0 || width > 32768 || height > 32768)
                        throw new OutOfMemoryException("Invalid image data.");
                    if (bitDepth is not (1 or 2 or 4 or 8 or 16))
                        throw new OutOfMemoryException("Invalid image data.");
                    if (colorType is not (0 or 2 or 3 or 4 or 6))
                        throw new OutOfMemoryException("Invalid image data.");
                    if ((colorType is 2 or 4 or 6) && bitDepth != 8 && bitDepth != 16)
                        throw new OutOfMemoryException("Invalid image data.");
                    if (colorType == 3 && bitDepth == 16)
                        throw new OutOfMemoryException("Invalid image data.");
                    haveHeader = true;
                    break;
                case "PLTE":
                    palette = data;
                    break;
                case "tRNS":
                    transparency = data;
                    break;
                case "IDAT":
                    idat.Write(data, 0, data.Length);
                    break;
                case "IEND":
                    if (!haveHeader)
                        throw new OutOfMemoryException("Invalid image data.");
                    return Assemble(width, height, bitDepth, colorType, palette, transparency, idat.ToArray());
                default:
                    break;
            }
        }
    }

    private static Bitmap Assemble(
        int width, int height, int bitDepth, int colorType,
        byte[] palette, byte[] transparency, byte[] compressed)
    {
        int channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            _ => 4,
        };
        int stride = (width * bitDepth * channels + 7) / 8;
        byte[] deflate = StripZlibWrapper(compressed);
        byte[] raw;
        using (var input = new MemoryStream(deflate, false))
        using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
        using (var plain = new MemoryStream())
        {
            inflate.CopyTo(plain);
            raw = plain.ToArray();
        }
        if (raw.Length != (stride + 1) * height)
            throw new OutOfMemoryException("Invalid image data.");
        VerifyAdler32(raw, compressed);

        int unit = Math.Max(1, (bitDepth * channels + 7) / 8);
        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        var bitmap = new Bitmap(width, height);
        int[] dest = bitmap.Pixels;
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = raw[offset++];
            Buffer.BlockCopy(raw, offset, current, 0, stride);
            offset += stride;
            Unfilter(filter, current, previous, unit);
            Buffer.BlockCopy(current, 0, previous, 0, stride);
            for (int x = 0; x < width; x++)
                dest[y * width + x] = Sample(current, x, width, bitDepth, colorType, palette, transparency).ToArgb();
        }
        return bitmap;
    }

    /// <summary>
    /// IDAT holds a zlib stream (RFC 1950): 2-byte header, raw deflate,
    /// 4-byte Adler-32 trailer. Returns the inner deflate bytes.
    /// </summary>
    private static byte[] StripZlibWrapper(byte[] compressed)
    {
        if (compressed.Length < 7
            || (compressed[0] & 0x0F) != 8
            || ((compressed[0] << 8) | compressed[1]) % 31 != 0)
            throw new OutOfMemoryException("Invalid image data.");
        byte[] deflate = new byte[compressed.Length - 6];
        Buffer.BlockCopy(compressed, 2, deflate, 0, deflate.Length);
        return deflate;
    }

    private static void VerifyAdler32(byte[] raw, byte[] compressed)
    {
        uint s1 = 1;
        uint s2 = 0;
        foreach (byte b in raw)
        {
            s1 = (s1 + b) % 65521;
            s2 = (s2 + s1) % 65521;
        }
        int n = compressed.Length;
        uint stored = ((uint)compressed[n - 4] << 24) | ((uint)compressed[n - 3] << 16)
            | ((uint)compressed[n - 2] << 8) | compressed[n - 1];
        if (stored != ((s2 << 16) | s1))
            throw new OutOfMemoryException("Invalid image data.");
    }

    private static void Unfilter(int filter, byte[] current, byte[] previous, int unit)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (int i = unit; i < current.Length; i++)
                    current[i] += current[i - unit];
                return;
            case 2:
                for (int i = 0; i < current.Length; i++)
                    current[i] += previous[i];
                return;
            case 3:
                for (int i = 0; i < current.Length; i++)
                {
                    int left = i >= unit ? current[i - unit] : 0;
                    current[i] += (byte)((left + previous[i]) / 2);
                }
                return;
            case 4:
                for (int i = 0; i < current.Length; i++)
                {
                    int left = i >= unit ? current[i - unit] : 0;
                    int up = previous[i];
                    int upperLeft = i >= unit ? previous[i - unit] : 0;
                    current[i] += (byte)Paeth(left, up, upperLeft);
                }
                return;
            default:
                throw new OutOfMemoryException("Invalid image data.");
        }
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        int estimate = left + up - upperLeft;
        int dl = Math.Abs(estimate - left);
        int du = Math.Abs(estimate - up);
        int dul = Math.Abs(estimate - upperLeft);
        if (dl <= du && dl <= dul)
            return left;
        return du <= dul ? up : upperLeft;
    }

    private static Color Sample(
        byte[] row, int x, int width, int bitDepth, int colorType,
        byte[] palette, byte[] transparency)
    {
        switch (colorType)
        {
            case 0:
            {
                int gray = GrayAt(row, x, bitDepth);
                int alpha = 255;
                if (transparency.Length >= 2)
                {
                    bool transparent = bitDepth == 16
                        ? ((row[x * 2] << 8) | row[x * 2 + 1]) == ((transparency[0] << 8) | transparency[1])
                        : transparency[0] == gray || transparency[1] == gray;
                    if (transparent)
                        alpha = 0;
                }
                return Color.FromArgb(alpha, gray, gray, gray);
            }
            case 2:
            {
                int o = x * (bitDepth == 16 ? 6 : 3);
                int step = bitDepth == 16 ? 2 : 1;
                int r = row[o];
                int g = row[o + step];
                int b = row[o + 2 * step];
                int alpha = 255;
                if (transparency.Length >= 6)
                {
                    bool transparent;
                    if (bitDepth == 16)
                    {
                        transparent = ((row[o] << 8) | row[o + 1]) == ((transparency[0] << 8) | transparency[1])
                            && ((row[o + 2] << 8) | row[o + 3]) == ((transparency[2] << 8) | transparency[3])
                            && ((row[o + 4] << 8) | row[o + 5]) == ((transparency[4] << 8) | transparency[5]);
                    }
                    else
                    {
                        transparent = (transparency[0] == r || transparency[1] == r)
                            && (transparency[2] == g || transparency[3] == g)
                            && (transparency[4] == b || transparency[5] == b);
                    }
                    if (transparent)
                        alpha = 0;
                }
                return Color.FromArgb(alpha, r, g, b);
            }
            case 3:
            {
                int index = PaletteIndexAt(row, x, bitDepth);
                int o = index * 3;
                if (o + 2 >= palette.Length)
                    throw new OutOfMemoryException("Invalid image data.");
                int alpha = index < transparency.Length ? transparency[index] : 255;
                return Color.FromArgb(alpha, palette[o], palette[o + 1], palette[o + 2]);
            }
            case 4:
            {
                int o = x * (bitDepth == 16 ? 4 : 2);
                int gray = row[o];
                int alpha = row[o + (bitDepth == 16 ? 2 : 1)];
                return Color.FromArgb(alpha, gray, gray, gray);
            }
            default:
            {
                int o = x * (bitDepth == 16 ? 8 : 4);
                int step = bitDepth == 16 ? 2 : 1;
                return Color.FromArgb(row[o + 3 * step], row[o], row[o + step], row[o + 2 * step]);
            }
        }
    }

    /// <summary>Unscaled sample value for palette lookup (0 to 2^bitDepth - 1).</summary>
    private static int PaletteIndexAt(byte[] row, int x, int bitDepth)
    {
        switch (bitDepth)
        {
            case 1:
                return (row[x / 8] >> (7 - (x % 8))) & 1;
            case 2:
                return (row[x / 4] >> (2 * (3 - (x % 4)))) & 3;
            case 4:
                return (row[x / 2] >> ((x % 2 == 0) ? 4 : 0)) & 15;
            default:
                return row[x];
        }
    }

    private static int GrayAt(byte[] row, int x, int bitDepth)
    {
        switch (bitDepth)
        {
            case 1:
                return ((row[x / 8] >> (7 - (x % 8))) & 1) * 255;
            case 2:
                return ((row[x / 4] >> (2 * (3 - (x % 4)))) & 3) * 85;
            case 4:
                return ((row[x / 2] >> ((x % 2 == 0) ? 4 : 0)) & 15) * 17;
            case 16:
                return row[x * 2];
            default:
                return row[x];
        }
    }

    private static void Fill(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    private static int ReadInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    // PNG encode: minimal spec writer for Image.Save(Stream/File, Png).
    // Writes non-interlaced 8-bit RGBA (color type 6) with filter-None
    // scanlines compressed by DeflateStream; chunk CRCs are real (a
    // strict reader must accept the output: round-trip through Decode).
    // Other formats keep throwing their milestone NotSupportedException.
    internal static void Encode(Stream output, Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(bitmap);
        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Cannot encode an empty bitmap.");

        byte[] raw = new byte[height * (1 + width * 4)];
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < width; x++)
            {
                Color c = bitmap.GetPixel(x, y);
                raw[o++] = c.R;
                raw[o++] = c.G;
                raw[o++] = c.B;
                raw[o++] = c.A;
            }
        }

        byte[] zlib;
        using (var compressor = new MemoryStream())
        {
            compressor.WriteByte(0x78);
            compressor.WriteByte(0x9C);
            using (var deflate = new DeflateStream(compressor, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(raw, 0, raw.Length);
            WriteUInt32BigEndian(compressor, Adler32(raw));
            zlib = compressor.ToArray();
        }

        output.Write(Signature, 0, Signature.Length);
        using (var ihdr = new MemoryStream())
        {
            WriteUInt32BigEndian(ihdr, (uint)width);
            WriteUInt32BigEndian(ihdr, (uint)height);
            ihdr.WriteByte(8);
            ihdr.WriteByte(6);
            ihdr.WriteByte(0);
            ihdr.WriteByte(0);
            ihdr.WriteByte(0);
            WriteChunk(output, "IHDR", ihdr.ToArray());
        }

        WriteChunk(output, "IDAT", zlib);
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream output, string name, byte[] data)
    {
        byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        WriteUInt32BigEndian(output, (uint)data.Length);
        output.Write(nameBytes, 0, nameBytes.Length);
        output.Write(data, 0, data.Length);
        uint crc = Crc32(nameBytes, data);
        byte[] crcBytes = new byte[4];
        crcBytes[0] = (byte)(crc >> 24);
        crcBytes[1] = (byte)(crc >> 16);
        crcBytes[2] = (byte)(crc >> 8);
        crcBytes[3] = (byte)crc;
        output.Write(crcBytes, 0, 4);
    }

    private static void WriteUInt32BigEndian(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] name, byte[] data)
    {
        uint c = uint.MaxValue;
        foreach (byte b in name)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ uint.MaxValue;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        foreach (byte v in data)
        {
            a = (a + v) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }
}
