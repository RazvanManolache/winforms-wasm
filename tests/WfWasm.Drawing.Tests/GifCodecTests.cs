extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfImage = WfDraw::System.Drawing.Image;

public sealed class GifCodecTests
{
    [Fact]
    public void SingleFrame_FullScreen_DecodesExactPixels()
    {
        byte[] gct = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        byte[] gif = BuildGif(2, 2, gct, w => WriteImage(w, 0, 0, 2, 2, [0, 1, 2, 3], 2));
        using var bitmap = DecodeGif(gif);
        Assert.Equal(new WfDraw::System.Drawing.Size(2, 2), bitmap.Size);
        Assert.Equal(WfDraw::System.Drawing.Imaging.PixelFormat.Format32bppArgb, bitmap.PixelFormat);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 255, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void LzwEncoder_EmitsHandVerifiedBytes()
    {
        // Pixels [1,2], min code size 2: CLEAR(4),1,2,EOI(5) at 3 bits,
        // LSB-first -> 0x8C 0x0A (hand-packed; anchors the encoder every
        // other fixture round-trips through).
        Assert.Equal(new byte[] { 0x8C, 0x0A }, LzwEncode([1, 2], 2));
    }

    [Fact]
    public void HandPackedStream_DecodesWithoutEncoder()
    {
        // Same hand bits as above, fed raw: decoder verified
        // independently of the test encoder.
        byte[] gct = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];
        byte[] gif = BuildGif(2, 1, gct, w => WriteImageRaw(w, 0, 0, 2, 1, [0x8C, 0x0A], 2));
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 255, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Transparency_GceTransparentIndex_YieldsAlphaZero()
    {
        byte[] gct = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        byte[] gif = BuildGif(2, 1, gct, w =>
        {
            // Packed 0x05: disposal 1 + transparent flag; nonzero delay
            // proves timing/disposal fields are ignored (first-frame still).
            WriteGce(w, transparent: true, transparentIndex: 1, disposal: 1, delay: 10);
            WriteImage(w, 0, 0, 2, 1, [0, 1], 2);
        });
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        // GDI+ parity: palette RGB under a zero alpha (0000FF00).
        Assert.Equal(WfColor.FromArgb(0, 0, 255, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void Interlaced_FourPass_ReordersRows()
    {
        // 1x8: every pass contributes (0 / 4 / 2,6 / 1,3,5,7).
        byte[] gct = new byte[8 * 3];
        for (int i = 0; i < 8; i++)
        {
            gct[i * 3] = (byte)(i * 30 + 10);
            gct[i * 3 + 1] = (byte)(i * 20 + 5);
            gct[i * 3 + 2] = (byte)(i * 10);
        }
        byte[] rows = [0, 1, 2, 3, 4, 5, 6, 7];
        byte[] gif = BuildGif(1, 8, gct, w => WriteImage(w, 0, 0, 1, 8, rows, 3, interlaced: true));
        using var bitmap = DecodeGif(gif);
        for (int r = 0; r < 8; r++)
            Assert.Equal(
                WfColor.FromArgb(gct[r * 3], gct[r * 3 + 1], gct[r * 3 + 2]).ToArgb(),
                bitmap.GetPixel(0, r).ToArgb());
    }

    [Fact]
    public void Animated_DecodesFirstFrameOnly()
    {
        byte[] gct = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        byte[] gif = BuildGif(2, 1, gct, w =>
        {
            WriteGce(w, transparent: false, transparentIndex: 0);
            WriteImage(w, 0, 0, 2, 1, [0, 0], 2);
            WriteGce(w, transparent: false, transparentIndex: 0, disposal: 2, delay: 5);
            WriteImage(w, 0, 0, 2, 1, [1, 1], 2);
        });
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void LocalPalette_OverridesGlobal()
    {
        byte[] gct = [0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0];
        byte[] lct = [255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0];
        byte[] gif = BuildGif(1, 1, gct, w => WriteImage(w, 0, 0, 1, 1, [0], 2, localPalette: lct));
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void NoGlobal_WithLocal_Decodes()
    {
        byte[] lct = [0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255];
        byte[] gif = BuildGif(1, 1, null, w => WriteImage(w, 0, 0, 1, 1, [0], 2, localPalette: lct));
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void FrameOffset_CompositesOntoLogicalScreen()
    {
        byte[] gct = [255, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0];
        // Background index 1 (green) is descriptive, not painted:
        // uncovered canvas stays transparent.
        byte[] gif = BuildGif(4, 4, gct, w => WriteImage(w, 1, 1, 2, 2, [0, 0, 0, 0], 2), backgroundIndex: 1);
        using var bitmap = DecodeGif(gif);
        Assert.Equal(new WfDraw::System.Drawing.Size(4, 4), bitmap.Size);
        Assert.Equal(0, bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
    }

    [Fact]
    public void Extensions_Skipped()
    {
        byte[] gct = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        byte[] gif = BuildGif(1, 1, gct, w =>
        {
            // NETSCAPE looping block, comment, and plain-text: all skipped.
            WriteRawExtension(w, 0xFF, [.. System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"), 3, 1, 0, 0, 0]);
            WriteRawExtension(w, 0xFE, [.. System.Text.Encoding.ASCII.GetBytes("hello")]);
            WriteRawExtension(w, 0x01, [1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 1, 1, 65]);
            WriteImage(w, 0, 0, 1, 1, [2], 2);
        });
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(0, 0, 255).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Gif87a_Decodes()
    {
        byte[] gct = [1, 2, 3, 4, 5, 6];
        byte[] gif = BuildGif(1, 1, gct, w => WriteImage(w, 0, 0, 1, 1, [1], 2), version: "87a");
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(4, 5, 6).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void OneBit_MinCodeSize2_Decodes()
    {
        // Minimum code size 1 is degenerate (no width rule keeps the
        // encoder and decoder synchronized; real encoders emit >= 2 —
        // GDI+ always emits 8). The decoder stays lenient for
        // giflib parity, but fixtures use mainstream sizes only.
        byte[] gct = [0, 0, 0, 255, 255, 255];
        byte[] gif = BuildGif(4, 1, gct, w => WriteImage(w, 0, 0, 4, 1, [0, 1, 0, 1], 2));
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(3, 0).ToArgb());
    }

    [Fact]
    public void Large_RunPattern_RoundTrips_And_Compresses()
    {
        // 48x48 solid-row bands: long runs force dictionary growth
        // (wide codes) and the KwKwK path; compressed bytes must be
        // far smaller than the pixel count to prove those paths ran.
        byte[] gct = new byte[16 * 3];
        for (int i = 0; i < 16; i++)
        {
            gct[i * 3] = (byte)(i * 16);
            gct[i * 3 + 1] = (byte)(255 - i * 16);
            gct[i * 3 + 2] = (byte)(i * 8);
        }
        byte[] pixels = new byte[48 * 48];
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
                pixels[y * 48 + x] = (byte)((y / 3) % 16);
        byte[] compressed = LzwEncode(pixels, 4);
        Assert.True(compressed.Length < pixels.Length / 4,
            $"expected dictionary compression, got {compressed.Length} bytes for {pixels.Length} pixels");
        byte[] gif = BuildGif(48, 48, gct, w => WriteImageRaw(w, 0, 0, 48, 48, compressed, 4));
        using var bitmap = DecodeGif(gif);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
            {
                int i = pixels[y * 48 + x];
                Assert.Equal(
                    WfColor.FromArgb(gct[i * 3], gct[i * 3 + 1], gct[i * 3 + 2]).ToArgb(),
                    bitmap.GetPixel(x, y).ToArgb());
            }
    }

    [Fact]
    public void Image_FromStream_And_FromFile_DecodeGif()
    {
        byte[] gct = [9, 8, 7, 0, 0, 0];
        byte[] gif = BuildGif(1, 1, gct, w => WriteImage(w, 0, 0, 1, 1, [0], 2));
        using (var stream = new MemoryStream(gif))
        using (WfImage image = WfImage.FromStream(stream))
        {
            Assert.Equal(new WfDraw::System.Drawing.Size(1, 1), image.Size);
        }
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        try
        {
            File.WriteAllBytes(path, gif);
            using WfImage image = WfImage.FromFile(path);
            var bitmap = Assert.IsType<WfBitmap>(image);
            Assert.Equal(WfColor.FromArgb(9, 8, 7).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TransparentPixel_HandPacked9Bit_MatchesGdiPlus()
    {
        // Pixels [5,252], min code size 8: CLEAR(256),5,252,EOI(257)
        // at 9 bits, LSB-first -> 00 0B F0 0B 08 (hand-packed; GDI+
        // decodes the same bytes to FFFF0000 00090807).
        byte[] gct = new byte[256 * 3];
        gct[5 * 3] = 255;
        gct[252 * 3] = 9; gct[252 * 3 + 1] = 8; gct[252 * 3 + 2] = 7;
        byte[] gif = BuildGif(2, 1, gct, w =>
        {
            WriteGce(w, transparent: true, transparentIndex: 252);
            WriteImageRaw(w, 0, 0, 2, 1, [0x00, 0x0B, 0xF0, 0x0B, 0x08], 8);
        });
        using var bitmap = DecodeGif(gif);
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 9, 8, 7).ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    // Real-encoder bytes (GDI+ Bitmap.Save, min code size 8, 256-color
    // GCT, GCE trans=252): expected pixels are GDI+'s own reload, so
    // these tests assert byte-level parity with desktop decoding.
    private const string TwocolorGifBase64 = "R0lGODlhBAABAPcAAAAAAAAAMwAAZgAAmQAAzAAA/wArAAArMwArZgArmQArzAAr/wBVAABVMwBVZgBVmQBVzABV/wCAAACAMwCAZgCAmQCAzACA/wCqAACqMwCqZgCqmQCqzACq/wDVAADVMwDVZgDVmQDVzADV/wD/AAD/MwD/ZgD/mQD/zAD//zMAADMAMzMAZjMAmTMAzDMA/zMrADMrMzMrZjMrmTMrzDMr/zNVADNVMzNVZjNVmTNVzDNV/zOAADOAMzOAZjOAmTOAzDOA/zOqADOqMzOqZjOqmTOqzDOq/zPVADPVMzPVZjPVmTPVzDPV/zP/ADP/MzP/ZjP/mTP/zDP//2YAAGYAM2YAZmYAmWYAzGYA/2YrAGYrM2YrZmYrmWYrzGYr/2ZVAGZVM2ZVZmZVmWZVzGZV/2aAAGaAM2aAZmaAmWaAzGaA/2aqAGaqM2aqZmaqmWaqzGaq/2bVAGbVM2bVZmbVmWbVzGbV/2b/AGb/M2b/Zmb/mWb/zGb//5kAAJkAM5kAZpkAmZkAzJkA/5krAJkrM5krZpkrmZkrzJkr/5lVAJlVM5lVZplVmZlVzJlV/5mAAJmAM5mAZpmAmZmAzJmA/5mqAJmqM5mqZpmqmZmqzJmq/5nVAJnVM5nVZpnVmZnVzJnV/5n/AJn/M5n/Zpn/mZn/zJn//8wAAMwAM8wAZswAmcwAzMwA/8wrAMwrM8wrZswrmcwrzMwr/8xVAMxVM8xVZsxVmcxVzMxV/8yAAMyAM8yAZsyAmcyAzMyA/8yqAMyqM8yqZsyqmcyqzMyq/8zVAMzVM8zVZszVmczVzMzV/8z/AMz/M8z/Zsz/mcz/zMz///8AAP8AM/8AZv8Amf8AzP8A//8rAP8rM/8rZv8rmf8rzP8r//9VAP9VM/9VZv9Vmf9VzP9V//+AAP+AM/+AZv+Amf+AzP+A//+qAP+qM/+qZv+qmf+qzP+q///VAP/VM//VZv/Vmf/VzP/V////AP//M///Zv//mf//zP///wAAAAAAAAAAAAAAACH5BAEAAPwALAAAAAAEAAEAAAgGAAHsExgQADs=";
    private const string Distinct8GifBase64 = "R0lGODlhCAABAPcAAAAAAAAAMwAAZgAAmQAAzAAA/wArAAArMwArZgArmQArzAAr/wBVAABVMwBVZgBVmQBVzABV/wCAAACAMwCAZgCAmQCAzACA/wCqAACqMwCqZgCqmQCqzACq/wDVAADVMwDVZgDVmQDVzADV/wD/AAD/MwD/ZgD/mQD/zAD//zMAADMAMzMAZjMAmTMAzDMA/zMrADMrMzMrZjMrmTMrzDMr/zNVADNVMzNVZjNVmTNVzDNV/zOAADOAMzOAZjOAmTOAzDOA/zOqADOqMzOqZjOqmTOqzDOq/zPVADPVMzPVZjPVmTPVzDPV/zP/ADP/MzP/ZjP/mTP/zDP//2YAAGYAM2YAZmYAmWYAzGYA/2YrAGYrM2YrZmYrmWYrzGYr/2ZVAGZVM2ZVZmZVmWZVzGZV/2aAAGaAM2aAZmaAmWaAzGaA/2aqAGaqM2aqZmaqmWaqzGaq/2bVAGbVM2bVZmbVmWbVzGbV/2b/AGb/M2b/Zmb/mWb/zGb//5kAAJkAM5kAZpkAmZkAzJkA/5krAJkrM5krZpkrmZkrzJkr/5lVAJlVM5lVZplVmZlVzJlV/5mAAJmAM5mAZpmAmZmAzJmA/5mqAJmqM5mqZpmqmZmqzJmq/5nVAJnVM5nVZpnVmZnVzJnV/5n/AJn/M5n/Zpn/mZn/zJn//8wAAMwAM8wAZswAmcwAzMwA/8wrAMwrM8wrZswrmcwrzMwr/8xVAMxVM8xVZsxVmcxVzMxV/8yAAMyAM8yAZsyAmcyAzMyA/8yqAMyqM8yqZsyqmcyqzMyq/8zVAMzVM8zVZszVmczVzMzV/8z/AMz/M8z/Zsz/mcz/zMz///8AAP8AM/8AZv8Amf8AzP8A//8rAP8rM/8rZv8rmf8rzP8r//9VAP9VM/9VZv9Vmf9VzP9V//+AAP+AM/+AZv+Amf+AzP+A//+qAP+qM/+qZv+qmf+qzP+q///VAP/VM//VZv/Vmf/VzP/V////AP//M///Zv//mf//zP///wAAAAAAAAAAAAAAACH5BAEAAPwALAAAAAAIAAEAAAgMAAGogAFm0aJdwQICADs=";
    private const string Bands48GifBase64 = "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";

    [Fact]
    public void RealEncoder_Twocolor_MatchesGdiPlus()
    {
        using var bitmap = DecodeGif(Convert.FromBase64String(TwocolorGifBase64));
        Assert.Equal(new WfDraw::System.Drawing.Size(4, 1), bitmap.Size);
        int[] expected = [unchecked((int)0xFF000000), unchecked((int)0xFFFFFFFF), unchecked((int)0xFF000000), unchecked((int)0xFFFFFFFF)];
        for (int x = 0; x < 4; x++)
            Assert.Equal(expected[x], bitmap.GetPixel(x, 0).ToArgb());
    }

    [Fact]
    public void RealEncoder_Distinct8_MatchesGdiPlus()
    {
        using var bitmap = DecodeGif(Convert.FromBase64String(Distinct8GifBase64));
        Assert.Equal(new WfDraw::System.Drawing.Size(8, 1), bitmap.Size);
        int[] expected =
        [
            unchecked((int)0xFF000000), unchecked((int)0xFF330000),
            unchecked((int)0xFF332B00), unchecked((int)0xFF665500),
            unchecked((int)0xFF995533), unchecked((int)0xFF995533),
            unchecked((int)0xFFCC8033), unchecked((int)0xFFCCAA33),
        ];
        for (int x = 0; x < 8; x++)
            Assert.Equal(expected[x], bitmap.GetPixel(x, 0).ToArgb());
    }

    [Fact]
    public void RealEncoder_Bands48_MatchesGdiPlus()
    {
        using var bitmap = DecodeGif(Convert.FromBase64String(Bands48GifBase64));
        Assert.Equal(new WfDraw::System.Drawing.Size(48, 48), bitmap.Size);
        // Spot checks (from GDI+'s reload) plus an FNV-1a hash over all
        // 2304 ARGBs: the LZW stream grows through 9/10/11-bit codes,
        // so any width-rule drift fails loudly.
        Assert.Equal(unchecked((int)0xFF00FF00), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(unchecked((int)0xFF00FF00), bitmap.GetPixel(47, 0).ToArgb());
        Assert.Equal(unchecked((int)0xFFFF0066), bitmap.GetPixel(0, 47).ToArgb());
        Assert.Equal(unchecked((int)0xFFFF0066), bitmap.GetPixel(47, 47).ToArgb());
        Assert.Equal(unchecked((int)0xFF998033), bitmap.GetPixel(24, 24).ToArgb());
        Assert.Equal(unchecked((int)0xFF995566), bitmap.GetPixel(12, 30).ToArgb());
        uint hash = 2166136261;
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
                foreach (byte b in BitConverter.GetBytes(bitmap.GetPixel(x, y).ToArgb()))
                    hash = (hash ^ b) * 16777619;
        Assert.Equal(0x421D8DA0u, hash);
    }

    [Fact]
    public void Corrupt_ThrowsOutOfMemory()
    {
        byte[] gct = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        byte[] good = BuildGif(2, 2, gct, w => WriteImage(w, 0, 0, 2, 2, [0, 1, 2, 3], 2));
        // Truncated header / bad magic / truncated tail.
        Assert.Throws<OutOfMemoryException>(() => DecodeGif([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a']));
        Assert.Throws<OutOfMemoryException>(() => DecodeGif([(byte)'G', (byte)'I', (byte)'F', (byte)'9', (byte)'9', (byte)'x', 1, 2, 3, 4, 5, 6, 7]));
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(good[..(good.Length / 2)]));
        // Trailer with no frame.
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(BuildGif(2, 2, gct, w => { })));
        // Zero-size frame.
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(BuildGif(2, 2, gct, w => WriteImageRaw(w, 0, 0, 0, 2, [0x8C], 2))));
        // Illegal LZW minimum code sizes.
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(BuildGif(2, 2, gct, w => WriteImageRaw(w, 0, 0, 2, 2, new byte[4], 0))));
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(BuildGif(2, 2, gct, w => WriteImageRaw(w, 0, 0, 2, 2, new byte[4], 9))));
        // Unknown block introducer.
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(BuildGif(2, 2, gct, w => w.Write((byte)0x55))));
        // Pixel index outside the palette.
        Assert.Throws<OutOfMemoryException>(() => DecodeGif(BuildGif(2, 1, gct, w => WriteImage(w, 0, 0, 2, 1, [0, 9], 4))));
    }

    private static WfBitmap DecodeGif(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new WfBitmap(stream);
    }

    private static byte[] BuildGif(
        int screenWidth, int screenHeight, byte[]? globalPalette,
        Action<BinaryWriter> body, int backgroundIndex = 0, string version = "89a")
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("GIF" + version));
        w.Write((ushort)screenWidth);
        w.Write((ushort)screenHeight);
        if (globalPalette is null)
        {
            w.Write((byte)0x70);
        }
        else
        {
            int sizeBits = PaletteSizeBits(globalPalette.Length / 3);
            w.Write((byte)(0x80 | 0x70 | sizeBits));
        }
        w.Write((byte)backgroundIndex);
        w.Write((byte)0);
        if (globalPalette is not null)
            w.Write(globalPalette);
        body(w);
        w.Write((byte)0x3B);
        return ms.ToArray();
    }

    private static void WriteGce(BinaryWriter w, bool transparent, int transparentIndex, int disposal = 0, int delay = 0)
    {
        w.Write((byte)0x21);
        w.Write((byte)0xF9);
        w.Write((byte)4);
        w.Write((byte)((disposal << 2) | (transparent ? 1 : 0)));
        w.Write((ushort)delay);
        w.Write((byte)transparentIndex);
        w.Write((byte)0);
    }

    private static void WriteRawExtension(BinaryWriter w, byte label, byte[] payload)
    {
        w.Write((byte)0x21);
        w.Write(label);
        WriteSubBlocks(w, payload);
    }

    private static void WriteImage(
        BinaryWriter w, int left, int top, int width, int height,
        byte[] pixels, int minCodeSize, byte[]? localPalette = null, bool interlaced = false)
    {
        byte[] fileOrder = interlaced ? ToInterlaceOrder(pixels, width, height) : pixels;
        WriteImageRaw(w, left, top, width, height, LzwEncode(fileOrder, minCodeSize), minCodeSize, localPalette, interlaced);
    }

    private static void WriteImageRaw(
        BinaryWriter w, int left, int top, int width, int height,
        byte[] compressed, int minCodeSize, byte[]? localPalette = null, bool interlaced = false)
    {
        w.Write((byte)0x2C);
        w.Write((ushort)left);
        w.Write((ushort)top);
        w.Write((ushort)width);
        w.Write((ushort)height);
        int packed = interlaced ? 0x40 : 0;
        if (localPalette is not null)
            packed |= 0x80 | PaletteSizeBits(localPalette.Length / 3);
        w.Write((byte)packed);
        if (localPalette is not null)
            w.Write(localPalette);
        w.Write((byte)minCodeSize);
        WriteSubBlocks(w, compressed);
    }

    private static void WriteSubBlocks(BinaryWriter w, byte[] payload)
    {
        // Small chunks on purpose: every fixture exercises sub-block
        // concatenation rather than a single segment.
        for (int i = 0; i < payload.Length; i += 7)
        {
            int chunk = Math.Min(7, payload.Length - i);
            w.Write((byte)chunk);
            w.Write(payload, i, chunk);
        }
        w.Write((byte)0);
    }

    private static int PaletteSizeBits(int colors)
    {
        int bits = 0;
        while ((2 << bits) < colors)
            bits++;
        return bits;
    }

    private static byte[] ToInterlaceOrder(byte[] pixels, int width, int height)
    {
        var ordered = new byte[pixels.Length];
        int pass = 0;
        foreach ((int start, int step) in new (int, int)[] { (0, 8), (4, 8), (2, 4), (1, 2) })
            for (int r = start; r < height; r += step)
            {
                Buffer.BlockCopy(pixels, r * width, ordered, pass * width, width);
                pass++;
            }
        return ordered;
    }

    // Minimal GIF LZW encoder (clear + dictionary codes, width growth,
    // table-full reset): realistic streams for round-trip fixtures.
    // Hand-anchored by LzwEncoder_EmitsHandVerifiedBytes.
    private static byte[] LzwEncode(byte[] pixels, int minCodeSize)
    {
        int clear = 1 << minCodeSize;
        int eoi = clear + 1;
        var codes = new List<(int Code, int Width)>();
        int codeSize = minCodeSize + 1;
        var dict = new Dictionary<int, int>();
        int next = eoi + 1;
        codes.Add((clear, codeSize));
        int prefix = pixels[0];
        for (int i = 1; i < pixels.Length; i++)
        {
            int key = (prefix << 8) | pixels[i];
            if (dict.TryGetValue(key, out int code))
            {
                prefix = code;
            }
            else
            {
                codes.Add((prefix, codeSize));
                if (next < 4096)
                {
                    dict[key] = next++;
                    // Deferred one add: the decoder learns each entry a
                    // code later than the encoder adds it, so the width
                    // switch must lag one code to land at the same stream
                    // position on both sides.
                    if (next == (1 << codeSize) + 1 && codeSize < 12)
                        codeSize++;
                }
                else
                {
                    codes.Add((clear, codeSize));
                    dict.Clear();
                    codeSize = minCodeSize + 1;
                    next = eoi + 1;
                }
                prefix = pixels[i];
            }
        }
        codes.Add((prefix, codeSize));
        codes.Add((eoi, codeSize));

        var packed = new List<byte>();
        int buffer = 0;
        int bits = 0;
        foreach ((int code, int width) in codes)
        {
            buffer |= code << bits;
            bits += width;
            while (bits >= 8)
            {
                packed.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }
        if (bits > 0)
            packed.Add((byte)buffer);
        return [.. packed];
    }
}
