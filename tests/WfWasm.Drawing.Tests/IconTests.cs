extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfIcon = WfDraw::System.Drawing.Icon;

namespace WfWasm.Drawing.Tests;

public sealed class IconTests
{
    [Fact]
    public void Bmp32_Pixels_And_RowOrder_Match()
    {
        // 2x2 32bpp, bottom-up; top row red/green, bottom row blue/white; AND mask empty.
        byte[] entry = BuildBmpEntry(2, 2, 32, Array.Empty<int>(),
            topRows:
            [
                [(255, 0, 0, 255), (0, 255, 0, 255)],
                [(0, 0, 255, 255), (255, 255, 255, 255)],
            ],
            andMask: [0, 0, 0, 0, 0, 0, 0, 0]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(2, 2, 32, entry)])));
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), icon.Size);
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), bmp.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), bmp.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void Bmp32_AndMask_Blanks_Pixel()
    {
        byte[] entry = BuildBmpEntry(2, 1, 32, Array.Empty<int>(),
            topRows: [[(255, 0, 0, 255), (0, 255, 0, 255)]],
            // AND row: second pixel masked (MSB-first: 0x40 covers x=1).
            andMask: [0x40, 0, 0, 0]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(2, 1, 32, entry)])));
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(255, bmp.GetPixel(0, 0).A);
        Assert.Equal(0, bmp.GetPixel(1, 0).A);
    }

    [Fact]
    public void Bmp4_Palette_Nibbles_Match()
    {
        // 4x1 4bpp: indices 1,0,1,0; palette 0=black, 1=white.
        byte[] entry = BuildBmpEntry(4, 1, 4, [0x000000, 0xFFFFFF],
            topRows: null, rawXor: [0x10, 0x10, 0, 0],
            andMask: [0, 0, 0, 0]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(4, 1, 4, entry)])));
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(3, 0).ToArgb());
    }

    [Fact]
    public void Bmp8_Palette_Bytes_Match()
    {
        byte[] entry = BuildBmpEntry(3, 1, 8, [0xFF0000, 0x00FF00, 0x0000FF],
            topRows: null, rawXor: [2, 0, 1, 0],
            andMask: [0, 0, 0, 0]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(3, 1, 8, entry)])));
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(WfColor.Blue.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), bmp.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void Bmp1_MsbFirst_Bits_Match()
    {
        byte[] entry = BuildBmpEntry(8, 1, 1, [0x000000, 0xFFFFFF],
            topRows: null, rawXor: [0xAA, 0, 0, 0],
            andMask: [0, 0, 0, 0]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(8, 1, 1, entry)])));
        using WfBitmap bmp = icon.ToBitmap();
        for (int x = 0; x < 8; x++)
            Assert.Equal((x % 2 == 0 ? WfColor.White : WfColor.Black).ToArgb(), bmp.GetPixel(x, 0).ToArgb());
    }

    [Fact]
    public void PngEntry_Decodes_FirstEntry_Size_Wins()
    {
        byte[] png = BuildPng1x1(10, 20, 30);
        byte[] bmpEntry = BuildBmpEntry(2, 2, 32, Array.Empty<int>(),
            topRows:
            [
                [(1, 2, 3, 255), (4, 5, 6, 255)],
                [(7, 8, 9, 255), (10, 11, 12, 255)],
            ],
            andMask: new byte[8]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(2, 2, 32, bmpEntry), (1, 1, 32, png)])));
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), icon.Size);
    }

    [Fact]
    public void SizedCtor_Picks_Exact_Entry()
    {
        byte[] png = BuildPng1x1(10, 20, 30);
        byte[] bmpEntry = BuildBmpEntry(2, 2, 32, Array.Empty<int>(),
            topRows: [[(1, 2, 3, 255), (4, 5, 6, 255)], [(7, 8, 9, 255), (10, 11, 12, 255)]],
            andMask: new byte[8]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(2, 2, 32, bmpEntry), (1, 1, 32, png)])),
            new WfDraw::System.Drawing.Size(1, 1));
        Assert.Equal(new WfDraw::System.Drawing.Size(1, 1), icon.Size);
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(WfColor.FromArgb(255, 10, 20, 30).ToArgb(), bmp.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void SizedCtor_Scales_Largest_When_No_Exact_Match()
    {
        byte[] bmpEntry = BuildBmpEntry(2, 2, 32, Array.Empty<int>(),
            topRows: [[(255, 0, 0, 255), (0, 255, 0, 255)], [(0, 0, 255, 255), (255, 255, 255, 255)]],
            andMask: new byte[8]);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(2, 2, 32, bmpEntry)])), 4, 4);
        Assert.Equal(new WfDraw::System.Drawing.Size(4, 4), icon.Size);
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(4, bmp.Width);
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Clone_Roundtrips_Raster()
    {
        byte[] png = BuildPng1x1(10, 20, 30);
        using var icon = new WfIcon(new MemoryStream(BuildIco([(1, 1, 32, png)])));
        using var clone = (WfIcon)icon.Clone();
        Assert.Equal(icon.Size, clone.Size);
        using WfBitmap bmp = clone.ToBitmap();
        Assert.Equal(WfColor.FromArgb(255, 10, 20, 30).ToArgb(), bmp.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Garbage_Throws_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WfIcon(new MemoryStream([1, 2, 3, 4, 5, 6])));
        Assert.Throws<ArgumentException>(() => new WfIcon(new MemoryStream(new byte[64])));
        Assert.Throws<ArgumentNullException>(() => new WfIcon((Stream)null!));
    }

    [Fact]
    public void FromHandle_Returns_Blank_Placeholder()
    {
        // No GDI handles exist headless, but dialog/menu/history chrome
        // (PDN ToIcon paths) needs a non-null icon to keep building.
        using var icon = WfIcon.FromHandle(IntPtr.Zero);
        Assert.NotNull(icon);
        Assert.Equal(new WfDraw::System.Drawing.Size(16, 16), icon.Size);
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(16, bmp.Width);
        Assert.Equal(0, bmp.GetPixel(0, 0).ToArgb());
    }

    private static byte[] BuildIco((int W, int H, int Bpp, byte[] Data)[] entries)
    {
        using var ms = new MemoryStream();
        void U16(int v)
        {
            ms.WriteByte((byte)v);
            ms.WriteByte((byte)(v >> 8));
        }
        void I32(int v)
        {
            ms.WriteByte((byte)v);
            ms.WriteByte((byte)(v >> 8));
            ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 24));
        }
        U16(0);
        U16(1);
        U16(entries.Length);
        int off = 6 + 16 * entries.Length;
        foreach (var e in entries)
        {
            ms.WriteByte((byte)(e.W >= 256 ? 0 : e.W));
            ms.WriteByte((byte)(e.H >= 256 ? 0 : e.H));
            ms.WriteByte(0);
            ms.WriteByte(0);
            U16(1);
            U16(e.Bpp);
            I32(e.Data.Length);
            I32(off);
            off += e.Data.Length;
        }
        foreach (var e in entries)
            ms.Write(e.Data, 0, e.Data.Length);
        return ms.ToArray();
    }

    private static byte[] BuildBmpEntry(int w, int h, int bpp, int[] palette,
        (int R, int G, int B, int A)[][]? topRows, byte[] andMask, byte[]? rawXor = null)
    {
        using var ms = new MemoryStream();
        void I32(int v)
        {
            ms.WriteByte((byte)v);
            ms.WriteByte((byte)(v >> 8));
            ms.WriteByte((byte)(v >> 16));
            ms.WriteByte((byte)(v >> 24));
        }
        void U16(int v)
        {
            ms.WriteByte((byte)v);
            ms.WriteByte((byte)(v >> 8));
        }
        I32(40);
        I32(w);
        I32(h * 2);
        U16(1);
        U16(bpp);
        I32(0);
        I32(0);
        I32(0);
        I32(0);
        I32(palette.Length);
        I32(0);
        foreach (int c in palette)
        {
            ms.WriteByte((byte)c);
            ms.WriteByte((byte)(c >> 8));
            ms.WriteByte((byte)(c >> 16));
            ms.WriteByte(0);
        }
        int xorStride = ((w * bpp + 31) / 32) * 4;
        if (rawXor is not null)
        {
            ms.Write(rawXor, 0, rawXor.Length);
        }
        else
        {
            for (int y = h - 1; y >= 0; y--)
                for (int sx = 0; sx < xorStride; sx++)
                {
                    int x = sx / 4;
                    if (x < w)
                    {
                        var (R, G, B, A) = topRows![y][x];
                        byte[] px = [ (byte)B, (byte)G, (byte)R, (byte)A ];
                        ms.WriteByte(px[sx % 4]);
                    }
                    else
                    {
                        ms.WriteByte(0);
                    }
                }
        }
        ms.Write(andMask, 0, andMask.Length);
        return ms.ToArray();
    }

    // Minimal 1x1 RGBA PNG (filter 0, stored zlib block) with valid CRC/Adler.
    private static byte[] BuildPng1x1(byte r, byte g, byte b)
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10], 0, 8);
        void Chunk(string type, byte[] data)
        {
            byte[] tb = System.Text.Encoding.ASCII.GetBytes(type);
            ms.WriteByte((byte)(data.Length >> 24));
            ms.WriteByte((byte)(data.Length >> 16));
            ms.WriteByte((byte)(data.Length >> 8));
            ms.WriteByte((byte)data.Length);
            ms.Write(tb, 0, 4);
            ms.Write(data, 0, data.Length);
            uint crc = Crc(tb, data);
            ms.WriteByte((byte)(crc >> 24));
            ms.WriteByte((byte)(crc >> 16));
            ms.WriteByte((byte)(crc >> 8));
            ms.WriteByte((byte)crc);
        }
        byte[] ihdr = [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0];
        Chunk("IHDR", ihdr);
        byte[] raw = [0, r, g, b, 255];
        using var idat = new MemoryStream();
        idat.WriteByte(0x78);
        idat.WriteByte(0x01);
        idat.WriteByte(0x01);
        idat.WriteByte((byte)raw.Length);
        idat.WriteByte(0);
        idat.WriteByte((byte)(~raw.Length & 255));
        idat.WriteByte((byte)((~raw.Length >> 8) & 255));
        idat.Write(raw, 0, raw.Length);
        uint adler = Adler(raw);
        idat.WriteByte((byte)(adler >> 24));
        idat.WriteByte((byte)(adler >> 16));
        idat.WriteByte((byte)(adler >> 8));
        idat.WriteByte((byte)adler);
        Chunk("IDAT", idat.ToArray());
        Chunk("IEND", []);
        return ms.ToArray();
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) == 1 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in type)
            c = CrcTable[(c ^ b) & 255] ^ (c >> 8);
        foreach (byte b in data)
            c = CrcTable[(c ^ b) & 255] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static uint Adler(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte x in data)
        {
            a = (a + x) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    // Regression: RadDiagram MouseDown calls FromHicon during input handling;
    // throwing aborts selection entirely, so answer the same blank 16x16
    // placeholder Icon.FromHandle uses.
    [Fact]
    public void FromHicon_Answers_Blank_Placeholder_Instead_Of_Throwing()
    {
        using WfBitmap bmp = WfBitmap.FromHicon(System.IntPtr.Zero);
        Assert.Equal(16, bmp.Width);
        Assert.Equal(16, bmp.Height);
        Assert.Equal(0, bmp.GetPixel(0, 0).A);
        using WfBitmap other = WfBitmap.FromHicon(new System.IntPtr(1234));
        Assert.Equal(16, other.Width);
        Assert.Equal(16, other.Height);
    }

    // Real icons understate the directory size field (WinForms down.ico
    // declares 104 bytes for a 16x16x32 entry needing 1128); Windows
    // reads by geometry. Truncate the declared size and require decode.
    [Fact]
    public void Understated_Directory_Size_Decodes_By_Geometry()
    {
        byte[] entry = BuildBmpEntry(2, 2, 32, Array.Empty<int>(),
            topRows:
            [
                [(255, 0, 0, 255), (0, 255, 0, 255)],
                [(0, 0, 255, 255), (255, 255, 255, 255)],
            ],
            andMask: [0, 0, 0, 0, 0, 0, 0, 0]);
        byte[] ico = BuildIco([(2, 2, 32, entry)]);
        ico[14] = 48; ico[15] = 0; ico[16] = 0; ico[17] = 0;
        using var icon = new WfIcon(new MemoryStream(ico));
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), icon.Size);
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
    }

    // 32bpp icons with an alpha channel routinely omit the AND mask
    // (WinForms up.ico ships header+XOR only); Windows treats the
    // missing mask as all-opaque. Truncate it and require decode with
    // alpha taken from the XOR channel.
    [Fact]
    public void Missing_And_Mask_Decodes_As_Opaque()
    {
        byte[] full = BuildBmpEntry(2, 2, 32, Array.Empty<int>(),
            topRows:
            [
                [(255, 0, 0, 255), (0, 255, 0, 255)],
                [(0, 0, 255, 255), (255, 255, 255, 255)],
            ],
            andMask: [0, 0, 0, 0, 0, 0, 0, 0]);
        byte[] entry = full[..^8];
        byte[] ico = BuildIco([(2, 2, 32, entry)]);
        using var icon = new WfIcon(new MemoryStream(ico));
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), icon.Size);
        using WfBitmap bmp = icon.ToBitmap();
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(255, bmp.GetPixel(0, 0).A);
    }
}
