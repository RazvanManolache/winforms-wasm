extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfImage = WfDraw::System.Drawing.Image;

public sealed class JpegCodecTests
{
    // Natural-order index per zigzag position (mirror of the codec table;
    // the hand-packed fixture below anchors both to hand-computed bytes).
    private static readonly int[] Zz =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // Fully hand-packed 8x8 grayscale JPEG: flat-8 quant, single-symbol
    // DC table (cat 0 -> "0") and AC table (EOB -> "0"), one zero block.
    // Scan bits "00" padded with ones -> 0x3F; every pixel is 0 + 128.
    private static byte[] HandPackedGray8x8()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[] { 0xFF, 0xDB, 0x00, 0x43, 0x00 });
        for (int i = 0; i < 64; i++)
            bytes.Add(0x08);
        bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00 });
        bytes.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00 });
        bytes.AddRange(new byte[] { 0xFF, 0xC4, 0x00, 0x14, 0x10, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x00 });
        bytes.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00 });
        bytes.Add(0x3F);
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    [Fact]
    public void Encoder_EmitsHandVerifiedBytes()
    {
        // The test encoder must reproduce the hand-packed bytes exactly;
        // anchors every other fixture's headers, tables, and scan bits.
        var spec = new JSpec { Width = 8, Height = 8 };
        spec.Comps.Add(GrayComp(DcBlock(0)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        Assert.Equal(HandPackedGray8x8(), BuildJpeg(spec));
    }

    [Fact]
    public void HandPackedStream_DecodesWithoutEncoder()
    {
        using var bitmap = DecodeJpeg(HandPackedGray8x8());
        Assert.Equal(new WfDraw::System.Drawing.Size(8, 8), bitmap.Size);
        Assert.Equal(WfDraw::System.Drawing.Imaging.PixelFormat.Format32bppArgb, bitmap.PixelFormat);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(WfColor.FromArgb(128, 128, 128).ToArgb(), bitmap.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void Grayscale_UniformBlocks_DecodeExact()
    {
        // Diffs +8 then -16 prove the DC predictor accumulates across blocks.
        var spec = new JSpec { Width = 16, Height = 8 };
        spec.Comps.Add(GrayComp(DcBlock(8), DcBlock(-8)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(new WfDraw::System.Drawing.Size(16, 8), bitmap.Size);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 16; x++)
            {
                int gray = x < 8 ? 136 : 120;
                Assert.Equal(WfColor.FromArgb(gray, gray, gray).ToArgb(), bitmap.GetPixel(x, y).ToArgb());
            }
    }

    [Fact]
    public void Grayscale_NonMultipleOf8_CropsPadding()
    {
        // 10x10 rides a 2x2 MCU grid; edge padding is decoded then cropped.
        var spec = new JSpec { Width = 10, Height = 10 };
        spec.Comps.Add(GrayComp(DcBlock(0), DcBlock(8), DcBlock(16), DcBlock(24)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(new WfDraw::System.Drawing.Size(10, 10), bitmap.Size);
        Assert.Equal(WfColor.FromArgb(128, 128, 128).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(136, 136, 136).ToArgb(), bitmap.GetPixel(9, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(144, 144, 144).ToArgb(), bitmap.GetPixel(0, 9).ToArgb());
        Assert.Equal(WfColor.FromArgb(152, 152, 152).ToArgb(), bitmap.GetPixel(9, 9).ToArgb());
        Assert.Equal(WfColor.FromArgb(128, 128, 128).ToArgb(), bitmap.GetPixel(7, 7).ToArgb());
        Assert.Equal(WfColor.FromArgb(152, 152, 152).ToArgb(), bitmap.GetPixel(8, 8).ToArgb());
    }

    [Fact]
    public void Grayscale_16BitQuant_Decodes()
    {
        // Pq=1 DQT: DC 2 x Q300 = 600 -> 600/8 + 128 = 203.
        var spec = new JSpec { Width = 8, Height = 8 };
        spec.Comps.Add(GrayComp(DcBlock(2)));
        spec.Quants.Add(FlatQuant(0, 1, 300));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(203, 203, 203).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(203, 203, 203).ToArgb(), bitmap.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void Grayscale_AcCoefficients_Approximate()
    {
        // Single AC (natural 1 = 8, dequant 64): expected pixels come from
        // an independent direct-sum IDCT below, tolerance +/-1.
        int[] block = new int[64];
        block[1] = 8;
        var spec = new JSpec { Width = 8, Height = 8 };
        spec.Comps.Add(GrayComp(block));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        int[] dequant = new int[64];
        for (int i = 0; i < 64; i++)
            dequant[i] = block[i] * 8;
        int[,] expected = NaiveIdct(dequant);
        bool varied = false;
        double sum = 0;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int got = bitmap.GetPixel(x, y).R;
                Assert.InRange(got, expected[y, x] - 1, expected[y, x] + 1);
                Assert.Equal(got, bitmap.GetPixel(x, y).G);
                Assert.Equal(got, bitmap.GetPixel(x, y).B);
                varied |= got != 128;
                sum += got;
            }
        Assert.True(varied, "AC coefficient had no visible effect");
        Assert.InRange(sum / 64.0, 127.0, 129.0);
    }

    [Fact]
    public void Color444_BlackWhite_DecodesExact()
    {
        // 4:4:4 two-MCU row: Y -128 -> 0 (black), Y 127 -> 255 (white),
        // chroma 128 throughout. Y diff +255 also exercises a large
        // positive delta (category 8) across MCUs.
        var spec = new JSpec { Width = 16, Height = 8 };
        spec.Comps.Add(ColorComp(1, 1, 1, DcBlock(-128), DcBlock(127)));
        spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(0), DcBlock(0)));
        spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(0), DcBlock(0)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(7, 7).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(8, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(15, 7).ToArgb());
    }

    [Fact]
    public void Color444_Red_DecodesExact()
    {
        // Y=76, Cb=85, Cr=255 -> R=254.054->254, G=0.103->0, B=-0.196->0.
        // Pins the conversion constants (lossy rounding is ours, not GDI+).
        var spec = new JSpec { Width = 8, Height = 8 };
        spec.Comps.Add(ColorComp(1, 1, 1, DcBlock(-52)));
        spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(-43)));
        spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(127)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(WfColor.FromArgb(254, 0, 0).ToArgb(), bitmap.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void Color420_Quadrants_DecodeExact()
    {
        // 4:2:0 with distinct Y DCs per quadrant; chroma single blocks.
        // Also exercises multi-table DQT (Tq0/Tq1) and per-component DHT
        // selectors (luma tables 0, chroma tables 1).
        var spec = new JSpec { Width = 16, Height = 16 };
        spec.Comps.Add(ColorComp(1, 2, 2, DcBlock(-128), DcBlock(127), DcBlock(0), DcBlock(64)));
        spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(0), tq: 1, td: 1, ta: 1));
        spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(0), tq: 1, td: 1, ta: 1));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        spec.Quants.Add(FlatQuant(1, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(4, 4).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(12, 4).ToArgb());
        Assert.Equal(WfColor.FromArgb(128, 128, 128).ToArgb(), bitmap.GetPixel(4, 12).ToArgb());
        Assert.Equal(WfColor.FromArgb(192, 192, 192).ToArgb(), bitmap.GetPixel(12, 12).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(192, 192, 192).ToArgb(), bitmap.GetPixel(15, 15).ToArgb());
    }

    [Fact]
    public void Color422_Decodes()
    {
        var spec = new JSpec { Width = 16, Height = 8 };
        spec.Comps.Add(ColorComp(1, 2, 1, DcBlock(127), DcBlock(-128)));
        spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(0)));
        spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(0)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), bitmap.GetPixel(15, 7).ToArgb());
    }

    [Fact]
    public void RestartMarkers_DecodeSameAsWithout()
    {
        // Same quadrant image with DRI=2 and DRI=1: predictor resets and
        // RSTn markers must be invisible in the pixels.
        static byte[] BuildQuadrants(int restart)
        {
            var spec = new JSpec { Width = 16, Height = 16, RestartInterval = restart };
            spec.Comps.Add(ColorComp(1, 2, 2, DcBlock(-128), DcBlock(127), DcBlock(0), DcBlock(64)));
            spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(0)));
            spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(0)));
            spec.Quants.Add(FlatQuant(0, 0, 8));
            return BuildJpeg(spec);
        }
        using var plain = DecodeJpeg(BuildQuadrants(0));
        using var dri2 = DecodeJpeg(BuildQuadrants(2));
        using var dri1 = DecodeJpeg(BuildQuadrants(1));
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                Assert.Equal(plain.GetPixel(x, y).ToArgb(), dri2.GetPixel(x, y).ToArgb());
                Assert.Equal(plain.GetPixel(x, y).ToArgb(), dri1.GetPixel(x, y).ToArgb());
            }
        Assert.Equal(WfColor.FromArgb(0, 0, 0).ToArgb(), dri2.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 255, 255).ToArgb(), dri1.GetPixel(15, 0).ToArgb());
    }

    [Fact]
    public void AppSegments_Skipped_And_JfifDensity_SetsResolution()
    {
        var spec = new JSpec { Width = 8, Height = 8 };
        spec.Comps.Add(GrayComp(DcBlock(0)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        spec.App0Payload = JfifPayload(units: 1, xd: 300, yd: 300);
        spec.ExtraSegments.Add((0xFE, System.Text.Encoding.ASCII.GetBytes("comment")));
        spec.ExtraSegments.Add((0xE7, new byte[] { 1, 2, 3 }));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(128, 128, 128).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(300f, bitmap.HorizontalResolution);
        Assert.Equal(300f, bitmap.VerticalResolution);
    }

    [Fact]
    public void AdobeTransform0_TreatsComponentsAsRgb()
    {
        // Stored-RGB: components land on R/G/B directly (no conversion).
        var spec = new JSpec { Width = 8, Height = 8, AdobeTransform = 0 };
        spec.Comps.Add(ColorComp(1, 1, 1, DcBlock(127)));
        spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(-128)));
        spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(-128)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(255, 0, 0).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void AdobeTransform1_ConvertsYcc()
    {
        // Same bytes as above but YCbCr: Y=255,Cb=Cr=0 -> (76,255,28).
        var spec = new JSpec { Width = 8, Height = 8, AdobeTransform = 1 };
        spec.Comps.Add(ColorComp(1, 1, 1, DcBlock(127)));
        spec.Comps.Add(ColorComp(2, 1, 1, DcBlock(-128)));
        spec.Comps.Add(ColorComp(3, 1, 1, DcBlock(-128)));
        spec.Quants.Add(FlatQuant(0, 0, 8));
        using var bitmap = DecodeJpeg(BuildJpeg(spec));
        Assert.Equal(WfColor.FromArgb(76, 255, 28).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void UnsupportedVariants_ThrowNotSupported()
    {
        // SOF marker swaps on the hand-packed file (SOF0 at offset 71+1).
        byte[] hand = HandPackedGray8x8();
        Assert.Equal(0xC0, hand[72]);
        foreach (int sof in new[] { 0xC2, 0xC3, 0xC9 })
        {
            byte[] variant = (byte[])hand.Clone();
            variant[72] = (byte)sof;
            var ex = Assert.Throws<NotSupportedException>(() => DecodeJpeg(variant));
            Assert.Contains("milestone", ex.Message);
        }
        // 12-bit precision.
        byte[] p12 = (byte[])hand.Clone();
        p12[75] = 0x0C;
        Assert.Throws<NotSupportedException>(() => DecodeJpeg(p12));
        // Spectral selection (progressive SOS parameters on SOF0).
        var spectral = new JSpec { Width = 8, Height = 8, Se = 62 };
        spectral.Comps.Add(GrayComp(DcBlock(0)));
        spectral.Quants.Add(FlatQuant(0, 0, 8));
        Assert.Throws<NotSupportedException>(() => DecodeJpeg(BuildJpeg(spectral)));
        // SOS carrying fewer components than the frame (multi-scan).
        var subset = new JSpec { Width = 8, Height = 8, SosSubset = new List<int> { 0 } };
        subset.Comps.Add(ColorComp(1, 1, 1, DcBlock(0)));
        subset.Comps.Add(ColorComp(2, 1, 1, DcBlock(0)));
        subset.Comps.Add(ColorComp(3, 1, 1, DcBlock(0)));
        subset.Quants.Add(FlatQuant(0, 0, 8));
        var multi = Assert.Throws<NotSupportedException>(() => DecodeJpeg(BuildJpeg(subset)));
        Assert.Contains("Multi-scan", multi.Message);
        // Four components (CMYK).
        var cmyk = new JSpec { Width = 8, Height = 8 };
        cmyk.Comps.Add(ColorComp(1, 1, 1, DcBlock(0)));
        cmyk.Comps.Add(ColorComp(2, 1, 1, DcBlock(0)));
        cmyk.Comps.Add(ColorComp(3, 1, 1, DcBlock(0)));
        cmyk.Comps.Add(ColorComp(4, 1, 1, DcBlock(0)));
        cmyk.Quants.Add(FlatQuant(0, 0, 8));
        var cmykEx = Assert.Throws<NotSupportedException>(() => DecodeJpeg(BuildJpeg(cmyk)));
        Assert.Contains("CMYK", cmykEx.Message);
    }

    [Fact]
    public void Corrupt_ThrowsOutOfMemory()
    {
        byte[] hand = HandPackedGray8x8();
        // Truncated header / bad magic / truncated tail.
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(hand[..(hand.Length / 2)]));
        byte[] badMagic = (byte[])hand.Clone();
        badMagic[0] = 0x00;
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(badMagic));
        // SOS with an immediately following EOI (no scan data).
        int scanAt = hand.Length - 3; // single scan byte, then EOI.
        byte[] noScan = hand[..scanAt].Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(noScan));
        // Scan bits matching no Huffman code (only "0" is valid here).
        byte[] badBits = (byte[])hand.Clone();
        badBits[scanAt] = 0x80;
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(badBits));
        // Missing DQT segment (SOI + everything from SOF at offset 71).
        byte[] noDqt = hand[..2].Concat(hand[71..]).ToArray();
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(noDqt));
        // DHT length running past the file end (DHT-DC length at 86-87).
        byte[] badDht = (byte[])hand.Clone();
        badDht[86] = 0xFF;
        badDht[87] = 0xFF;
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(badDht));
        // Zero width in SOF (width bytes at 78-79).
        byte[] zeroW = (byte[])hand.Clone();
        zeroW[78] = 0;
        zeroW[79] = 0;
        Assert.Throws<OutOfMemoryException>(() => DecodeJpeg(zeroW));
    }

    [Fact]
    public void Image_FromStream_And_FromFile_DecodeJpeg()
    {
        byte[] jpeg = HandPackedGray8x8();
        using (var stream = new MemoryStream(jpeg))
        using (WfImage image = WfImage.FromStream(stream))
        {
            Assert.Equal(new WfDraw::System.Drawing.Size(8, 8), image.Size);
        }
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
        try
        {
            File.WriteAllBytes(path, jpeg);
            using WfImage image = WfImage.FromFile(path);
            var bitmap = Assert.IsType<WfBitmap>(image);
            Assert.Equal(WfColor.FromArgb(128, 128, 128).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static WfBitmap DecodeJpeg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new WfBitmap(stream);
    }

    // Independent direct-sum IDCT (doubles): cross-checks the codec's
    // separable path; input is dequantized natural-order coefficients.
    private static int[,] NaiveIdct(int[] coef)
    {
        var px = new int[8, 8];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int v = 0; v < 8; v++)
                    for (int u = 0; u < 8; u++)
                    {
                        double cu = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
                        double cv = v == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
                        s += cu * cv * coef[v * 8 + u]
                            * Math.Cos((2 * x + 1) * u * Math.PI / 16.0)
                            * Math.Cos((2 * y + 1) * v * Math.PI / 16.0);
                    }
                px[y, x] = Math.Clamp(
                    (int)Math.Round(s / 4.0 + 128.0, MidpointRounding.AwayFromZero), 0, 255);
            }
        return px;
    }

    private static int[] DcBlock(int dc)
    {
        int[] block = new int[64];
        block[0] = dc;
        return block;
    }

    private static JComp GrayComp(params int[][] blocks) =>
        new() { Id = 1, H = 1, V = 1, Blocks = blocks.ToList() };

    private static JComp ColorComp(int id, int h, int v, params int[][] blocks) =>
        new() { Id = id, H = h, V = v, Blocks = blocks.ToList() };

    private static JComp ColorComp(int id, int h, int v, int[] first, int tq, int td, int ta) =>
        new() { Id = id, H = h, V = v, Tq = tq, Td = td, Ta = ta, Blocks = new List<int[]> { first } };

    private static JQuant FlatQuant(int tq, int pq, int value)
    {
        int[] q = new int[64];
        Array.Fill(q, value);
        return new JQuant { Tq = tq, Pq = pq, Q = q };
    }

    private static byte[] JfifPayload(int units, int xd, int yd) =>
    [
        (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1,
        (byte)units, (byte)(xd >> 8), (byte)xd, (byte)(yd >> 8), (byte)yd, 0, 0,
    ];

    private sealed class JComp
    {
        public int Id = 1;
        public int H = 1;
        public int V = 1;
        public int Tq;
        public int Td;
        public int Ta;
        public List<int[]> Blocks = new();
    }

    private sealed class JQuant
    {
        public int Tq;
        public int Pq;
        public int[] Q = new int[64];
    }

    private sealed class JSpec
    {
        public int Width;
        public int Height;
        public List<JComp> Comps = new();
        public List<JQuant> Quants = new();
        public int RestartInterval;
        public int SofMarker = 0xC0;
        public int Precision = 8;
        public byte[]? App0Payload;
        public int? AdobeTransform;
        public List<(int Marker, byte[] Payload)> ExtraSegments = new();
        public List<int>? SosSubset;
        public int Ss;
        public int Se = 63;
        public int Ahl;
    }

    private sealed class BlockOps
    {
        public int Diff;
        public int Cat;
        public List<(int Rs, int Val, int Size)> Ac = new();
    }

    private sealed class Canon
    {
        public byte[] Counts = new byte[16];
        public List<int> Symbols = new();
        public Dictionary<int, (int Code, int Len)> Map = new();

        public static Canon Build(IEnumerable<int> needed)
        {
            // Complete prefix code over the sorted symbols: lengths
            // 1..n-1 plus a doubled last length (Kraft sum = 1); wide
            // alphabets fall back to flat length-16 codes.
            List<int> syms = needed.Distinct().OrderBy(s => s).ToList();
            int n = syms.Count;
            int[] lens = new int[n];
            if (n == 1)
                lens[0] = 1;
            else if (n > 17)
                Array.Fill(lens, 16);
            else
            {
                for (int i = 0; i < n - 1; i++)
                    lens[i] = i + 1;
                lens[n - 1] = n - 1;
            }
            var items = syms.Select((s, i) => (Sym: s, Len: lens[i]))
                .OrderBy(t => t.Len).ThenBy(t => t.Sym).ToList();
            var table = new Canon();
            int code = 0;
            int prev = items[0].Len;
            foreach ((int sym, int len) in items)
            {
                code <<= len - prev;
                table.Map[sym] = (code, len);
                table.Counts[len - 1]++;
                table.Symbols.Add(sym);
                code++;
                prev = len;
            }
            return table;
        }
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _out = new();
        private int _acc;
        private int _nbits;

        public void Put(int code, int len)
        {
            for (int i = len - 1; i >= 0; i--)
                PutBit((code >> i) & 1);
        }

        public void PutExtra(int value, int size)
        {
            Put(value >= 0 ? value : value + (1 << size) - 1, size);
        }

        public void PutBit(int b)
        {
            _acc = (_acc << 1) | b;
            if (++_nbits == 8)
            {
                _out.Add((byte)_acc);
                if (_acc == 0xFF)
                    _out.Add(0x00);
                _acc = 0;
                _nbits = 0;
            }
        }

        public void Align()
        {
            while (_nbits > 0)
                PutBit(1);
        }

        public void Marker(int m)
        {
            Align();
            _out.Add(0xFF);
            _out.Add((byte)m);
        }

        public byte[] ToScan()
        {
            Align();
            return _out.ToArray();
        }
    }

    // Minimal baseline JPEG writer: quantized natural-order blocks in,
    // one single-scan file out. Hand-anchored by
    // Encoder_EmitsHandVerifiedBytes.
    private static byte[] BuildJpeg(JSpec spec)
    {
        int hmax = spec.Comps.Max(c => c.H);
        int vmax = spec.Comps.Max(c => c.V);
        int mcuW = (spec.Width + hmax * 8 - 1) / (hmax * 8);
        int mcuH = (spec.Height + vmax * 8 - 1) / (vmax * 8);
        for (int i = 0; i < spec.Comps.Count; i++)
        {
            int want = mcuW * spec.Comps[i].H * mcuH * spec.Comps[i].V;
            if (spec.Comps[i].Blocks.Count != want)
                throw new InvalidOperationException($"Comp {i}: {spec.Comps[i].Blocks.Count} blocks, need {want}.");
        }
        List<int> sos = spec.SosSubset ?? spec.Comps.Select((_, i) => i).ToList();

        // Walk MCUs in decode order, recording diffs + AC ops with
        // restart resets, and collecting each table's used symbols.
        var dcNeeded = new Dictionary<int, HashSet<int>>();
        var acNeeded = new Dictionary<int, HashSet<int>>();
        var ops = new List<(int Ci, BlockOps Op)>();
        int[] pred = new int[spec.Comps.Count];
        int mcuTotal = mcuW * mcuH;
        int mcuDone = 0;
        for (int my = 0; my < mcuH; my++)
            for (int mx = 0; mx < mcuW; mx++)
            {
                foreach (int ci in sos)
                {
                    JComp comp = spec.Comps[ci];
                    int row = mcuW * comp.H;
                    for (int vy = 0; vy < comp.V; vy++)
                        for (int vx = 0; vx < comp.H; vx++)
                        {
                            int[] block = comp.Blocks[(my * comp.V + vy) * row + mx * comp.H + vx];
                            var op = new BlockOps { Diff = block[0] - pred[ci] };
                            op.Cat = Category(op.Diff);
                            pred[ci] = block[0];
                            op.Ac = EncodeAc(block);
                            ops.Add((ci, op));
                            dcNeeded.GetOrAdd(comp.Td, _ => new HashSet<int>()).Add(op.Cat);
                            HashSet<int> ac = acNeeded.GetOrAdd(comp.Ta, _ => new HashSet<int>());
                            foreach ((int rs, _, _) in op.Ac)
                                ac.Add(rs);
                        }
                }
                mcuDone++;
                if (spec.RestartInterval > 0 && mcuDone % spec.RestartInterval == 0 && mcuDone < mcuTotal)
                    Array.Clear(pred);
            }
        var dcTables = dcNeeded.ToDictionary(kv => kv.Key, kv => Canon.Build(kv.Value));
        var acTables = acNeeded.ToDictionary(kv => kv.Key, kv => Canon.Build(kv.Value));

        var file = new List<byte> { 0xFF, 0xD8 };
        if (spec.App0Payload is not null)
            WriteSegment(file, 0xE0, spec.App0Payload);
        if (spec.AdobeTransform is int transform)
            WriteSegment(file, 0xEE,
            [
                (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0,
                1, 0, 0, 0, 0, 0, (byte)transform,
            ]);
        foreach ((int marker, byte[] payload) in spec.ExtraSegments)
            WriteSegment(file, marker, payload);
        var dqt = new List<byte>();
        foreach (JQuant quant in spec.Quants)
        {
            dqt.Add((byte)((quant.Pq << 4) | quant.Tq));
            for (int i = 0; i < 64; i++)
            {
                int v = quant.Q[Zz[i]];
                if (quant.Pq == 0)
                    dqt.Add((byte)v);
                else
                {
                    dqt.Add((byte)(v >> 8));
                    dqt.Add((byte)v);
                }
            }
        }
        WriteSegment(file, 0xDB, dqt.ToArray());
        var sof = new List<byte>
        {
            (byte)spec.Precision,
            (byte)(spec.Height >> 8), (byte)spec.Height,
            (byte)(spec.Width >> 8), (byte)spec.Width,
            (byte)spec.Comps.Count,
        };
        foreach (JComp comp in spec.Comps)
        {
            sof.Add((byte)comp.Id);
            sof.Add((byte)((comp.H << 4) | comp.V));
            sof.Add((byte)comp.Tq);
        }
        WriteSegment(file, spec.SofMarker, sof.ToArray());
        foreach (int t in dcTables.Keys.OrderBy(t => t))
            WriteDht(file, 0, t, dcTables[t]);
        foreach (int t in acTables.Keys.OrderBy(t => t))
            WriteDht(file, 1, t, acTables[t]);
        if (spec.RestartInterval > 0)
            WriteSegment(file, 0xDD, [(byte)(spec.RestartInterval >> 8), (byte)spec.RestartInterval]);
        var sosBytes = new List<byte> { (byte)sos.Count };
        foreach (int ci in sos)
        {
            JComp comp = spec.Comps[ci];
            sosBytes.Add((byte)comp.Id);
            sosBytes.Add((byte)((comp.Td << 4) | comp.Ta));
        }
        sosBytes.Add((byte)spec.Ss);
        sosBytes.Add((byte)spec.Se);
        sosBytes.Add((byte)spec.Ahl);
        WriteSegment(file, 0xDA, sosBytes.ToArray());

        var writer = new BitWriter();
        int blocksPerMcu = sos.Sum(ci => spec.Comps[ci].H * spec.Comps[ci].V);
        int opIndex = 0;
        int mcuIndex = 0;
        int rst = 0;
        foreach ((int ci, BlockOps op) in ops)
        {
            JComp comp = spec.Comps[ci];
            (int dcCode, int dcLen) = dcTables[comp.Td].Map[op.Cat];
            writer.Put(dcCode, dcLen);
            if (op.Cat > 0)
                writer.PutExtra(op.Diff, op.Cat);
            foreach ((int rs, int val, int size) in op.Ac)
            {
                (int acCode, int acLen) = acTables[comp.Ta].Map[rs];
                writer.Put(acCode, acLen);
                if (size > 0)
                    writer.PutExtra(val, size);
            }
            if (++opIndex % blocksPerMcu == 0)
            {
                mcuIndex++;
                if (spec.RestartInterval > 0 && mcuIndex % spec.RestartInterval == 0 && mcuIndex < mcuTotal)
                    writer.Marker(0xD0 + (rst++ % 8));
            }
        }
        file.AddRange(writer.ToScan());
        file.Add(0xFF);
        file.Add(0xD9);
        return file.ToArray();
    }

    private static List<(int Rs, int Val, int Size)> EncodeAc(int[] block)
    {
        int last = 0;
        for (int k = 1; k < 64; k++)
            if (block[Zz[k]] != 0)
                last = k;
        var acs = new List<(int, int, int)>();
        if (last == 0)
        {
            acs.Add((0x00, 0, 0));
            return acs;
        }
        int run = 0;
        for (int k = 1; k <= last; k++)
        {
            int v = block[Zz[k]];
            if (v == 0)
            {
                run++;
                continue;
            }
            while (run > 15)
            {
                acs.Add((0xF0, 0, 0));
                run -= 16;
            }
            acs.Add(((run << 4) | Category(v), v, Category(v)));
            run = 0;
        }
        if (last < 63)
            acs.Add((0x00, 0, 0));
        return acs;
    }

    private static int Category(int v)
    {
        int a = Math.Abs(v);
        int s = 0;
        while (a > 0)
        {
            s++;
            a >>= 1;
        }
        return s;
    }

    private static void WriteSegment(List<byte> file, int marker, byte[] payload)
    {
        file.Add(0xFF);
        file.Add((byte)marker);
        file.Add((byte)((payload.Length + 2) >> 8));
        file.Add((byte)(payload.Length + 2));
        file.AddRange(payload);
    }

    private static void WriteDht(List<byte> file, int tc, int th, Canon table)
    {
        var payload = new List<byte> { (byte)((tc << 4) | th) };
        payload.AddRange(table.Counts);
        foreach (int s in table.Symbols)
            payload.Add((byte)s);
        WriteSegment(file, 0xC4, payload.ToArray());
    }
}

internal static class JpegTestDictionaryExtensions
{
    internal static TValue GetOrAdd<TKey, TValue>(
        this Dictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> create)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out TValue? value))
        {
            value = create(key);
            dict[key] = value;
        }
        return value;
    }
}
