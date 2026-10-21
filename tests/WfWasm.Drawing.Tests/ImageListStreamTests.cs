extern alias WfDraw;

using System;
using System.Collections.Generic;
using System.IO;
using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfNrbf = WfDraw::WfWasm.Drawing.NrbfImage;
using Xunit;

public sealed class ImageListStreamTests
{
    // Real PhotoAlbum imageList2.ImageStream NRBF (ribbon_minimize /
    // ribbon_maximize / ribbon_close, 16x16, 8bpp + mask). Ground truth
    // verified against GDI+: strip 64x16, glyphs white-on-black with a
    // white-background mask (bit 1 = transparent).
    private const string RealBlobBase64 =
"AAEAAAD/////AQAAAAAAAAAMAgAAAFdTeXN0ZW0uV2luZG93cy5Gb3JtcywgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWI3N2E1YzU2MTkzNGUwODkFAQAAACZTeXN0ZW0uV2luZG93cy5Gb3Jtcy5JbWFnZUxpc3RTdHJlYW1lcgEAAAAERGF0YQcCAgAAAAkDAAAADwMAAADMBwAAAk1TRnQBSQFMAgEBAwEAATABAAEwAQABEAEAARABAAT/AQkBAAj/AUIBTQE2AQQGAAE2AQQCAAEoAwABQAMAARADAAEBAQABCAYAAQQYAAGAAgABgAMAAoABAAGAAwABgAEAAYABAAKAAgADwAEAAcAB3AHAAQAB8AHKAaYBAAEzBQABMwEAATMBAAEzAQACMwIAAxYBAAMcAQADIgEAAykBAANVAQADTQEAA0IBAAM5AQABgAF8Af8BAAJQAf8BAAGTAQAB1gEAAf8B7AHMAQABxgHWAe8BAAHWAucBAAGQAakBrQIAAf8BMwMAAWYDAAGZAwABzAIAATMDAAIzAgABMwFmAgABMwGZAgABMwHMAgABMwH/AgABZgMAAWYBMwIAAmYCAAFmAZkCAAFmAcwCAAFmAf8CAAGZAwABmQEzAgABmQFmAgACmQIAAZkBzAIAAZkB/wIAAcwDAAHMATMCAAHMAWYCAAHMAZkCAALMAgABzAH/AgAB/wFmAgAB/wGZAgAB/wHMAQABMwH/AgAB/wEAATMBAAEzAQABZgEAATMBAAGZAQABMwEAAcwBAAEzAQAB/wEAAf8BMwIAAzMBAAIzAWYBAAIzAZkBAAIzAcwBAAIzAf8BAAEzAWYCAAEzAWYBMwEAATMCZgEAATMBZgGZAQABMwFmAcwBAAEzAWYB/wEAATMBmQIAATMBmQEzAQABMwGZAWYBAAEzApkBAAEzAZkBzAEAATMBmQH/AQABMwHMAgABMwHMATMBAAEzAcwBZgEAATMBzAGZAQABMwLMAQABMwHMAf8BAAEzAf8BMwEAATMB/wFmAQABMwH/AZkBAAEzAf8BzAEAATMC/wEAAWYDAAFmAQABMwEAAWYBAAFmAQABZgEAAZkBAAFmAQABzAEAAWYBAAH/AQABZgEzAgABZgIzAQABZgEzAWYBAAFmATMBmQEAAWYBMwHMAQABZgEzAf8BAAJmAgACZgEzAQADZgEAAmYBmQEAAmYBzAEAAWYBmQIAAWYBmQEzAQABZgGZAWYBAAFmApkBAAFmAZkBzAEAAWYBmQH/AQABZgHMAgABZgHMATMBAAFmAcwBmQEAAWYCzAEAAWYBzAH/AQABZgH/AgABZgH/ATMBAAFmAf8BmQEAAWYB/wHMAQABzAEAAf8BAAH/AQABzAEAApkCAAGZATMBmQEAAZkBAAGZAQABmQEAAcwBAAGZAwABmQIzAQABmQEAAWYBAAGZATMBzAEAAZkBAAH/AQABmQFmAgABmQFmATMBAAGZATMBZgEAAZkBZgGZAQABmQFmAcwBAAGZATMB/wEAApkBMwEAApkBZgEAA5kBAAKZAcwBAAKZAf8BAAGZAcwCAAGZAcwBMwEAAWYBzAFmAQABmQHMAZkBAAGZAswBAAGZAcwB/wEAAZkB/wIAAZkB/wEzAQABmQHMAWYBAAGZAf8BmQEAAZkB/wHMAQABmQL/AQABzAMAAZkBAAEzAQABzAEAAWYBAAHMAQABmQEAAcwBAAHMAQABmQEzAgABzAIzAQABzAEzAWYBAAHMATMBmQEAAcwBMwHMAQABzAEzAf8BAAHMAWYCAAHMAWYBMwEAAZkCZgEAAcwBZgGZAQABzAFmAcwBAAGZAWYB/wEAAcwBmQIAAcwBmQEzAQABzAGZAWYBAAHMApkBAAHMAZkBzAEAAcwBmQH/AQACzAIAAswBMwEAAswBZgEAAswBmQEAA8wBAALMAf8BAAHMAf8CAAHMAf8BMwEAAZkB/wFmAQABzAH/AZkBAAHMAf8BzAEAAcwC/wEAAcwBAAEzAQAB/wEAAWYBAAH/AQABmQEAAcwBMwIAAf8CMwEAAf8BMwFmAQAB/wEzAZkBAAH/ATMBzAEAAf8BMwH/AQAB/wFmAgAB/wFmATMBAAHMAmYBAAH/AWYBmQEAAf8BZgHMAQABzAFmAf8BAAH/AZkCAAH/AZkBMwEAAf8BmQFmAQAB/wKZAQAB/wGZAcwBAAH/AZkB/wEAAf8BzAIAAf8BzAEzAQAB/wHMAWYBAAH/AcwBmQEAAf8CzAEAAf8BzAH/AQAC/wEzAQABzAH/AWYBAAL/AZkBAAL/AcwBAAJmAf8BAAFmAf8BZgEAAWYC/wEAAf8CZgEAAf8BZgH/AQAC/wFmAQABIQEAAaUBAANfAQADdwEAA4YBAAOWAQADywEAA7IBAAPXAQAD3QEAA+MBAAPqAQAD8QEAA/gBAAHwAfsB/wEAAaQCoAEAA4ADAAH/AgAB/wMAAv8BAAH/AwAB/wEAAf8BAAL/AgAD//8ABQAH9xkAA+8DAAPvFwAH7AkACfcIAAP3AQAD9ygAAfcHAAH3CQAF9ykAAfcHAAH3CgAD9yoAAfcHAAH3CgAD7SoACe0JAALtAewC7SkACewIAALtAewBAAHsAu0oAAnsBwAD7AMAA+z/ABUAAUIBTQE+BwABPgMAASgDAAFAAwABEAMAAQEBAAEBBQABgBcAA/8BAAb/AgAG/wIABv8CAAb/AgAB4AE/Av8B4wGPAgAB4AE/AeABDwHxAR8CAAL/Au8B+AE/AgAC/wLvAfwBfwIAAv8C7wH8AX8CAAL/AeABDwH4AT8CAAL/AeABDwHxAR8CAAL/AeABDwHjAY8CAAb/AgAG/wIABv8CAAb/AgAL";
    [Fact]
    public void Real_PhotoAlbum_Blob_Decodes_Three_Glyphs()
    {
        byte[] nrbf = Convert.FromBase64String(RealBlobBase64);
        Assert.True(WfNrbf.TryExtractImageListBytes(nrbf, out byte[] msft));
        Assert.Equal((byte)'M', msft[0]);
        Assert.True(WfNrbf.TryDecodeImageListStream(msft, out WfBitmap[]? images, out var slot, out int depth));
        Assert.NotNull(images);
        Assert.Equal(3, images.Length);
        Assert.Equal(new WfDraw::System.Drawing.Size(16, 16), slot);
        Assert.Equal(8, depth);
        // Grounded opaque-pixel counts from the mask (GDI+): dash 14,
        // square 42, X 40.
        int[] expectedOpaque = [14, 42, 40];
        WfColor slot2Center = default;
        for (int slotIndex = 0; slotIndex < images.Length; slotIndex++)
        {
            WfBitmap? glyph = images[slotIndex];
            Assert.NotNull(glyph);
            Assert.Equal(16, glyph.Width);
            Assert.Equal(16, glyph.Height);
            // Black strip background is mask-transparent.
            Assert.Equal(0, glyph.GetPixel(0, 0).A);
            int opaque = 0;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    if (glyph.GetPixel(x, y).A != 0)
                        opaque++;
            Assert.Equal(expectedOpaque[slotIndex], opaque);
            if (slotIndex == 2)
                slot2Center = glyph.GetPixel(8, 8);
            glyph.Dispose();
        }
        // GDI+-grounded spot check: slot2 (close X) center is opaque gray.
        Assert.Equal(255, slot2Center.A);
        Assert.True(slot2Center.R > 100, $"Expected grayish X arm, got {slot2Center}.");
    }

    [Fact]
    public void Real_Blob_Carries_No_Plain_Image_Magic()
    {
        // The MSFt body is RLE-compressed: the plain image extractor
        // must not claim it (routing split between the two extractors).
        byte[] nrbf = Convert.FromBase64String(RealBlobBase64);
        Assert.False(WfNrbf.TryExtractImageBytes(nrbf, out _));
    }

    [Fact]
    public void Synthetic_Two_Slot_Decode_Applies_Mask()
    {
        // 2 slots of 2x2, 8bpp palette [black, red, lime, blue].
        // Top row indices [1,2,3,1], bottom row [2,3,1,2].
        byte[] palette = [
            0, 0, 0, 0,
            0, 0, 255, 0,
            0, 255, 0, 0,
            255, 0, 0, 0,
        ];
        byte[] xor = [2, 3, 1, 2, 1, 2, 3, 1]; // bottom-up rows
        byte[] colorBmp = BuildBmp(4, 2, 8, palette, xor);
        // Mask: top row bits (MSB-first) 0,0,1,0 (slot1/x2
        // transparent), bottom row all opaque.
        byte[] maskBits = [0x00, 0, 0, 0, 0x20, 0, 0, 0]; // bottom-up
        byte[] maskBmp = BuildBmp(4, 2, 1, [0, 0, 0, 0, 255, 255, 255, 0], maskBits);
        byte[] il = BuildImageListStream(2, 2, 2, colorBmp, maskBmp);
        byte[] msft = RleEncode(il);

        Assert.True(WfNrbf.TryDecodeImageListStream(msft, out WfBitmap[]? images, out var slot, out int depth));
        Assert.NotNull(images);
        Assert.Equal(2, images.Length);
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), slot);
        Assert.Equal(8, depth);

        WfBitmap slot0 = images[0];
        Assert.Equal(WfColor.Red.ToArgb(), slot0.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), slot0.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), slot0.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), slot0.GetPixel(1, 1).ToArgb());

        WfBitmap slot1 = images[1];
        Assert.Equal(0, slot1.GetPixel(0, 0).A); // mask-transparent
        Assert.Equal(WfColor.Red.ToArgb(), slot1.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), slot1.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), slot1.GetPixel(1, 1).ToArgb());
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { (byte)'M', (byte)'S', (byte)'F', (byte)'X' })]
    [InlineData(new byte[] { (byte)'M', (byte)'S', (byte)'F', (byte)'t', 1 })]
    public void Corrupt_Streams_Answer_False(byte[] msft)
    {
        Assert.False(WfNrbf.TryDecodeImageListStream(msft, out _, out _, out _));
    }

    [Fact]
    public void Corrupt_Inner_Streams_Answer_False()
    {
        Assert.False(WfNrbf.TryDecodeImageListStream(null, out _, out _, out _));
        // Valid RLE framing, wrong IL magic.
        byte[] badHead = RleEncode([(byte)'X', (byte)'L', 0, 0, 1, 0, 0, 0, 0, 0, 2, 0, 2, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.False(WfNrbf.TryDecodeImageListStream(badHead, out _, out _, out _));
        // Right magic, zero images.
        byte[] zeroCount = RleEncode([(byte)'I', (byte)'L', 1, 1, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        Assert.False(WfNrbf.TryDecodeImageListStream(zeroCount, out _, out _, out _));
        // Right head, no color BMP.
        byte[] noBmp = RleEncode([(byte)'I', (byte)'L', 1, 1, 1, 0, 0, 0, 0, 0, 2, 0, 2, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9, 9, 9, 9]);
        Assert.False(WfNrbf.TryDecodeImageListStream(noBmp, out _, out _, out _));
    }

    private static byte[] BuildImageListStream(int imageCount, int cx, int cy, byte[] colorBmp, byte[] maskBmp)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)'I');
            w.Write((byte)'L');
            w.Write((ushort)0x0101);
            w.Write((ushort)imageCount);
            w.Write((ushort)4);
            w.Write((ushort)4);
            w.Write((ushort)cx);
            w.Write((ushort)cy);
            w.Write(0xFFFFFFFFu);
            w.Write((ushort)0x0009);
            w.Write((short)-1);
            w.Write((short)-1);
            w.Write((short)-1);
            w.Write((short)-1);
            w.Write(colorBmp);
            w.Write(maskBmp);
        }
        return ms.ToArray();
    }

    private static byte[] BuildBmp(int width, int height, int bpp, byte[] palette, byte[] bits)
    {
        int dataOff = 14 + 40 + palette.Length;
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)'B');
            w.Write((byte)'M');
            w.Write(dataOff); // bogus bfSize like real IL streams (headers only)
            w.Write(0);
            w.Write(dataOff);
            w.Write(40);
            w.Write(width);
            w.Write(height);
            w.Write((ushort)1);
            w.Write((ushort)bpp);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(palette.Length / 4);
            w.Write(0);
            w.Write(palette);
            w.Write(bits);
        }
        return ms.ToArray();
    }

    private static byte[] RleEncode(byte[] il)
    {
        var outBytes = new List<byte> { (byte)'M', (byte)'S', (byte)'F', (byte)'t' };
        int i = 0;
        while (i < il.Length)
        {
            int run = 1;
            while (i + run < il.Length && il[i + run] == il[i] && run < 255)
                run++;
            outBytes.Add((byte)run);
            outBytes.Add(il[i]);
            i += run;
        }
        return [.. outBytes];
    }
}
