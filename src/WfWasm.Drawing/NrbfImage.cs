// NRBF image extraction: legacy .resources store Bitmap/Icon values as
// BinaryFormatter payloads (user-type entries), and BinaryFormatter is
// removed on .NET 9+, so ResourceManager.GetObject always throws for
// them. Resource payloads have a fixed simple shape (one class with a
// single byte[] member holding PNG/BMP/JPEG/GIF/ICO file bytes), so a
// small record walker recovers the image without a deserializer: parse
// the record stream, collect every byte-array payload, answer the
// largest with valid image magic. Unknown records abort (the caller
// keeps its placeholder); the walk is fully bounds-checked.
// Wire format: MS-NRBF record types 0/1/4/5/6/7/8/9/10/11/12/13/14/15
// plus 16/17. Records 2/3 (ClassWithMembers, layout known only from
// the missing assembly) abort: the netfx writer emits the AndTypes
// forms for resource payloads.
namespace WfWasm.Drawing;

public static class NrbfImage
{
    public static bool TryExtractImageBytes(byte[]? nrbf, out byte[] image)
    {
        image = [];
        if (nrbf is null || nrbf.Length < 20 || nrbf.Length > 100_000_000)
            return false;
        try
        {
            var walker = new Walker(nrbf);
            if (!walker.Run())
                return false;
            byte[]? best = null;
            foreach (byte[] candidate in walker.ByteArrays)
            {
                if (candidate.Length < 8 || !HasImageMagic(candidate))
                    continue;
                if (best is null || candidate.Length > best.Length)
                    best = candidate;
            }
            if (best is null)
                return false;
            image = best;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // .NET Core+ ResourceWriter stores Bitmap/Icon user-type values
    // without any BinaryFormatter envelope: 0x02 marker + 7-bit length +
    // raw image-file bytes (observed live: DRR-stamped Syncfusion
    // streams carry 02 + len + PNG). Lift the inner file when the length
    // fits exactly and the magic matches; anything else is a miss (the
    // caller keeps its placeholder).
    public static bool TryExtractPrefixedImageBytes(byte[]? payload, out byte[] image)
    {
        image = [];
        if (payload is null || payload.Length < 5 || payload[0] != 0x02)
            return false;
        try
        {
            int pos = 1, len = 0, shift = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pos >= payload.Length)
                    return false;
                byte b = payload[pos++];
                len |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
            }
            if (len <= 0 || pos + len != payload.Length)
                return false;
            byte[] inner = new byte[len];
            System.Buffer.BlockCopy(payload, pos, inner, 0, len);
            if (!HasImageMagic(inner))
                return false;
            image = inner;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasImageMagic(byte[] bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true; // PNG
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true; // JPEG
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
            && bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
            return true; // GIF
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return true; // BMP
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00
            && (bytes[2] == 0x01 || bytes[2] == 0x02) && bytes[3] == 0x00)
            return true; // ICO/CUR
        if (bytes.Length >= 4 && ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00)
            || (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)))
            return true; // TIFF
        return false;
    }

    private sealed class MemberLayout
    {
        internal readonly byte[] BinTypes;
        internal readonly byte[] PrimTypes;
        internal MemberLayout(int count)
        {
            BinTypes = new byte[count];
            PrimTypes = new byte[count];
        }
    }

    private sealed class Walker
    {
        private readonly byte[] _buf;
        private int _pos;
        private int _records;
        private int _depth;
        internal readonly System.Collections.Generic.List<byte[]> ByteArrays = new();
        private readonly System.Collections.Generic.Dictionary<int, MemberLayout> _layouts = new();

        internal Walker(byte[] buf)
        {
            _buf = buf;
        }

        internal bool Run()
        {
            if (!Need(1) || ReadByte() != 0)
                return false;
            if (!Need(16))
                return false;
            _pos += 16; // SerializedStreamHeader root/header/version
            // Top-level records (libraries precede the root object);
            // MessageEnd closes the stream, EOF is tolerated.
            while (true)
            {
                if (!Need(1))
                    return true;
                if (_buf[_pos] == 11)
                    return true;
                if (!ParseRecord())
                    return false;
            }
        }

        private bool ParseRecord()
        {
            if (++_records > 20000 || ++_depth > 100)
                return false;
            try
            {
                if (!Need(1))
                    return false;
                byte rec = ReadByte();
                switch (rec)
                {
                    case 0:
                        if (!Need(16))
                            return false;
                        _pos += 16;
                        return true;
                    case 1:
                    {
                        if (!Need(8))
                            return false;
                        int id = ReadInt32();
                        int meta = ReadInt32();
                        if (!_layouts.TryGetValue(meta, out MemberLayout? layout))
                            return false;
                        _layouts[id] = layout;
                        return ParseMembers(layout);
                    }
                    case 4:
                    case 5:
                        return ParseClass(rec);
                    case 6:
                    {
                        if (!Need(4))
                            return false;
                        _pos += 4;
                        return SkipString();
                    }
                    case 7:
                        return ParseBinaryArray();
                    case 8:
                    {
                        if (!Need(1))
                            return false;
                        return SkipPrimitive(ReadByte());
                    }
                    case 9:
                    {
                        if (!Need(4))
                            return false;
                        _pos += 4;
                        return true;
                    }
                    case 10:
                        return true;
                    case 11:
                        return true;
                    case 12:
                    {
                        if (!Need(4))
                            return false;
                        _pos += 4;
                        return SkipString();
                    }
                    case 13:
                    {
                        if (!Need(1))
                            return false;
                        _pos += 1;
                        return true;
                    }
                    case 14:
                    {
                        if (!Need(4))
                            return false;
                        _pos += 4;
                        return true;
                    }
                    case 15:
                        return ParseArraySinglePrimitive();
                    case 16:
                    case 17:
                        return ParseArraySingleObjectOrString();
                    default:
                        return false;
                }
            }
            finally
            {
                _depth--;
            }
        }

        private bool ParseClass(byte rec)
        {
            if (!Need(4))
                return false;
            int id = ReadInt32();
            if (!SkipString())
                return false;
            if (!Need(4))
                return false;
            int count = ReadInt32();
            if (count < 0 || count > 1000)
                return false;
            for (int i = 0; i < count; i++)
            {
                if (!SkipString())
                    return false;
            }
            var layout = new MemberLayout(count);
            if (!Need(count))
                return false;
            for (int i = 0; i < count; i++)
                layout.BinTypes[i] = ReadByte();
            for (int i = 0; i < count; i++)
            {
                if (!SkipMemberTypeInfo(layout.BinTypes[i], layout, i))
                    return false;
            }
            if (rec == 5)
            {
                if (!Need(4))
                    return false;
                _pos += 4; // libraryId
            }
            _layouts[id] = layout;
            return ParseMembers(layout);
        }

        private bool SkipMemberTypeInfo(byte binType, MemberLayout layout, int index)
        {
            switch (binType)
            {
                case 0: // Primitive: PrimitiveTypeEnum byte
                case 7: // PrimitiveArray: element PrimitiveTypeEnum byte
                    if (!Need(1))
                        return false;
                    layout.PrimTypes[index] = ReadByte();
                    return true;
                case 1: // String
                case 2: // Object
                case 5: // ObjectArray
                case 6: // StringArray
                    return true; // no additional info
                case 3: // SystemClass: class name
                    return SkipString();
                case 4: // Class: class name + libraryId
                    if (!SkipString() || !Need(4))
                        return false;
                    _pos += 4;
                    return true;
                default:
                    return false;
            }
        }

        private bool ParseMembers(MemberLayout layout)
        {
            for (int i = 0; i < layout.BinTypes.Length; i++)
            {
                if (layout.BinTypes[i] == 0)
                {
                    if (!SkipPrimitive(layout.PrimTypes[i]))
                        return false;
                }
                else if (!ParseRecord())
                {
                    return false;
                }
            }
            return true;
        }

        private bool ParseBinaryArray()
        {
            if (!Need(8))
                return false;
            _pos += 4; // objectId
            int rank = ReadInt32();
            if (rank < 0 || rank > 8 || !Need(rank * 8 + 1))
                return false;
            long total = 1;
            int[] lens = new int[rank];
            for (int i = 0; i < rank; i++)
            {
                lens[i] = ReadInt32();
                if (lens[i] < 0 || lens[i] > 100_000_000)
                    return false;
                total *= lens[i];
                if (total > 100_000_000)
                    return false;
            }
            _pos += rank * 4; // lower bounds
            byte typeCode = ReadByte();
            byte primType = 0;
            if (typeCode == 0)
            {
                if (!Need(1))
                    return false;
                primType = ReadByte();
            }
            else if (typeCode == 3)
            {
                if (!SkipString())
                    return false;
            }
            else if (typeCode == 4)
            {
                if (!SkipString() || !Need(4))
                    return false;
                _pos += 4;
            }
            else if (typeCode > 4)
            {
                return false;
            }
            if (typeCode == 0)
            {
                int size = PrimitiveSize(primType);
                if (size <= 0 || !Need(total * size))
                    return false;
                if (primType == 2 && rank == 1)
                {
                    byte[] payload = new byte[lens[0]];
                    System.Buffer.BlockCopy(_buf, _pos, payload, 0, lens[0]);
                    ByteArrays.Add(payload);
                }
                _pos += (int)(total * size);
                return true;
            }
            for (long i = 0; i < total; i++)
            {
                if (!ParseRecord())
                    return false;
            }
            return true;
        }

        private bool ParseArraySinglePrimitive()
        {
            if (!Need(9))
                return false;
            _pos += 4; // objectId
            int length = ReadInt32();
            if (length < 0 || length > 100_000_000)
                return false;
            byte primType = ReadByte();
            int size = PrimitiveSize(primType);
            if (size <= 0 || !Need((long)length * size))
                return false;
            if (primType == 2)
            {
                byte[] payload = new byte[length];
                System.Buffer.BlockCopy(_buf, _pos, payload, 0, length);
                ByteArrays.Add(payload);
            }
            _pos += length * size;
            return true;
        }

        private bool ParseArraySingleObjectOrString()
        {
            if (!Need(8))
                return false;
            _pos += 4; // objectId
            int length = ReadInt32();
            if (length < 0 || length > 1_000_000)
                return false;
            for (int i = 0; i < length; i++)
            {
                if (!ParseRecord())
                    return false;
            }
            return true;
        }

        private bool SkipPrimitive(byte primType)
        {
            if (primType == 18)
                return ParseRecord(); // embedded string record
            int size = PrimitiveSize(primType);
            if (size <= 0 || !Need(size))
                return false;
            _pos += size;
            return true;
        }

        // Wire codes are InternalPrimitiveTypeE (netfx mscorlib): note
        // Currency at 4 shifting Decimal/Double after it (Int32 is 8,
        // not 7). Null has no inline data (aborts), String parses.
        private static int PrimitiveSize(byte primType) => primType switch
        {
            1 or 2 or 10 => 1, // Boolean, Byte, SByte
            3 or 7 or 14 => 2, // Char, Int16, UInt16
            8 or 11 or 15 => 4, // Int32, Single, UInt32
            4 or 6 or 9 or 12 or 13 or 16 => 8, // Currency, Double, Int64, TimeSpan, DateTime, UInt64
            5 => 16, // Decimal
            _ => -1,
        };

        private bool SkipString()
        {
            int start = _pos;
            int shift = 0;
            int length = 0;
            while (true)
            {
                if (!Need(1))
                    return false;
                byte b = ReadByte();
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;
                shift += 7;
                if (shift > 28 || _pos - start > 5)
                    return false;
            }
            if (length < 0 || !Need(length))
                return false;
            _pos += length;
            return true;
        }

        private bool Need(long count) => count >= 0 && _pos >= 0 && _pos + count <= _buf.Length;

        private byte ReadByte() => _buf[_pos++];

        private int ReadInt32()
        {
            int v = _buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24);
            _pos += 4;
            return v;
        }
    }

    // ImageListStreamer blobs (designer ".ImageStream" entries): the NRBF
    // payload wraps a "MSFt"-magic body = RunLengthEncoder-compressed
    // (count,value) pairs (upstream ImageListStreamer) over a comctl32
    // legacy ImageList_Write stream: 28-byte IL head ("IL", image count,
    // slot System.Drawing.Size) + color BMP + optional 1bpp mask BMP. These carry no
    // image magic, so TryExtractImageBytes never matches them.
    public static bool TryExtractImageListBytes(byte[]? nrbf, out byte[] msft)
    {
        msft = [];
        if (nrbf is null || nrbf.Length < 20 || nrbf.Length > 100_000_000)
            return false;
        try
        {
            var walker = new Walker(nrbf);
            if (!walker.Run())
                return false;
            byte[]? best = null;
            foreach (byte[] candidate in walker.ByteArrays)
            {
                if (candidate.Length < 8 || !HasImageListMagic(candidate))
                    continue;
                if (best is null || candidate.Length > best.Length)
                    best = candidate;
            }
            if (best is null)
                return false;
            msft = best;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasImageListMagic(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == (byte)'M' && bytes[1] == (byte)'S' && bytes[2] == (byte)'F' && bytes[3] == (byte)'t';

    // Decodes an "MSFt" imagelist stream into per-slot bitmaps (mask
    // folded into alpha). Never throws: every anomaly answers false and
    // the caller degrades to an empty list.
    public static bool TryDecodeImageListStream(byte[]? msft, out System.Drawing.Bitmap[]? images, out System.Drawing.Size slot, out int bitDepth)
    {
        images = null;
        slot = System.Drawing.Size.Empty;
        bitDepth = 0;
        if (msft is null || !HasImageListMagic(msft))
            return false;
        try
        {
            return DecodeImageListStreamCore(msft, out images, out slot, out bitDepth);
        }
        catch
        {
            images = null;
            return false;
        }
    }

    private static bool DecodeImageListStreamCore(byte[] msft, out System.Drawing.Bitmap[]? images, out System.Drawing.Size slot, out int bitDepth)
    {
        images = null;
        slot = System.Drawing.Size.Empty;
        bitDepth = 0;
        if ((msft.Length - 4) % 2 != 0)
            return false;
        long total = 0;
        for (int i = 4; i < msft.Length; i += 2)
            total += msft[i];
        if (total <= 0 || total > 100_000_000)
            return false;
        byte[] il = new byte[total];
        int w = 0;
        for (int i = 4; i < msft.Length; i += 2)
        {
            int count = msft[i];
            if (count <= 0)
                return false;
            byte value = msft[i + 1];
            for (int j = 0; j < count; j++)
                il[w++] = value;
        }
        if (il.Length < 28 + 26 || il[0] != (byte)'I' || il[1] != (byte)'L')
            return false;
        int imageCount = il[4] | (il[5] << 8);
        int cx = il[10] | (il[11] << 8);
        int cy = il[12] | (il[13] << 8);
        if (imageCount <= 0 || imageCount > 4096 || cx <= 0 || cy <= 0 || cx > 1024 || cy > 1024)
            return false;
        if (il[28] != (byte)'B' || il[29] != (byte)'M')
            return false;
        byte[] colorSlice = new byte[il.Length - 28];
        Buffer.BlockCopy(il, 28, colorSlice, 0, colorSlice.Length);
        System.Drawing.Bitmap strip;
        try { strip = System.Drawing.BmpCodec.Decode(colorSlice); }
        catch { return false; }
        if (strip.Width < imageCount * cx || strip.Height < cy)
            return false;
        if (ReadI32(il, 28 + 14) < 40)
            return false;
        bitDepth = il[28 + 14 + 14] | (il[28 + 14 + 15] << 8);
        int colorDataOff = ReadI32(il, 28 + 10);
        int colorW = ReadI32(il, 28 + 14 + 4);
        int colorHSigned = ReadI32(il, 28 + 14 + 8);
        int colorH = Math.Abs(colorHSigned);
        bool colorTopDown = colorHSigned < 0;
        long colorStride = ((long)colorW * bitDepth + 31) / 32 * 4;
        long colorEnd = 28L + colorDataOff + colorStride * colorH;
        bool hasMask = false;
        byte[]? maskBits = null;
        int maskW = 0;
        if (colorEnd >= 0 && colorEnd + 26 <= il.Length && il[colorEnd] == (byte)'B' && il[colorEnd + 1] == (byte)'M')
            hasMask = TryReadMaskBits(il, (int)colorEnd, strip.Width, strip.Height, out maskBits, out maskW);
        images = new System.Drawing.Bitmap[imageCount];
        int[] src = strip.Pixels;
        for (int s = 0; s < imageCount; s++)
        {
            var slotBmp = new System.Drawing.Bitmap(cx, cy);
            int[] dest = slotBmp.Pixels;
            for (int y = 0; y < cy; y++)
            {
                for (int x = 0; x < cx; x++)
                {
                    int gx = s * cx + x;
                    int argb = src[y * strip.Width + gx];
                    bool transparent = hasMask && maskBits![y * maskW + gx] == 1;
                    int alpha = transparent ? 0 : (bitDepth == 32 ? ColorAlphaAt(il, 28, colorDataOff, (int)colorStride, colorTopDown, colorH, gx, y) : 255);
                    dest[y * cx + x] = (alpha << 24) | (argb & 0xFFFFFF);
                }
            }
            images[s] = slotBmp;
        }
        slot = new System.Drawing.Size(cx, cy);
        return true;
    }

    // Reads the 1bpp mask BMP into top-down 0/1 bytes. Anything
    // unexpected (wrong depth, compression, geometry) answers false and
    // the caller renders opaque.
    private static bool TryReadMaskBits(byte[] il, int at, int stripW, int stripH, out byte[]? bits, out int maskW)
    {
        bits = null;
        maskW = 0;
        try
        {
            if (at < 0 || at + 26 > il.Length)
                return false;
            if (ReadI32(il, at + 14) != 40)
                return false;
            int mw = ReadI32(il, at + 14 + 4);
            int mhSigned = ReadI32(il, at + 14 + 8);
            int mh = Math.Abs(mhSigned);
            bool topDown = mhSigned < 0;
            int bpp = il[at + 14 + 14] | (il[at + 14 + 15] << 8);
            if (bpp != 1 || ReadI32(il, at + 14 + 16) != 0 || mw != stripW || mh != stripH)
                return false;
            int dataOff = ReadI32(il, at + 10);
            long stride = ((long)mw + 31) / 32 * 4;
            if (dataOff < 0 || (long)dataOff + stride * mh > il.Length - at)
                return false;
            var bytes = new byte[mw * mh];
            for (int y = 0; y < mh; y++)
            {
                int srcRow = topDown ? y : mh - 1 - y;
                for (int x = 0; x < mw; x++)
                    bytes[y * mw + x] = (byte)((il[at + dataOff + srcRow * stride + (x >> 3)] >> (7 - (x & 7))) & 1);
            }
            bits = bytes;
            maskW = mw;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // System.Drawing.BmpCodec drops the alpha channel of BI_RGB 32bpp data; recover it
    // from the XOR bytes for imagelist slot compositing.
    private static int ColorAlphaAt(byte[] il, int colorAt, int dataOff, int stride, bool topDown, int height, int gx, int y)
    {
        try
        {
            if (dataOff < 0 || stride <= 0 || height <= 0)
                return 255;
            int srcRow = topDown ? y : height - 1 - y;
            long o = (long)colorAt + dataOff + (long)srcRow * stride + (long)gx * 4 + 3;
            if (o < 0 || o >= il.Length)
                return 255;
            return il[o];
        }
        catch
        {
            return 255;
        }
    }

    private static int ReadI32(byte[] bytes, int at) =>
        bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24);
}
