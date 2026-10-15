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
}
