// Hand-written Font/FontFamily: exact .NET member shapes, web-compatible
// (no native calls). Typeface resolution and text measurement belong to the
// backend; metrics here are documented approximations.
using System.ComponentModel;

namespace System.Drawing;

/// <summary>Typeface family (cf. System.Drawing.FontFamily).</summary>
public sealed class FontFamily : IEquatable<FontFamily>, IDisposable
{
    public FontFamily(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    public string Name { get; }

    public static FontFamily GenericSansSerif { get; } = new("Microsoft Sans Serif");
    public static FontFamily GenericSerif { get; } = new("Times New Roman");
    public static FontFamily GenericMonospace { get; } = new("Courier New");

    /// <summary>
    /// Installed families (cf. FontFamily.Families). No font enumeration
    /// exists on web, so this reports the three generic families.
    /// </summary>
    public static FontFamily[] Families { get; } = new[] { GenericSansSerif, GenericSerif, GenericMonospace };

    public FontFamily(string name, Text.FontCollection fontCollection)
        : this(name)
    {
        ArgumentNullException.ThrowIfNull(fontCollection);
    }

    public FontFamily(Text.GenericFontFamilies genericFamily)
        : this(genericFamily switch
        {
            Text.GenericFontFamilies.Serif => "Times New Roman",
            Text.GenericFontFamilies.Monospace => "Courier New",
            _ => "Microsoft Sans Serif",
        })
    {
    }

    /// <summary>Design metrics for the generic families, probed from .NET 9
    /// (Microsoft Sans Serif 1888/430/2318, Times New Roman 1825/443/2355,
    /// Courier New 1705/615/2320 at 2048 em). Other families fall back to
    /// the sans-serif row until font-file parsing lands.</summary>
    public int GetEmHeight(FontStyle style) => 2048;

    public int GetCellAscent(FontStyle style) => MetricsFor(Name).Ascent;

    public int GetCellDescent(FontStyle style) => MetricsFor(Name).Descent;

    public int GetLineSpacing(FontStyle style) => MetricsFor(Name).LineSpacing;

    public string GetName(int language) => Name;

    public static FontFamily[] GetFamilies(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        return Families;
    }

    private static (int Ascent, int Descent, int LineSpacing) MetricsFor(string name) =>
        name switch
        {
            "Times New Roman" => (1825, 443, 2355),
            "Courier New" => (1705, 615, 2320),
            _ => (1888, 430, 2318),
        };

    /// <summary>
    /// GDI+ typographic line-box height as a multiple of the em size
    /// (desktop oracle text-linebox.fsx). GDI+ picks per-font vertical
    /// metrics that only sometimes equal the design line spacing
    /// (Segoe UI 1.33 vs design 1.13!); unknown families fall back to
    /// the design ratio.
    /// </summary>
    internal static float GdiLineBoxFactor(string name) =>
        name switch
        {
            "Segoe UI" => 1.3301f,
            "Tahoma" => 1.2070f,
            "Verdana" => 1.2153f,
            "Trebuchet MS" => 1.1611f,
            "Georgia" => 1.1362f,
            "Times New Roman" => 1.1074f,
            "Courier New" => 1.1328f,
            "Arial" => 1.1172f,
            "Microsoft Sans Serif" => 1.1318f,
            _ => MetricsFor(name).LineSpacing / 2048f,
        };

    /// <summary>
    /// The canvas backend renders every style from the same outlines, so all
    /// styles report available (cf. IsStyleAvailable).
    /// </summary>
    public bool IsStyleAvailable(FontStyle style) => true;

    public bool Equals(FontFamily? other) =>
        other is not null && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is FontFamily other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public static bool operator ==(FontFamily? left, FontFamily? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(FontFamily? left, FontFamily? right) => !(left == right);

    public override string ToString() => Name;

    ~FontFamily()
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

/// <summary>Typeface + size + style (cf. System.Drawing.Font).</summary>
[TypeConverter(typeof(FontConverter))]
public sealed class Font : IEquatable<Font>, IDisposable, ICloneable
{
    public Font(FontFamily family, float emSize)
        : this(family, emSize, FontStyle.Regular, GraphicsUnit.Point)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style)
        : this(family, emSize, style, GraphicsUnit.Point)
    {
    }

    public Font(FontFamily family, float emSize, GraphicsUnit unit)
        : this(family, emSize, FontStyle.Regular, unit)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit)
        : this(family, emSize, style, unit, 1)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet)
        : this(family, emSize, style, unit, gdiCharSet, false)
    {
    }

    public Font(FontFamily family, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet, bool gdiVerticalFont)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(emSize, 0);
        FontFamily = family;
        Size = emSize;
        Style = style;
        Unit = unit;
        GdiCharSet = gdiCharSet;
        GdiVerticalFont = gdiVerticalFont;
    }

    public Font(string familyName, float emSize)
        : this(new FontFamily(familyName), emSize)
    {
    }

    public Font(string familyName, float emSize, FontStyle style)
        : this(new FontFamily(familyName), emSize, style)
    {
    }

    public Font(string familyName, float emSize, GraphicsUnit unit)
        : this(new FontFamily(familyName), emSize, FontStyle.Regular, unit)
    {
    }

    public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit)
        : this(new FontFamily(familyName), emSize, style, unit)
    {
    }

    public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet)
        : this(new FontFamily(familyName), emSize, style, unit, gdiCharSet)
    {
    }

    public Font(string familyName, float emSize, FontStyle style, GraphicsUnit unit, byte gdiCharSet, bool gdiVerticalFont)
        : this(new FontFamily(familyName), emSize, style, unit, gdiCharSet, gdiVerticalFont)
    {
    }

    public Font(Font prototype, FontStyle newStyle)
        : this(prototype.FontFamily, prototype.Size, newStyle, prototype.Unit, prototype.GdiCharSet, prototype.GdiVerticalFont)
    {
    }

    public FontFamily FontFamily { get; }
    public float Size { get; }
    public FontStyle Style { get; }
    public GraphicsUnit Unit { get; }
    public byte GdiCharSet { get; }
    public bool GdiVerticalFont { get; }

    public string Name => FontFamily.Name;
    public bool Bold => (Style & FontStyle.Bold) != 0;
    public bool Italic => (Style & FontStyle.Italic) != 0;
    public bool Underline => (Style & FontStyle.Underline) != 0;
    public bool Strikeout => (Style & FontStyle.Strikeout) != 0;

    /// <summary>
    /// Line spacing in pixels, approximated from em size (cf. Font.Height):
    /// backends without font metrics use 96dpi point conversion.
    /// </summary>
    public int Height => Unit == GraphicsUnit.Pixel
        ? (int)Math.Ceiling(Size)
        : (int)Math.Ceiling(Size * 96f / 72f);

    public bool IsSystemFont => false;

    /// <summary>Size in points (cf. Font.SizeInPoints).</summary>
    public float SizeInPoints => Unit switch
    {
        GraphicsUnit.Point => Size,
        GraphicsUnit.Pixel => Size * 72f / 96f,
        GraphicsUnit.Inch => Size * 72f,
        GraphicsUnit.Document => Size * 72f / 300f,
        GraphicsUnit.Millimeter => Size * 72f / 25.4f,
        _ => Size,
    };

    public string OriginalFontName => Name;
    public string SystemFontName => IsSystemFont ? Name : "";

    /// <summary>Line spacing in pixels at 96dpi (cf. Font.GetHeight()).</summary>
    public float GetHeight() => Height;

    public float GetHeight(float dpi) => Height * dpi / 96f;
    public float GetHeight(Graphics graphics) => GetHeight(graphics?.DpiY ?? 96f);

    private static int s_fontHandleCounter = 0x1000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, WeakReference<Font>> s_fontHandles = new();

    /// <summary>GDI font handles pseudo-allocated for WinForms handle wrapper compatibility.</summary>
    public IntPtr ToHfont()
    {
        var handle = (IntPtr)Interlocked.Increment(ref s_fontHandleCounter);
        s_fontHandles[handle] = new WeakReference<Font>(this);
        return handle;
    }

    public static Font FromHfont(IntPtr hfont)
    {
        if (s_fontHandles.TryGetValue(hfont, out var wr) && wr.TryGetTarget(out var font))
            return font;
        return SystemFonts.DefaultFont;
    }

    public static bool TryFromHfont(IntPtr hfont, out Font? font)
    {
        font = null;
        return s_fontHandles.TryGetValue(hfont, out var wr) && wr.TryGetTarget(out font);
    }

    public static Font FromHdc(IntPtr hdc) =>
        throw new NotSupportedException("GDI device contexts are not available on web.");

    public static Font FromLogFont(object logFont) => FromLogFont(logFont, IntPtr.Zero);

    public static Font FromLogFont(object logFont, IntPtr hdc)
    {
        _ = hdc;
        if (logFont is not Interop.LOGFONT lf)
            throw new ArgumentException("logFont must be a LOGFONT.", nameof(logFont));
        var style = FontStyle.Regular;
        if (lf.lfWeight >= 700)
            style |= FontStyle.Bold;
        if (lf.lfItalic != 0)
            style |= FontStyle.Italic;
        if (lf.lfUnderline != 0)
            style |= FontStyle.Underline;
        if (lf.lfStrikeOut != 0)
            style |= FontStyle.Strikeout;
        return new Font(lf.FaceName(), Math.Max(1, Math.Abs(lf.lfHeight)), style, GraphicsUnit.Pixel);
    }

    public static Font FromLogFont(in Interop.LOGFONT logFont) =>
        FromLogFont((object)logFont);

    public static Font FromLogFont(in Interop.LOGFONT logFont, IntPtr hdc) =>
        FromLogFont((object)logFont, hdc);

    public void ToLogFont(out Interop.LOGFONT logFont) => ToLogFont(out logFont, null);

    public void ToLogFont(out Interop.LOGFONT logFont, Graphics? graphics)
    {
        _ = graphics;
        logFont = new Interop.LOGFONT
        {
            lfHeight = -Height,
            lfWeight = Bold ? 700 : 400,
            lfItalic = (byte)(Italic ? 1 : 0),
            lfUnderline = (byte)(Underline ? 1 : 0),
            lfStrikeOut = (byte)(Strikeout ? 1 : 0),
            lfCharSet = GdiCharSet
        };
        logFont.SetFaceName(Name);
    }

    public void ToLogFont(object logFont) => ToLogFont(logFont, null);

    public void ToLogFont(object logFont, Graphics? graphics)
    {
        _ = graphics;
        if (logFont is not Interop.LOGFONT)
            throw new ArgumentException("logFont must be a LOGFONT.", nameof(logFont));
    }

    public object Clone() => new Font(FontFamily, Size, Style, Unit, GdiCharSet, GdiVerticalFont);

    ~Font()
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

    public bool Equals(Font? other) =>
        other is not null && FontFamily.Equals(other.FontFamily)
        && Size.Equals(other.Size) && Style == other.Style && Unit == other.Unit;

    public override bool Equals(object? obj) => obj is Font other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(FontFamily, Size, (int)Style, (int)Unit);

    public override string ToString() => $"{Name}, {Size}pt, style={Style}";
}
