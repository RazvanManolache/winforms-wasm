// Hand-written Color core: exact .NET behavior, web-compatible.
// Known-color data (ARGB table, statics) is generated in GenColors.cs.
// Signatures are verified by the ApiScan parity gate.
using System.ComponentModel;

namespace System.Drawing;

[TypeConverter(typeof(ColorConverter))]
public partial struct Color : IEquatable<Color>
{
    private const short StateKnownColorValid = 1;
    private const short StateValueValid = 2;
    private const short StateNameValid = 8;

    private readonly string? name;
    private readonly long value;
    private readonly short knownColor;
    private readonly short state;

    public static readonly Color Empty;

    private Color(string? name, long value, short knownColor, short state)
    {
        this.name = name;
        this.value = value;
        this.knownColor = knownColor;
        this.state = state;
    }

    public readonly byte A => (byte)(Value >> 24);
    public readonly byte R => (byte)(Value >> 16);
    public readonly byte G => (byte)(Value >> 8);
    public readonly byte B => (byte)Value;

    public readonly bool IsEmpty => state == 0;
    public readonly bool IsKnownColor => (state & StateKnownColorValid) != 0;

    public readonly bool IsSystemColor =>
        IsKnownColor && (uint)knownColor <= KnownColorTable.Max && (uint)knownColor < (uint)KnownColorTable.IsSystem.Length && KnownColorTable.IsSystem[knownColor];

    public readonly bool IsNamedColor => (state & StateNameValid) != 0 || IsKnownColor;

    public readonly string Name
    {
        get
        {
            if ((state & StateNameValid) != 0)
                return name!;
            if ((state & StateKnownColorValid) != 0)
                return ((KnownColor)knownColor).ToString();
            if ((state & StateValueValid) != 0)
                return Convert.ToString((int)value, 16);
            return ((KnownColor)knownColor).ToString();
        }
    }

    internal readonly int Value
    {
        get
        {
            if ((state & StateValueValid) != 0)
                return (int)value;
            if ((state & StateKnownColorValid) != 0 && (uint)knownColor < (uint)KnownColorTable.Argb.Length)
                return KnownColorTable.Argb[knownColor];
            return 0;
        }
    }

    public static Color FromArgb(int argb) => new(null, (long)(uint)argb, 0, StateValueValid);

    public static Color FromArgb(int alpha, int red, int green, int blue)
    {
        CheckByte(alpha, nameof(alpha));
        CheckByte(red, nameof(red));
        CheckByte(green, nameof(green));
        CheckByte(blue, nameof(blue));
        long v = (long)(uint)((alpha << 24) | (red << 16) | (green << 8) | blue);
        return new Color(null, v, 0, StateValueValid);
    }

    public static Color FromArgb(int alpha, Color baseColor)
    {
        CheckByte(alpha, nameof(alpha));
        long v = (long)(uint)((alpha << 24) | (baseColor.Value & 0xFFFFFF));
        return new Color(null, v, 0, StateValueValid);
    }

    public static Color FromArgb(int red, int green, int blue) => FromArgb(255, red, green, blue);

    /// <summary>
    /// Canvas interop: the shim/painting stack packs colors as 0xRRGGBBAA
    /// uints while Color stores 0xAARRGGBB. These operators bridge the two
    /// without touching call sites (extra surface beyond the framework,
    /// ignored by the parity scan).
    /// </summary>
    public static implicit operator uint(Color color) =>
        ((uint)color.R << 24) | ((uint)color.G << 16) | ((uint)color.B << 8) | color.A;

    public static implicit operator Color(uint rgba) => FromArgb(
        (byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8));

    public static Color FromKnownColor(KnownColor color) =>
        color == 0 ? new Color(null, 0, 0, 0) : new Color(null, 0, (short)color, StateKnownColorValid);

    public static Color FromName(string name)
    {
        if (NameToKnown.TryGetValue(name, out KnownColor known))
            return new Color(name, 0, (short)known, (short)(StateKnownColorValid | StateNameValid));
        return new Color(name, 0, 0, StateNameValid);
    }

    public readonly int ToArgb() => Value;
    public KnownColor ToKnownColor() => (KnownColor)knownColor;

    public readonly float GetBrightness()
    {
        byte min = Math.Min(Math.Min(R, G), B);
        byte max = Math.Max(Math.Max(R, G), B);
        return (max + min) / 510f;
    }

    public readonly float GetSaturation()
    {
        byte min = Math.Min(Math.Min(R, G), B);
        byte max = Math.Max(Math.Max(R, G), B);
        if (max == min)
            return 0f;
        int sum = max + min;
        if (sum > 255)
            sum = 510 - sum;
        return (max - min) / (float)sum;
    }

    public readonly float GetHue()
    {
        if (R == G && G == B)
            return 0f;
        byte max = Math.Max(Math.Max(R, G), B);
        byte min = Math.Min(Math.Min(R, G), B);
        float delta = max - min;
        float hue = R == max ? (G - B) / delta
            : G == max ? 2f + (B - R) / delta
            : 4f + (R - G) / delta;
        hue *= 60f;
        if (hue < 0f)
            hue += 360f;
        return hue;
    }

    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    public readonly bool Equals(Color other) =>
        value == other.value && state == other.state
        && knownColor == other.knownColor && name == other.name;

    public override readonly bool Equals(object? obj) => obj is Color other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(value, state, knownColor, name);

    public override readonly string ToString()
    {
        if (IsEmpty)
            return "Color [Empty]";
        if (IsNamedColor)
            return $"Color [{Name}]";
        return $"Color [A={A}, R={R}, G={G}, B={B}]";
    }

    private static void CheckByte(int value, string name)
    {
        if (value < 0 || value > 255)
            throw new ArgumentException($"Value of '{value}' is not valid for '{name}'.");
    }

    internal static readonly Dictionary<string, KnownColor> NameToKnown = BuildNameMap();

    private static Dictionary<string, KnownColor> BuildNameMap()
    {
        var map = new Dictionary<string, KnownColor>(StringComparer.OrdinalIgnoreCase);
        foreach (KnownColor kc in Enum.GetValues<KnownColor>())
        {
            if ((int)kc != 0)
                map[kc.ToString()] = kc;
        }

        return map;
    }
}

public sealed class ColorTranslator
{
    private ColorTranslator()
    {
    }

    public static Color FromHtml(string htmlColor)
    {
        // Desktop parity (oracle-verified case by case, including the odd
        // corners): null/empty read Empty; leading/trailing SPACES are
        // ignored (Syncfusion themes ship " #444444"-style values; without
        // this every themed paint threw ArgumentException); other
        // whitespace is NOT skipped (a tab-padded "#fff" takes the plain
        // integer path below, exactly like desktop).
        if (string.IsNullOrEmpty(htmlColor))
            return Color.Empty;
        htmlColor = htmlColor.Trim(' ');
        if (htmlColor.Length == 0)
            return Color.Empty;
        if (htmlColor[0] != '#')
        {
            if (htmlColor.Equals("LightGrey", StringComparison.OrdinalIgnoreCase))
                return Color.FromKnownColor(KnownColor.LightGray);
            if (Color.NameToKnown.TryGetValue(htmlColor, out KnownColor trimmedKnown))
                return Color.FromKnownColor(trimmedKnown);
            // Odd desktop corner: other whitespace is not skipped up front,
            // but a fully-trimmed "#..." still parses as a plain integer
            // ("\t #fff\t" reads 0x00000FFF, oracle-verified).
            string fullTrim = htmlColor.Trim();
            if (fullTrim.Length > 0 && fullTrim[0] == '#')
                return Color.FromArgb(Convert.ToInt32(fullTrim.Substring(1), 16));
            throw new ArgumentException($"'{htmlColor}' is not a valid color.");
        }
        {
            string hex = htmlColor.Substring(1);
            if (hex.Length == 3)
            {
                int r = Convert.ToInt32(new string(hex[0], 2), 16);
                int g = Convert.ToInt32(new string(hex[1], 2), 16);
                int b = Convert.ToInt32(new string(hex[2], 2), 16);
                return Color.FromArgb(r, g, b);
            }

            if (hex.Length == 6)
            {
                return Color.FromArgb(
                    Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16));
            }

            if (hex.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToInt32(hex.Substring(0, 2), 16),
                    Convert.ToInt32(hex.Substring(2, 2), 16),
                    Convert.ToInt32(hex.Substring(4, 2), 16),
                    Convert.ToInt32(hex.Substring(6, 2), 16));
            }

            return Color.FromArgb(Convert.ToInt32(hex, 16));
        }

        if (htmlColor.Equals("LightGrey", StringComparison.OrdinalIgnoreCase))
            return Color.FromKnownColor(KnownColor.LightGray);
        if (Color.NameToKnown.TryGetValue(htmlColor, out KnownColor known))
            return Color.FromKnownColor(known);
        throw new ArgumentException($"'{htmlColor}' is not a valid color.");
    }

    public static string ToHtml(Color c)
    {
        if (c.IsNamedColor)
        {
            if (c.IsSystemColor)
                return c.ToKnownColor().ToString().ToLowerInvariant();
            return c.Name;
        }

        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    // OLE system-color index (low 5 bits) -> KnownColor value, 0 = not mapped.
    private static readonly int[] OleIndexToKnown =
    {
        23, 11, 2, 17, 21, 24, 25, 22, 26, 3, 1, 16, 4, 13, 14, 5,
        6, 12, 10, 18, 9, 7, 8, 20, 19, 0, 15, 171, 172, 174, 173, 0,
    };

    private static readonly Dictionary<int, int> KnownToOle = BuildKnownToOle();

    private static Dictionary<int, int> BuildKnownToOle()
    {
        var map = new Dictionary<int, int>();
        for (int i = 0; i < OleIndexToKnown.Length; i++)
        {
            if (OleIndexToKnown[i] != 0)
                map[OleIndexToKnown[i]] = unchecked((int)0x80000000) | i;
        }

        // Aliases sharing an OLE slot with their canonical color.
        map[168] = map[5];
        map[169] = map[9];
        map[170] = map[6];
        return map;
    }

    public static Color FromOle(int oleColor)
    {
        if ((oleColor & unchecked((int)0x80000000)) != 0)
        {
            int index = oleColor & 0xFF;
            if ((uint)index < OleIndexToKnown.Length && OleIndexToKnown[index] != 0)
                return Color.FromKnownColor((KnownColor)OleIndexToKnown[index]);
        }

        return Color.FromArgb(oleColor & 0xFF, (oleColor >> 8) & 0xFF, (oleColor >> 16) & 0xFF);
    }

    public static int ToOle(Color c)
    {
        if (c.IsSystemColor && KnownToOle.TryGetValue((int)c.ToKnownColor(), out int ole))
            return ole;
        return c.R | (c.G << 8) | (c.B << 16);
    }

    public static Color FromWin32(int win32Color) =>
        Color.FromArgb(win32Color & 0xFF, (win32Color >> 8) & 0xFF, (win32Color >> 16) & 0xFF);

    public static int ToWin32(Color c) => c.R | (c.G << 8) | (c.B << 16);
}

