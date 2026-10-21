// JPEG decode: minimal baseline reader for Image.FromStream/FromFile.
// Supports sequential DCT JPEG (SOF0/SOF1, 8-bit): grayscale and
// 3-component YCbCr (or Adobe APP14 RGB), 8/16-bit quant tables,
// sampling factors 1-4 (4:4:4, 4:2:2, 4:2:0, ...), restart intervals,
// JFIF density (DPI), single-scan interleaved or not. Progressive
// (SOF2), lossless (SOF3), differential/arithmetic (SOF5-7/9-11/13-15),
// multi-scan files, and component counts other than 1/3 throw
// NotSupportedException; corrupt data throws OutOfMemoryException
// (the GDI+ quirk, matching PngCodec/BmpCodec/GifCodec). Mapped to
// 32bpp ARGB like every other decoded image. Bytes after a complete
// scan are ignored (missing/trailing EOI tolerated, like browsers);
// chroma upsampling is box replication. IDCT is separable float with
// an exact integer path for DC-only blocks; lossy pixels pin this
// implementation's rounding, not GDI+ bit parity (libjpeg float/int
// builds differ by +/-1 too).

namespace System.Drawing;

internal static class JpegCodec
{
    // Natural-order index per zigzag position: DQT byte i and scan
    // coefficient k both address ZigZag[k] in the 8x8 block.
    private static readonly byte[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    private static readonly double[] CosT = BuildCosT();
    private static readonly double[] C8 = [1.0 / Math.Sqrt(2.0), 1, 1, 1, 1, 1, 1, 1];

    private static double[] BuildCosT()
    {
        double[] t = new double[64];
        for (int a = 0; a < 8; a++)
            for (int b = 0; b < 8; b++)
                t[a * 8 + b] = Math.Cos((2 * a + 1) * b * Math.PI / 16.0);
        return t;
    }

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

    private sealed class Frame
    {
        internal int Width;
        internal int Height;
        internal int Nf;
        internal readonly int[] Id = new int[4];
        internal readonly int[] H = new int[4];
        internal readonly int[] V = new int[4];
        internal readonly int[] Tq = new int[4];
        internal int Hmax = 1;
        internal int Vmax = 1;
    }

    private sealed class ScanSpec
    {
        internal int Ns;
        internal readonly int[] Comp = new int[4];
        internal readonly int[] Td = new int[4];
        internal readonly int[] Ta = new int[4];
    }

    private static Bitmap DecodeCore(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF)
            throw new OutOfMemoryException("Invalid image data.");
        int pos = 2;
        int[][] quants = new int[4][];
        HuffmanTable?[,] huff = new HuffmanTable?[2, 4];
        Frame? frame = null;
        int restartInterval = 0;
        float dpiX = 96f;
        float dpiY = 96f;
        bool adobeRgb = false;

        while (true)
        {
            int marker = ReadMarker(bytes, ref pos);
            if (marker == 0xD9)
                throw new OutOfMemoryException("Invalid image data."); // EOI before any scan.
            if (marker is 0x01 or >= 0xD0 and <= 0xD8)
                continue; // TEM / stray RST / repeated SOI: standalone, no payload.
            int segStart = pos + 2;
            int segEnd = segStart + ReadLength(bytes, ref pos) - 2;
            if (segEnd > bytes.Length)
                throw new OutOfMemoryException("Invalid image data.");
            switch (marker)
            {
                case 0xE0:
                    ParseApp0(bytes, segStart, segEnd, ref dpiX, ref dpiY);
                    break;
                case 0xEE:
                    adobeRgb = ParseApp14(bytes, segStart, segEnd, adobeRgb);
                    break;
                case 0xDB:
                    ParseDqt(bytes, segStart, segEnd, quants);
                    break;
                case 0xC0:
                case 0xC1:
                    frame = ParseSof(bytes, segStart, segEnd);
                    break;
                case 0xC2:
                    throw new NotSupportedException("Progressive JPEG decode arrives with the codec milestone.");
                case 0xC3:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xC9:
                case 0xCA:
                case 0xCB:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    throw new NotSupportedException("Lossless/arithmetic JPEG decode arrives with the codec milestone.");
                case 0xC4:
                    ParseDht(bytes, segStart, segEnd, huff);
                    break;
                case 0xDD:
                    restartInterval = ParseDri(bytes, segStart, segEnd);
                    break;
                case 0xDA:
                    if (frame is null)
                        throw new OutOfMemoryException("Invalid image data.");
                    ScanSpec scan = ParseSos(bytes, segStart, segEnd, frame, quants, huff);
                    Bitmap bitmap = DecodeScan(bytes, segEnd, frame, quants, huff, scan, restartInterval, adobeRgb);
                    bitmap.SetResolution(dpiX, dpiY);
                    return bitmap;
                default:
                    break; // APPn / COM / DNL / DAC / JPG: length-skipped.
            }
            pos = segEnd;
        }
    }

    private static int ReadMarker(byte[] bytes, ref int pos)
    {
        if (pos >= bytes.Length || bytes[pos++] != 0xFF)
            throw new OutOfMemoryException("Invalid image data.");
        int b;
        do
        {
            if (pos >= bytes.Length)
                throw new OutOfMemoryException("Invalid image data.");
            b = bytes[pos++];
        }
        while (b == 0xFF);
        if (b == 0x00)
            throw new OutOfMemoryException("Invalid image data.");
        return b;
    }

    private static int ReadLength(byte[] bytes, ref int pos)
    {
        if (pos + 2 > bytes.Length)
            throw new OutOfMemoryException("Invalid image data.");
        int length = (bytes[pos] << 8) | bytes[pos + 1];
        pos += 2;
        if (length < 2)
            throw new OutOfMemoryException("Invalid image data.");
        return length;
    }

    private static void ParseApp0(byte[] bytes, int start, int end, ref float dpiX, ref float dpiY)
    {
        // JFIF header: "JFIF\0" + version(2) + units(1) + Xdensity(2) +
        // Ydensity(2) + thumbnail dims(2). Units 0 is aspect-only (keeps
        // 96); thumbnail pixels ride the same segment and are ignored.
        if (end - start < 14
            || bytes[start] != (byte)'J' || bytes[start + 1] != (byte)'F'
            || bytes[start + 2] != (byte)'I' || bytes[start + 3] != (byte)'F'
            || bytes[start + 4] != 0)
            return;
        int units = bytes[start + 7];
        int xd = (bytes[start + 8] << 8) | bytes[start + 9];
        int yd = (bytes[start + 10] << 8) | bytes[start + 11];
        if (xd <= 0 || yd <= 0)
            return;
        if (units == 1)
        {
            dpiX = xd;
            dpiY = yd;
        }
        else if (units == 2)
        {
            dpiX = xd * 2.54f;
            dpiY = yd * 2.54f;
        }
    }

    private static bool ParseApp14(byte[] bytes, int start, int end, bool current)
    {
        // Adobe header: "Adobe\0" + version(2) + flags(4) + transform(1).
        // Transform 0 means stored-RGB (no YCbCr conversion); 1 means
        // YCbCr (the default); YCCK rides 4 components (unsupported).
        if (end - start < 13
            || bytes[start] != (byte)'A' || bytes[start + 1] != (byte)'d'
            || bytes[start + 2] != (byte)'o' || bytes[start + 3] != (byte)'b'
            || bytes[start + 4] != (byte)'e' || bytes[start + 5] != 0)
            return current;
        return bytes[start + 12] == 0;
    }

    private static void ParseDqt(byte[] bytes, int start, int end, int[][] quants)
    {
        int p = start;
        while (p < end)
        {
            int info = bytes[p++];
            int pq = info >> 4;
            int tq = info & 15;
            if (pq > 1 || tq > 3)
                throw new OutOfMemoryException("Invalid image data.");
            int size = pq == 0 ? 1 : 2;
            if ((long)p + 64L * size > end)
                throw new OutOfMemoryException("Invalid image data.");
            // Table bytes run in zigzag order; stored in natural order
            // so dequantization indexes by block position directly.
            int[] q = new int[64];
            for (int i = 0; i < 64; i++)
            {
                int v = bytes[p++];
                if (size == 2)
                    v = (v << 8) | bytes[p++];
                q[ZigZag[i]] = v;
            }
            quants[tq] = q;
        }
    }

    private static Frame ParseSof(byte[] bytes, int start, int end)
    {
        if (end - start < 6)
            throw new OutOfMemoryException("Invalid image data.");
        if (bytes[start] != 8)
            throw new NotSupportedException("12-bit JPEG decode arrives with the codec milestone.");
        int height = (bytes[start + 1] << 8) | bytes[start + 2];
        int width = (bytes[start + 3] << 8) | bytes[start + 4];
        int nf = bytes[start + 5];
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new OutOfMemoryException("Invalid image data.");
        if (nf == 4)
            throw new NotSupportedException("CMYK JPEG decode arrives with the codec milestone.");
        if (nf != 1 && nf != 3)
            throw new NotSupportedException("JPEG decode arrives with the codec milestone.");
        if (end - start < 6 + 3 * nf)
            throw new OutOfMemoryException("Invalid image data.");
        var frame = new Frame { Width = width, Height = height, Nf = nf };
        int blocksPerMcu = 0;
        for (int i = 0; i < nf; i++)
        {
            int id = bytes[start + 6 + i * 3];
            int hv = bytes[start + 7 + i * 3];
            int tq = bytes[start + 8 + i * 3];
            int h = hv >> 4;
            int v = hv & 15;
            if (h is < 1 or > 4 || v is < 1 or > 4 || tq > 3)
                throw new OutOfMemoryException("Invalid image data.");
            for (int j = 0; j < i; j++)
                if (frame.Id[j] == id)
                    throw new OutOfMemoryException("Invalid image data.");
            frame.Id[i] = id;
            frame.H[i] = h;
            frame.V[i] = v;
            frame.Tq[i] = tq;
            frame.Hmax = Math.Max(frame.Hmax, h);
            frame.Vmax = Math.Max(frame.Vmax, v);
            blocksPerMcu += h * v;
        }
        if (blocksPerMcu > 10)
            throw new OutOfMemoryException("Invalid image data.");
        return frame;
    }

    private static void ParseDht(byte[] bytes, int start, int end, HuffmanTable?[,] huff)
    {
        int p = start;
        while (p < end)
        {
            if (p + 17 > end)
                throw new OutOfMemoryException("Invalid image data.");
            int info = bytes[p++];
            int tc = info >> 4;
            int th = info & 15;
            if (tc > 1 || th > 3)
                throw new OutOfMemoryException("Invalid image data.");
            byte[] counts = new byte[16];
            int total = 0;
            for (int i = 0; i < 16; i++)
            {
                counts[i] = bytes[p++];
                total += counts[i];
            }
            if (total > 256 || p + total > end)
                throw new OutOfMemoryException("Invalid image data.");
            byte[] symbols = new byte[total];
            Buffer.BlockCopy(bytes, p, symbols, 0, total);
            p += total;
            huff[tc, th] = HuffmanTable.Build(counts, symbols);
        }
    }

    private static int ParseDri(byte[] bytes, int start, int end)
    {
        if (end - start != 2)
            throw new OutOfMemoryException("Invalid image data.");
        return (bytes[start] << 8) | bytes[start + 1];
    }

    private static ScanSpec ParseSos(
        byte[] bytes, int start, int end, Frame frame,
        int[][] quants, HuffmanTable?[,] huff)
    {
        if (end - start < 4)
            throw new OutOfMemoryException("Invalid image data.");
        int ns = bytes[start];
        if (ns is < 1 or > 4 || end - start < 4 + 2 * ns)
            throw new OutOfMemoryException("Invalid image data.");
        if (ns != frame.Nf)
            throw new NotSupportedException("Multi-scan JPEG decode arrives with the codec milestone.");
        var scan = new ScanSpec { Ns = ns };
        for (int j = 0; j < ns; j++)
        {
            int id = bytes[start + 1 + j * 2];
            int tdta = bytes[start + 2 + j * 2];
            int td = tdta >> 4;
            int ta = tdta & 15;
            int ci = -1;
            for (int i = 0; i < frame.Nf; i++)
                if (frame.Id[i] == id)
                    ci = i;
            if (ci < 0 || td > 3 || ta > 3)
                throw new OutOfMemoryException("Invalid image data.");
            for (int k = 0; k < j; k++)
                if (scan.Comp[k] == ci)
                    throw new OutOfMemoryException("Invalid image data.");
            if (huff[0, td] is null || huff[1, ta] is null || quants[frame.Tq[ci]] is null)
                throw new OutOfMemoryException("Invalid image data.");
            scan.Comp[j] = ci;
            scan.Td[j] = td;
            scan.Ta[j] = ta;
        }
        int ss = bytes[start + 1 + ns * 2];
        int se = bytes[start + 2 + ns * 2];
        int ahl = bytes[start + 3 + ns * 2];
        if (ss != 0 || se != 63 || ahl != 0)
            throw new NotSupportedException("Progressive JPEG decode arrives with the codec milestone.");
        return scan;
    }

    /// <summary>Canonical Huffman table with libjpeg-style min/max decode.</summary>
    private sealed class HuffmanTable
    {
        private readonly int[] _minCode = new int[17];
        private readonly int[] _maxCode = new int[17];
        private readonly int[] _start = new int[17];
        private readonly byte[] _symbols;

        private HuffmanTable(byte[] symbols)
        {
            _symbols = symbols;
        }

        internal static HuffmanTable Build(byte[] counts, byte[] symbols)
        {
            var table = new HuffmanTable(symbols);
            int code = 0;
            int k = 0;
            for (int len = 1; len <= 16; len++)
            {
                int count = counts[len - 1];
                table._start[len] = k;
                table._minCode[len] = code;
                code += count;
                if (code > (1 << len))
                    throw new OutOfMemoryException("Invalid image data.");
                k += count;
                table._maxCode[len] = code - 1;
                code <<= 1;
            }
            if (k != symbols.Length)
                throw new OutOfMemoryException("Invalid image data.");
            return table;
        }

        internal int Decode(BitReader reader)
        {
            // Empty tables are definable but never selectable: an SOS
            // naming one fails here (unused empty tables stay inert).
            if (_symbols.Length == 0)
                throw new OutOfMemoryException("Invalid image data.");
            int acc = 0;
            for (int len = 1; len <= 16; len++)
            {
                acc = (acc << 1) | reader.ReadBit();
                if (acc >= _minCode[len] && acc <= _maxCode[len])
                    return _symbols[_start[len] + acc - _minCode[len]];
            }
            throw new OutOfMemoryException("Invalid image data.");
        }
    }

    /// <summary>MSB-first scan reader with FF00 unstuffing.</summary>
    private sealed class BitReader
    {
        private readonly byte[] _bytes;
        private int _pos;
        private int _buffer;
        private int _bitsLeft;

        internal BitReader(byte[] bytes, int pos)
        {
            _bytes = bytes;
            _pos = pos;
        }

        internal int ReadBit()
        {
            if (_bitsLeft == 0)
            {
                Fill();
                if (_bitsLeft == 0)
                    throw new OutOfMemoryException("Invalid image data.");
            }
            _bitsLeft--;
            return (_buffer >> _bitsLeft) & 1;
        }

        internal int ReadBits(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++)
                v = (v << 1) | ReadBit();
            return v;
        }

        private void Fill()
        {
            // Demand-driven single bytes (never read-ahead): Fill runs
            // only on an empty buffer, so a marker hit always means the
            // decoder asked past the scan end (truncated/corrupt).
            if (_pos >= _bytes.Length)
                return;
            int b = _bytes[_pos++];
            if (b != 0xFF)
            {
                _buffer = b;
                _bitsLeft = 8;
                return;
            }
            if (_pos < _bytes.Length && _bytes[_pos] == 0x00)
            {
                _pos++;
                _buffer = 0xFF;
                _bitsLeft = 8;
            }
            // Else a marker/EOF: no bits, the ReadBit caller throws.
        }

        internal void AlignToByte()
        {
            _bitsLeft = 0;
            _buffer = 0;
        }

        /// <summary>Reads the byte-aligned marker after pad bits.</summary>
        internal int ReadMarker()
        {
            AlignToByte();
            if (_pos >= _bytes.Length)
                return -1;
            if (_bytes[_pos++] != 0xFF)
                throw new OutOfMemoryException("Invalid image data.");
            int c;
            do
            {
                if (_pos >= _bytes.Length)
                    return -1;
                c = _bytes[_pos++];
            }
            while (c == 0xFF);
            if (c == 0x00)
                throw new OutOfMemoryException("Invalid image data.");
            return c;
        }
    }

    private static Bitmap DecodeScan(
        byte[] bytes, int scanStart, Frame frame, int[][] quants,
        HuffmanTable?[,] huff, ScanSpec scan, int restartInterval, bool adobeRgb)
    {
        int mcuW = (frame.Width + frame.Hmax * 8 - 1) / (frame.Hmax * 8);
        int mcuH = (frame.Height + frame.Vmax * 8 - 1) / (frame.Vmax * 8);
        byte[][] planes = new byte[frame.Nf][];
        int[] strides = new int[frame.Nf];
        for (int i = 0; i < frame.Nf; i++)
            strides[i] = mcuW * frame.H[i] * 8;
        for (int i = 0; i < frame.Nf; i++)
            planes[i] = new byte[(long)strides[i] * mcuH * frame.V[i] * 8];

        var reader = new BitReader(bytes, scanStart);
        int[] dc = new int[4];
        int[] coef = new int[64];
        int totalMcu = mcuW * mcuH;
        int done = 0;
        for (int my = 0; my < mcuH; my++)
        {
            for (int mx = 0; mx < mcuW; mx++)
            {
                for (int j = 0; j < scan.Ns; j++)
                {
                    int ci = scan.Comp[j];
                    HuffmanTable dcTable = huff[0, scan.Td[j]]!;
                    HuffmanTable acTable = huff[1, scan.Ta[j]]!;
                    int[] q = quants[frame.Tq[ci]]!;
                    for (int vy = 0; vy < frame.V[ci]; vy++)
                        for (int vx = 0; vx < frame.H[ci]; vx++)
                            DecodeBlock(reader, dcTable, acTable, q, dc, ci, coef,
                                planes[ci], strides[ci],
                                (mx * frame.H[ci] + vx) * 8, (my * frame.V[ci] + vy) * 8);
                }
                done++;
                if (restartInterval > 0 && done % restartInterval == 0 && done < totalMcu)
                {
                    int marker = reader.ReadMarker();
                    if (marker is < 0xD0 or > 0xD7)
                        throw new OutOfMemoryException("Invalid image data.");
                    Array.Clear(dc);
                }
            }
        }
        return Assemble(frame, planes, strides, adobeRgb);
    }

    private static void DecodeBlock(
        BitReader reader, HuffmanTable dcTable, HuffmanTable acTable,
        int[] q, int[] dc, int ci, int[] coef,
        byte[] plane, int stride, int bx, int by)
    {
        Array.Clear(coef);
        int s = dcTable.Decode(reader);
        if (s is < 0 or > 11)
            throw new OutOfMemoryException("Invalid image data.");
        if (s != 0)
        {
            dc[ci] += Extend(reader.ReadBits(s), s);
            // 8-bit quantized DCs stay near +/-1024; beyond this the
            // stream is corrupt (and the dequant multiply must not wrap).
            if (dc[ci] is < -2048 or > 2048)
                throw new OutOfMemoryException("Invalid image data.");
        }
        coef[0] = dc[ci] * q[0];
        bool acNonZero = false;
        int k = 1;
        while (k < 64)
        {
            int rs = acTable.Decode(reader);
            int run = rs >> 4;
            int size = rs & 15;
            if (size == 0)
            {
                if (run == 0)
                    break; // EOB.
                if (run == 15)
                {
                    k += 16; // ZRL: 16 zeros.
                    continue;
                }
                throw new OutOfMemoryException("Invalid image data.");
            }
            if (size > 10)
                throw new OutOfMemoryException("Invalid image data.");
            k += run;
            if (k >= 64)
                throw new OutOfMemoryException("Invalid image data.");
            coef[ZigZag[k]] = Extend(reader.ReadBits(size), size) * q[ZigZag[k]];
            acNonZero = true;
            k++;
        }
        if (k > 64)
            throw new OutOfMemoryException("Invalid image data.");
        WriteBlock(coef, acNonZero, plane, stride, bx, by);
    }

    private static int Extend(int v, int s)
    {
        if (s == 0)
            return 0;
        return v < (1 << (s - 1)) ? v - ((1 << s) - 1) : v;
    }

    private static void WriteBlock(
        int[] coef, bool acNonZero, byte[] plane, int stride, int bx, int by)
    {
        if (!acNonZero)
        {
            // DC-only: every sample is DC/8 + 128 exactly (integer path,
            // matching MidpointRounding.AwayFromZero at .5 both signs).
            int v = coef[0];
            int p = v / 8 + 128;
            int rem = v % 8;
            if (rem >= 4)
                p++;
            else if (rem <= -4)
                p--;
            byte b = (byte)Math.Clamp(p, 0, 255);
            for (int y = 0; y < 8; y++)
            {
                int o = (by + y) * stride + bx;
                plane[o] = b;
                plane[o + 1] = b;
                plane[o + 2] = b;
                plane[o + 3] = b;
                plane[o + 4] = b;
                plane[o + 5] = b;
                plane[o + 6] = b;
                plane[o + 7] = b;
            }
            return;
        }
        // Separable 8x8 IDCT: rows then columns (1024 mults, versus
        // 4096 for the direct double sum; AAN is future work).
        Span<double> tmp = stackalloc double[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                double t = 0;
                for (int u = 0; u < 8; u++)
                    t += C8[u] * coef[y * 8 + u] * CosT[x * 8 + u];
                tmp[y * 8 + x] = t;
            }
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                double t = 0;
                for (int v = 0; v < 8; v++)
                    t += C8[v] * tmp[v * 8 + x] * CosT[y * 8 + v];
                int p = (int)Math.Round(t / 4.0 + 128.0, MidpointRounding.AwayFromZero);
                plane[(by + y) * stride + bx + x] = (byte)Math.Clamp(p, 0, 255);
            }
    }

    private static Bitmap Assemble(Frame frame, byte[][] planes, int[] strides, bool adobeRgb)
    {
        var bitmap = new Bitmap(frame.Width, frame.Height);
        int[] dest = bitmap.Pixels;
        if (frame.Nf == 1)
        {
            byte[] g = planes[0];
            int gs = strides[0];
            int h = frame.H[0];
            int v = frame.V[0];
            for (int y = 0; y < frame.Height; y++)
            {
                int row = (y * v / frame.Vmax) * gs;
                for (int x = 0; x < frame.Width; x++)
                {
                    int gray = g[row + x * h / frame.Hmax];
                    dest[y * frame.Width + x] = unchecked((int)0xFF000000) | (gray << 16) | (gray << 8) | gray;
                }
            }
            return bitmap;
        }
        byte[] c0 = planes[0];
        byte[] c1 = planes[1];
        byte[] c2 = planes[2];
        for (int y = 0; y < frame.Height; y++)
        {
            int y0 = (y * frame.V[0] / frame.Vmax) * strides[0];
            int y1 = (y * frame.V[1] / frame.Vmax) * strides[1];
            int y2 = (y * frame.V[2] / frame.Vmax) * strides[2];
            for (int x = 0; x < frame.Width; x++)
            {
                int a = c0[y0 + x * frame.H[0] / frame.Hmax];
                int b = c1[y1 + x * frame.H[1] / frame.Hmax];
                int c = c2[y2 + x * frame.H[2] / frame.Hmax];
                int r, g, bl;
                if (adobeRgb)
                {
                    r = a;
                    g = b;
                    bl = c;
                }
                else
                {
                    YCbCrToRgb(a, b, c, out r, out g, out bl);
                }
                dest[y * frame.Width + x] = Color.FromArgb(255, r, g, bl).ToArgb();
            }
        }
        return bitmap;
    }

    private static void YCbCrToRgb(int y, int cb, int cr, out int r, out int g, out int b)
    {
        double dcb = cb - 128;
        double dcr = cr - 128;
        r = ClampRound(y + 1.40200 * dcr);
        g = ClampRound(y - 0.344136 * dcb - 0.714136 * dcr);
        b = ClampRound(y + 1.77200 * dcb);
    }

    private static int ClampRound(double v) =>
        v <= 0 ? 0 : v >= 255 ? 255 : (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
