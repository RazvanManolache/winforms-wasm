// Text layout: minimal shapes; measurement/rasterization arrive with the
// text milestone (canvas text draws through the host today).
namespace System.Drawing
{
    /// <summary>Text layout (cf. System.Drawing.StringFormat).</summary>
    public sealed class StringFormat : ICloneable, IDisposable
    {
        public StringFormat()
        {
        }

        public StringFormat(StringFormatFlags options)
        {
            FormatFlags = options;
        }

        public StringFormat(StringFormatFlags options, int language)
        {
            FormatFlags = options;
            Language = language;
        }

        public StringFormat(StringFormat format)
        {
            ArgumentNullException.ThrowIfNull(format);
            FormatFlags = format.FormatFlags;
            Alignment = format.Alignment;
            LineAlignment = format.LineAlignment;
            Language = format.Language;
            Trimming = format.Trimming;
            HotkeyPrefix = format.HotkeyPrefix;
            _digitLanguage = format.DigitSubstitutionLanguage;
            _digitMethod = format.DigitSubstitutionMethod;
            _tabStops = (float[])format._tabStops.Clone();
            _firstTabOffset = format._firstTabOffset;
            _measurableRanges = (CharacterRange[])format._measurableRanges.Clone();
        }

        public StringFormatFlags FormatFlags { get; set; }
        public StringAlignment Alignment { get; set; } = StringAlignment.Near;
        public StringAlignment LineAlignment { get; set; } = StringAlignment.Near;
        public int Language { get; }
        public StringTrimming Trimming { get; set; } = StringTrimming.Character;
        public Text.HotkeyPrefix HotkeyPrefix { get; set; } = Text.HotkeyPrefix.None;
        public int DigitSubstitutionLanguage => _digitLanguage;
        public StringDigitSubstitute DigitSubstitutionMethod => _digitMethod;

        public float[] GetTabStops(out float firstTabOffset)
        {
            firstTabOffset = _firstTabOffset;
            return (float[])_tabStops.Clone();
        }

        public void SetTabStops(float firstTabOffset, float[] tabStops)
        {
            ArgumentNullException.ThrowIfNull(tabStops);
            _firstTabOffset = firstTabOffset;
            _tabStops = (float[])tabStops.Clone();
        }

        public void SetDigitSubstitution(int language, StringDigitSubstitute substitute)
        {
            _digitLanguage = language;
            _digitMethod = substitute;
        }

        public void SetMeasurableCharacterRanges(CharacterRange[] ranges)
        {
            ArgumentNullException.ThrowIfNull(ranges);
            _measurableRanges = (CharacterRange[])ranges.Clone();
        }

        public override string ToString() =>
            $"StringFormat(align={Alignment}, line={LineAlignment}, trim={Trimming})";

        private int _digitLanguage;
        private StringDigitSubstitute _digitMethod = StringDigitSubstitute.User;
        private float[] _tabStops = [];
        private float _firstTabOffset;
        private CharacterRange[] _measurableRanges = [];

        public static StringFormat GenericDefault { get; } = new();
        public static StringFormat GenericTypographic { get; } = new(
            StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip);

        public object Clone() => new StringFormat(this);

        ~StringFormat()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}

namespace System.Drawing.Text
{
    /// <summary>Font family source (cf. System.Drawing.Text.FontCollection).</summary>
    public abstract class FontCollection : IDisposable
    {
        public FontFamily[] Families => GetFamilies();

        internal virtual FontFamily[] GetFamilies() => [];

        ~FontCollection()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Installed families (cf. System.Drawing.Text.InstalledFontCollection).</summary>
    public sealed class InstalledFontCollection : FontCollection
    {
        public InstalledFontCollection()
        {
        }

        internal override FontFamily[] GetFamilies() => FontFamily.Families;
    }

    /// <summary>Memory/file-loaded families (cf. System.Drawing.Text.PrivateFontCollection).</summary>
    public sealed class PrivateFontCollection : FontCollection
    {
        public PrivateFontCollection()
        {
        }

        public void AddFontFile(string filename)
        {
            ArgumentException.ThrowIfNullOrEmpty(filename);
            _files.Add(filename);
            _families.Add(new FontFamily(System.IO.Path.GetFileNameWithoutExtension(filename)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _files.Clear();
                _blobs.Clear();
                _families.Clear();
            }
            base.Dispose(disposing);
        }

        public void AddMemoryFont(IntPtr memory, int length)
        {
            if (memory == IntPtr.Zero)
                throw new ArgumentException("Memory must be non-zero.", nameof(memory));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
            var copy = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(memory, copy, 0, length);
            _blobs.Add(copy);
            // Record only successfully named fonts: consumers (Syncfusion
            // IconFontHelper.RegisterIcons) loop while Families is empty
            // and recurse while the single name mismatches, so an unnamed
            // entry degrades to an empty set (bounded, terminating) rather
            // than a hang, while a parsed entry terminates the loop.
            string? name = TryReadFamilyName(copy);
            if (name is not null)
                _families.Add(new FontFamily(name));
        }

        // Only fonts added to THIS collection (cf. desktop, where installed
        // families never leak into a private collection). Returning the
        // installed generics here made vendor load-once checks misfire:
        // they saw a non-empty set before loading, skipped the load, and
        // then indexed the still-null shared slot.
        internal override FontFamily[] GetFamilies() => _families.ToArray();

        // Best-effort TrueType 'name' table family (nameID 1) read. Never
        // throws: any malformed input yields null. Prefers the Windows
        // Unicode entry, then Mac Roman, then the first family record.
        internal static string? TryReadFamilyName(byte[] bytes)
        {
            try
            {
                if (bytes.Length < 12)
                    return null;
                int tableCount = ReadU16(bytes, 4);
                int nameOffset = -1;
                for (int i = 0; i < tableCount; i++)
                {
                    int record = 12 + i * 16;
                    if (record + 16 > bytes.Length)
                        return null;
                    if (bytes[record] == (byte)'n' && bytes[record + 1] == (byte)'a'
                        && bytes[record + 2] == (byte)'m' && bytes[record + 3] == (byte)'e')
                    {
                        nameOffset = ReadU32(bytes, record + 8);
                        break;
                    }
                }
                if (nameOffset < 0 || nameOffset + 6 > bytes.Length)
                    return null;
                int recordCount = ReadU16(bytes, nameOffset + 2);
                int storageOffset = nameOffset + ReadU16(bytes, nameOffset + 4);
                string? macRoman = null;
                string? first = null;
                for (int i = 0; i < recordCount; i++)
                {
                    int record = nameOffset + 6 + i * 12;
                    if (record + 12 > bytes.Length)
                        break;
                    int platform = ReadU16(bytes, record);
                    int encoding = ReadU16(bytes, record + 2);
                    int language = ReadU16(bytes, record + 4);
                    int nameId = ReadU16(bytes, record + 6);
                    int byteLength = ReadU16(bytes, record + 8);
                    int stringOffset = ReadU16(bytes, record + 10);
                    if (nameId != 1)
                        continue;
                    int start = storageOffset + stringOffset;
                    if (start < 0 || byteLength < 0 || start + byteLength > bytes.Length)
                        continue;
                    if (platform == 3)
                    {
                        string candidate = System.Text.Encoding.BigEndianUnicode.GetString(bytes, start, byteLength);
                        if (encoding == 1 && language == 0x0409)
                            return candidate;
                        first ??= candidate;
                    }
                    else if (platform == 1 && encoding == 0 && language == 0)
                    {
                        macRoman ??= System.Text.Encoding.Latin1.GetString(bytes, start, byteLength);
                    }
                }
                return macRoman ?? first;
            }
            catch
            {
                return null;
            }
        }

        private static int ReadU16(byte[] bytes, int offset) =>
            (bytes[offset] << 8) | bytes[offset + 1];

        private static int ReadU32(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        private readonly List<string> _files = new();
        private readonly List<byte[]> _blobs = new();
        private readonly List<FontFamily> _families = new();
    }
}
