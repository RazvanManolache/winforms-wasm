extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;

public sealed class BmpCodecTests
{
    [Fact]
    public void Bit24_BottomUp_DecodesExactPixels()
    {
        // 2x2: file rows bottom-first (blue,white / red,green).
        byte[] rows =
        [
            255, 0, 0, 255, 255, 255, 0, 0, // blue, white + pad
            0, 0, 255, 0, 255, 0, 0, 0, // red, green + pad
        ];
        using var bitmap = DecodeBmp(BuildBmp(2, 2, 24, 0, [], rows));
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), bitmap.Size);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 255, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void NegativeHeight_DecodesTopDown()
    {
        byte[] rows =
        [
            0, 0, 255, 0, // red + pad
            255, 0, 0, 0, // blue + pad
        ];
        using var bitmap = DecodeBmp(BuildBmp(1, -2, 24, 0, [], rows));
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
    }

    [Fact]
    public void Bit32_RGB_Is_Opaque()
    {
        byte[] rows = [16, 32, 48, 99, 200, 100, 50, 77];
        using var bitmap = DecodeBmp(BuildBmp(2, 1, 32, 0, [], rows));
        Assert.Equal(WfColor.FromArgb(255, 48, 32, 16).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 50, 100, 200).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Bit32_V4_Alpha_Preserves_Transparency()
    {
        // V4 header with alpha mask; GDI+ keeps per-pixel alpha.
        byte[] rows = [0, 0, 255, 128, 0, 255, 0, 0];
        using var bitmap = DecodeBmp(BuildBmpV4(2, 1, rows));
        Assert.Equal(WfColor.FromArgb(128, 255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 255, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Bit8_Palette_Indexes_Colors()
    {
        byte[] palette = [0, 0, 0, 0, 255, 255, 255, 0]; // black, white
        byte[] rows = [1, 0, 0, 0, 0, 1, 0, 0]; // bottom: white,black / top: black,white
        using var bitmap = DecodeBmp(BuildBmp(2, 2, 8, 0, palette, rows));
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void Bit4_Palette_Unpacks_Nibbles()
    {
        byte[] palette = [0, 0, 255, 0, 0, 255, 0, 0]; // red, green
        byte[] rows = [0x10, 0, 0, 0]; // green, red + pad
        using var bitmap = DecodeBmp(BuildBmp(2, 1, 4, 0, palette, rows));
        Assert.Equal(WfColor.FromArgb(0, 255, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Bit1_Palette_Unpacks_Bits()
    {
        byte[] palette = [255, 255, 255, 0, 0, 0, 0, 0]; // white, black
        byte[] rows = [0b10100000, 0, 0, 0];
        using var bitmap = DecodeBmp(BuildBmp(3, 1, 1, 0, palette, rows));
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void Bit16_555_And_565_Scale_To_8Bit()
    {
        // 555 red (0x7C00) via BI_RGB; 565 green (0x07E0) via BITFIELDS.
        using var rgb = DecodeBmp(BuildBmp(1, 1, 16, 0, [], [0x00, 0x7C, 0, 0]));
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), rgb.GetPixel(0, 0).ToArgb());
        byte[] fields = [..BitConverter.GetBytes(0xF800u), ..BitConverter.GetBytes(0x07E0u), ..BitConverter.GetBytes(0x001Fu)];
        using var b565 = DecodeBmp(BuildBmp(1, 1, 16, 3, fields, [0xE0, 0x07, 0, 0]));
        Assert.Equal(WfColor.FromArgb(0, 255, 0).ToArgb(), b565.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Corrupt_Throws_OutOfMemory_Rle_Throws_NotSupported()
    {
        Assert.Throws<OutOfMemoryException>(() => DecodeBmp([(byte)'B', (byte)'M', 1, 2, 3]));
        Assert.Throws<OutOfMemoryException>(() => DecodeBmp(BuildBmp(2, 2, 24, 0, [], [1, 2])));
        Assert.Throws<NotSupportedException>(() => DecodeBmp(BuildBmp(2, 2, 8, 1, [], new byte[8])));
    }

    private static WfBitmap DecodeBmp(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new WfBitmap(stream);
    }

    // INFO-header BMP: file header + 40-byte header + palette/masks + rows.
    private static byte[] BuildBmp(int width, int height, ushort bpp, int compression, byte[] paletteOrMasks, byte[] rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataOffset = 14 + 40 + paletteOrMasks.Length;
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(dataOffset + rows.Length);
        w.Write(0);
        w.Write(dataOffset);
        w.Write(40);
        w.Write(width); w.Write(height);
        w.Write((ushort)1); w.Write(bpp);
        w.Write(compression);
        w.Write(rows.Length);
        w.Write(0); w.Write(0);
        w.Write(bpp <= 8 ? paletteOrMasks.Length / 4 : 0); // colorsUsed
        w.Write(0);
        w.Write(paletteOrMasks);
        w.Write(rows);
        return ms.ToArray();
    }

    // V4 BMP (108-byte header) with 32-bit A8R8G8B8 masks.
    private static byte[] BuildBmpV4(int width, int height, byte[] rows)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataOffset = 14 + 108;
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(dataOffset + rows.Length);
        w.Write(0);
        w.Write(dataOffset);
        w.Write(108);
        w.Write(width); w.Write(height);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(3); // BITFIELDS
        w.Write(rows.Length);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        w.Write(0x00FF0000u); w.Write(0x0000FF00u); w.Write(0x000000FFu); w.Write(0xFF000000u);
        w.Write(1); // LCS_WINDOWS_COLOR_SPACE
        w.Write(new byte[36]); // endpoints
        w.Write(0); w.Write(0); w.Write(0); // gamma
        w.Write(rows);
        return ms.ToArray();
    }
}
