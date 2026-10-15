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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _files.Clear();
                _blobs.Clear();
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
        }

        internal override FontFamily[] GetFamilies() => FontFamily.Families;

        private readonly List<string> _files = new();
        private readonly List<byte[]> _blobs = new();
    }
}
