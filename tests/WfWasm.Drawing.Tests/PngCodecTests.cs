extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfImage = WfDraw::System.Drawing.Image;
using WfPixelFormat = WfDraw::System.Drawing.Imaging.PixelFormat;

namespace WfWasm.Drawing.Tests.TestData;

/// <summary>Anchor type so Bitmap(Type, string) resolves TestData resources.</summary>
public sealed class PngAssetHolder
{
}

public sealed class PngCodecTests
{
    [Fact]
    public void Truecolor_AllFiveFilters_DecodeExactPixels()
    {
        // 2x4 RGBA; stored bytes were filtered by hand (see comments in test plan).
        byte[][] rows =
        [
            [1, 10, 20, 30, 40, 40, 40, 40, 40],
            [2, 1, 1, 1, 1, 1, 1, 1, 1],
            [3, 7, 12, 17, 22, 21, 21, 21, 21],
            [4, 1, 1, 1, 1, 1, 1, 1, 1],
        ];
        using var bitmap = DecodePng(BuildPng(2, 4, 8, 6, rows));
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 4), bitmap.Size);
        Assert.Equal(WfPixelFormat.Format32bppArgb, bitmap.PixelFormat);
        int[,] expected =
        {
            { WfColor.FromArgb(40, 10, 20, 30).ToArgb(), WfColor.FromArgb(80, 50, 60, 70).ToArgb() },
            { WfColor.FromArgb(41, 11, 21, 31).ToArgb(), WfColor.FromArgb(81, 51, 61, 71).ToArgb() },
            { WfColor.FromArgb(42, 12, 22, 32).ToArgb(), WfColor.FromArgb(82, 52, 62, 72).ToArgb() },
            { WfColor.FromArgb(43, 13, 23, 33).ToArgb(), WfColor.FromArgb(83, 53, 63, 73).ToArgb() },
        };
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 2; x++)
                Assert.Equal(expected[y, x], bitmap.GetPixel(x, y).ToArgb());
    }

    [Theory]
    [InlineData(1, new byte[] { 0, 0x80 }, new int[] { 255, 0 })]
    [InlineData(2, new byte[] { 0, 0x1B }, new int[] { 0, 85, 170, 255 })]
    [InlineData(4, new byte[] { 0, 0x0F }, new int[] { 0, 255 })]
    [InlineData(8, new byte[] { 0, 17, 250 }, new int[] { 17, 250 })]
    public void Grayscale_SubByteDepths_ScaleToWhite(int bitDepth, byte[] row, int[] gray)
    {
        using var bitmap = DecodePng(BuildPng(gray.Length, 1, bitDepth, 0, [row]));
        Assert.Equal(gray.Length, bitmap.Width);
        for (int x = 0; x < gray.Length; x++)
            Assert.Equal(WfColor.FromArgb(255, gray[x], gray[x], gray[x]).ToArgb(), bitmap.GetPixel(x, 0).ToArgb());
    }

    [Fact]
    public void Grayscale16_TakesHighByte()
    {
        using var bitmap = DecodePng(BuildPng(1, 1, 16, 0, [[0, 0x12, 0x34]]));
        Assert.Equal(WfColor.FromArgb(255, 0x12, 0x12, 0x12).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void GrayscaleAlpha_Decodes()
    {
        using var bitmap = DecodePng(BuildPng(2, 1, 8, 4, [[0, 200, 77, 10, 255]]));
        Assert.Equal(WfColor.FromArgb(77, 200, 200, 200).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 10, 10, 10).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void GrayscaleAlpha16_TakesHighBytes()
    {
        using var bitmap = DecodePng(BuildPng(1, 1, 16, 4, [[0, 0xAB, 0xCD, 0xEF, 0x01]]));
        Assert.Equal(WfColor.FromArgb(0xEF, 0xAB, 0xAB, 0xAB).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Truecolor16_TakesHighBytes()
    {
        using var bitmap = DecodePng(BuildPng(1, 1, 16, 2, [[0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66]]));
        Assert.Equal(WfColor.FromArgb(255, 0x11, 0x33, 0x55).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void TruecolorAlpha16_TakesHighBytes()
    {
        using var bitmap = DecodePng(BuildPng(1, 1, 16, 6, [[0, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88]]));
        Assert.Equal(WfColor.FromArgb(0x77, 0x11, 0x33, 0x55).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Palette_WithTransparency_Resolves()
    {
        byte[] palette = [255, 0, 0, 0, 0, 255];
        byte[] trns = [255, 128];
        using var bitmap = DecodePng(BuildPng(2, 1, 1, 3, [[0, 0x40]], palette, trns));
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(128, 0, 0, 255).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Truecolor_TransparentKey_MatchesEitherKeyByte()
    {
        // Key (255,0,0) stored high-byte-first; the all-channels rule keeps green/blue opaque.
        byte[][] rows = [[0, 255, 0, 0, 0, 255, 0, 0, 0, 255]];
        byte[] trns = [0, 255, 0, 0, 0, 0];
        using var bitmap = DecodePng(BuildPng(3, 1, 8, 2, rows, null, trns));
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(WfColor.FromArgb(255, 0, 255, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 0, 0, 255).ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void Gray_TransparentKey_MatchesEitherKeyByte()
    {
        using var bitmap = DecodePng(BuildPng(2, 1, 8, 0, [[0, 10, 20]], null, [0, 20]));
        Assert.Equal(WfColor.FromArgb(255, 10, 10, 10).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(1, 0).A);
    }

    [Fact]
    public void AncillaryChunks_Skipped_And_SplitIdat_Decodes()
    {
        var prefix = new List<(string, byte[])>
        {
            ("tEXt", [.. System.Text.Encoding.ASCII.GetBytes("Title\0x")]),
            ("abCD", [1, 2, 3, 4, 5]),
            ("pHYs", [0, 0, 0, 1, 0, 0, 0, 1, 1]),
        };
        byte[][] rows = [[0, 255, 0, 0, 255]];
        using var bitmap = DecodePng(BuildPng(1, 1, 8, 6, rows, null, null, prefix, splitIdat: true));
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Image_FromStream_ReturnsDecodedBitmap()
    {
        byte[][] rows = [[0, 1, 2, 3, 255]];
        using var stream = new MemoryStream(BuildPng(1, 1, 8, 6, rows));
        using WfImage image = WfImage.FromStream(stream);
        Assert.Equal(new WfDraw::System.Drawing.Size(1, 1), image.Size);
    }

    [Fact]
    public void Image_FromFile_Roundtrips_ThroughTempFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            File.WriteAllBytes(path, BuildPng(1, 1, 8, 6, [[0, 9, 8, 7, 255]]));
            using WfImage image = WfImage.FromFile(path);
            var bitmap = Assert.IsType<WfBitmap>(image);
            Assert.Equal(WfColor.FromArgb(255, 9, 8, 7).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Bitmap_StreamCtor_Decodes()
    {
        using var stream = new MemoryStream(BuildPng(1, 1, 8, 6, [[0, 9, 8, 7, 255]]));
        using var bitmap = new WfBitmap(stream);
        Assert.Equal(WfColor.FromArgb(255, 9, 8, 7).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Bitmap_TypeResourceCtor_LoadsEmbeddedPng()
    {
        using var bitmap = new WfBitmap(typeof(PngAssetHolder), "red1x1.png");
        Assert.Equal(new WfDraw::System.Drawing.Size(1, 1), bitmap.Size);
        Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Bitmap_TypeResourceCtor_Missing_ThrowsArgument()
    {
        Assert.Throws<ArgumentException>(() => new WfBitmap(typeof(PngAssetHolder), "nope.png"));
    }

    [Fact]
    public void GarbageBytes_ThrowOutOfMemory()
    {
        Assert.Throws<OutOfMemoryException>(() => DecodePng([104, 105]));
    }

    [Fact]
    public void TruncatedPng_ThrowsOutOfMemory()
    {
        byte[] full = BuildPng(2, 2, 8, 6, [[0, 1, 2, 3, 4, 5, 6, 7, 8], [0, 11, 12, 13, 14, 15, 16, 17, 18]]);
        Assert.Throws<OutOfMemoryException>(() => DecodePng(full[..(full.Length / 2)]));
    }

    [Fact]
    public void CorruptIdat_ThrowsOutOfMemory()
    {
        byte[] png = BuildPng(4, 4, 8, 6, FlatRows(4, 4, 4));
        for (int i = 45; i < 55 && i < png.Length; i++)
            png[i] ^= 0xFF;
        Assert.Throws<OutOfMemoryException>(() => DecodePng(png));
    }

    [Fact]
    public void Interlaced_ThrowsNotSupported()
    {
        byte[] png = BuildPng(1, 1, 8, 6, [[0, 1, 2, 3, 4]], interlaced: true);
        Assert.Throws<NotSupportedException>(() => DecodePng(png));
    }

    [Theory]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 0x2A, 0x00, 0, 0, 1, 2 })]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0x00, 0x2A, 0, 0, 1, 2 })]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00, 1, 2, 3, 4 })]
    // BM used to throw here; BMP now decodes (BmpCodec) — short/truncated
    // BM bytes report OutOfMemory instead (see BmpCodecTests).
    // GIF likewise decodes now (GifCodec; see GifCodecTests).
    // JPEG likewise decodes now (JpegCodec; see JpegCodecTests).
    public void OtherFormats_ThrowNotSupported(byte[] magic)
    {
        var ex = Assert.Throws<NotSupportedException>(() => DecodePng(magic));
        Assert.Contains("milestone", ex.Message);
    }

    [Fact]
    public void NullStream_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => WfImage.FromStream(null!));
        Assert.Throws<ArgumentNullException>(() => new WfBitmap((System.IO.Stream)null!));
    }

    [Fact]
    public void FromFile_Missing_ThrowsFileNotFound()
    {
        Assert.Throws<System.IO.FileNotFoundException>(() => WfImage.FromFile(Path.Combine(Path.GetTempPath(), "wfwasm-nope-" + Guid.NewGuid() + ".png")));
    }

    [Fact]
    public void Encode_RoundTrips_ExactPixels()
    {
        using var src = new WfBitmap(3, 2);
        var colors = new WfColor[2, 3];
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
            {
                colors[y, x] = WfColor.FromArgb(200 - y * 10 - x, 10 + x * 20, 20 + y * 30, 30 + x + y);
                src.SetPixel(x, y, colors[y, x]);
            }

        using var encoded = new MemoryStream();
        src.Save(encoded, WfDraw::System.Drawing.Imaging.ImageFormat.Png);
        byte[] bytes = encoded.ToArray();
        Assert.True(bytes.Length > 8);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);

        using var back = DecodePng(bytes);
        Assert.Equal(new WfDraw::System.Drawing.Size(3, 2), back.Size);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                Assert.Equal(colors[y, x].ToArgb(), back.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void Encode_Transparent_RoundTrips_Alpha()
    {
        using var src = new WfBitmap(1, 1);
        src.SetPixel(0, 0, WfColor.FromArgb(0, 255, 0, 0));
        using var encoded = new MemoryStream();
        src.Save(encoded, WfDraw::System.Drawing.Imaging.ImageFormat.Png);
        using var back = DecodePng(encoded.ToArray());
        Assert.Equal(0, back.GetPixel(0, 0).A);
    }

    [Fact]
    public void Encode_NonPng_StillThrowsMilestone()
    {
        using var src = new WfBitmap(1, 1);
        using var encoded = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => src.Save(encoded, WfDraw::System.Drawing.Imaging.ImageFormat.Jpeg));
        Assert.Throws<NotSupportedException>(() => src.Save(encoded, WfDraw::System.Drawing.Imaging.ImageFormat.Bmp));
    }

    [Fact]
    public void Encode_Base64_RoundTrips_Through_FromStream()
    {
        // Mirrors the diagram BitmapNode.ImageToBase64 snapshot path.
        using var src = new WfBitmap(2, 1);
        src.SetPixel(0, 0, WfColor.FromArgb(255, 1, 2, 3));
        src.SetPixel(1, 0, WfColor.FromArgb(255, 4, 5, 6));
        string b64;
        using (var ms = new MemoryStream())
        {
            src.Save(ms, WfDraw::System.Drawing.Imaging.ImageFormat.Png);
            b64 = Convert.ToBase64String(ms.ToArray());
        }

        using var back = new WfBitmap(new MemoryStream(Convert.FromBase64String(b64), false));
        Assert.Equal(WfColor.FromArgb(255, 1, 2, 3).ToArgb(), back.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 4, 5, 6).ToArgb(), back.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void FromStream_PositionedAtEnd_DecodesLikeGdiPlus()
    {
        // Syncfusion BitmapNode.Base64ToImage shape: a MemoryStream over
        // the payload, payload written again (Position ends at Length),
        // then Image.FromStream. Real GDI+ (System.Drawing.Common 9.0.0,
        // probed in E:/tmp/gdiprobe) decodes this; the fork must too.
        byte[] png = BuildPng(3, 1, 8, 6,
            [[0, 255, 0, 0, 255, 255, 255, 255, 255, 0, 0, 255, 255]]);
        using var stream = new MemoryStream(png, 0, png.Length);
        stream.Write(png, 0, png.Length);
        Assert.Equal(png.Length, stream.Position);

        var back = Assert.IsType<WfBitmap>(WfImage.FromStream(stream, true));
        using (back)
        {
            Assert.Equal(new WfDraw::System.Drawing.Size(3, 1), back.Size);
            Assert.Equal(WfColor.FromArgb(255, 255, 0, 0).ToArgb(), back.GetPixel(0, 0).ToArgb());
            Assert.Equal(WfColor.FromArgb(255, 0, 0, 255).ToArgb(), back.GetPixel(2, 0).ToArgb());
        }
    }

    [Fact]
    public void FromStream_PositionedMid_DecodesLikeGdiPlus()
    {
        // Same oracle: GDI+ seeks back to the start, so a mid-stream
        // position still decodes (but a junk prefix with the position
        // past it does not: the rewind only helps true positioned reads).
        byte[] png = BuildPng(3, 1, 8, 6,
            [[0, 255, 0, 0, 255, 255, 255, 255, 255, 0, 0, 255, 255]]);
        using var stream = new MemoryStream(png, false);
        stream.Position = png.Length / 2;

        var back = Assert.IsType<WfBitmap>(WfImage.FromStream(stream));
        using (back)
        {
            Assert.Equal(new WfDraw::System.Drawing.Size(3, 1), back.Size);
            Assert.Equal(WfColor.FromArgb(255, 255, 255, 255).ToArgb(), back.GetPixel(1, 0).ToArgb());
        }
    }

    private static WfBitmap DecodePng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new WfBitmap(stream);
    }

    private static byte[][] FlatRows(int width, int height, int channels)
    {
        var rows = new byte[height][];
        for (int y = 0; y < height; y++)
        {
            rows[y] = new byte[1 + width * channels];
            for (int i = 1; i < rows[y].Length; i++)
                rows[y][i] = (byte)(y * 7 + i);
        }
        return rows;
    }

    private static byte[] BuildPng(
        int width, int height, int bitDepth, int colorType, byte[][] filteredRows,
        byte[]? palette = null, byte[]? transparency = null,
        IEnumerable<(string Name, byte[] Data)>? prefixChunks = null, bool interlaced = false, bool splitIdat = false)
    {
        using var plain = new MemoryStream();
        foreach (byte[] row in filteredRows)
            plain.Write(row, 0, row.Length);
        plain.Position = 0;
        using var compressed = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            plain.CopyTo(deflate);
        byte[] idat = WrapZlib(compressed.ToArray(), filteredRows);

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10], 0, 8);
        byte[] ihdr =
        [
            (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            (byte)bitDepth, (byte)colorType, 0, 0, (byte)(interlaced ? 1 : 0),
        ];
        WriteChunk(png, "IHDR", ihdr);
        if (palette is not null)
            WriteChunk(png, "PLTE", palette);
        if (transparency is not null)
            WriteChunk(png, "tRNS", transparency);
        if (prefixChunks is not null)
            foreach (var (name, data) in prefixChunks)
                WriteChunk(png, name, data);
        if (splitIdat)
        {
            int half = idat.Length / 2;
            WriteChunk(png, "IDAT", idat[..half]);
            WriteChunk(png, "IDAT", idat[half..]);
        }
        else
        {
            WriteChunk(png, "IDAT", idat);
        }
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] WrapZlib(byte[] deflate, byte[][] rows)
    {
        uint s1 = 1;
        uint s2 = 0;
        foreach (byte[] row in rows)
            foreach (byte b in row)
            {
                s1 = (s1 + b) % 65521;
                s2 = (s2 + s1) % 65521;
            }
        uint adler = (s2 << 16) | s1;
        var zlib = new byte[2 + deflate.Length + 4];
        zlib[0] = 0x78;
        zlib[1] = 0x01;
        Buffer.BlockCopy(deflate, 0, zlib, 2, deflate.Length);
        zlib[^4] = (byte)(adler >> 24);
        zlib[^3] = (byte)(adler >> 16);
        zlib[^2] = (byte)(adler >> 8);
        zlib[^1] = (byte)adler;
        return zlib;
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        png.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], 0, 4);
        png.Write(name, 0, 4);
        png.Write(data, 0, data.Length);
        uint crc = Crc32(name, data);
        png.Write([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc], 0, 4);
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
}
