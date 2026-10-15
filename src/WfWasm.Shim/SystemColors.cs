extern alias WfDraw;

namespace WfWasm.Shim;

using SysColor = WfDraw::System.Drawing.Color;

/// <summary>
/// System color table (cf. SystemColors, Windows 10 GetSysColor values) plus
/// the HLS lighten/darken math behind ControlPaint.Light/Dark (cf. HLSColor).
/// Real-named entries are Colors; canvas uint packing (0xRRGGBBAA) converts
/// implicitly. Themed*/gradient entries are our own extensions and stay uint.
/// </summary>
public static class SystemColors
{
    public static SysColor Control => SysColor.FromArgb(0xF0, 0xF0, 0xF0);
    public static SysColor ControlLightLight => SysColor.FromArgb(0xFF, 0xFF, 0xFF);
    public static SysColor ControlLight => SysColor.FromArgb(0xE3, 0xE3, 0xE3);
    public static SysColor ControlDark => SysColor.FromArgb(0xA0, 0xA0, 0xA0);
    public static SysColor ControlDarkDark => SysColor.FromArgb(0x69, 0x69, 0x69);
    public static SysColor ControlText => SysColor.FromArgb(0, 0, 0);
    public static SysColor GrayText => SysColor.FromArgb(0x6D, 0x6D, 0x6D);
    public static SysColor Highlight => SysColor.FromArgb(0x00, 0x78, 0xD7);
    public static SysColor HighlightText => SysColor.FromArgb(0xFF, 0xFF, 0xFF);
    public static SysColor HotTrack => SysColor.FromArgb(0x00, 0x78, 0xD7);
    public static SysColor Window => SysColor.FromArgb(0xFF, 0xFF, 0xFF);
    public static SysColor WindowText => SysColor.FromArgb(0, 0, 0);
    public static SysColor Menu => SysColor.FromArgb(0xF0, 0xF0, 0xF0);
    public static SysColor MenuText => SysColor.FromArgb(0, 0, 0);
    public static SysColor ScrollBar => SysColor.FromArgb(0xC8, 0xC8, 0xC8);
    public static SysColor ActiveCaption => SysColor.FromArgb(0x00, 0x78, 0xD7);
    public static SysColor ActiveCaptionText => SysColor.FromArgb(0xFF, 0xFF, 0xFF);
    public static SysColor InactiveCaption => SysColor.FromArgb(0xFF, 0xFF, 0xFF);
    public static SysColor InactiveCaptionText => SysColor.FromArgb(0, 0, 0);
    public static SysColor ActiveBorder => SysColor.FromArgb(0xB4, 0xB4, 0xB4);
    public static SysColor InactiveBorder => SysColor.FromArgb(0xF4, 0xF7, 0xFC);
    public static SysColor AppWorkspace => SysColor.FromArgb(0x80, 0x80, 0x80);
    public static SysColor Desktop => SysColor.FromArgb(0, 0, 0);
    public static SysColor WindowFrame => SysColor.FromArgb(0, 0, 0);
    public static SysColor Info => SysColor.FromArgb(0xFF, 0xFF, 0xE1);
    public static SysColor InfoText => SysColor.FromArgb(0, 0, 0);
    public static SysColor MenuBar => SysColor.FromArgb(0xF0, 0xF0, 0xF0);
    public static SysColor MenuHighlight => SysColor.FromArgb(0x00, 0x78, 0xD7);
    public static SysColor ButtonFace => Control;
    public static SysColor ButtonShadow => ControlDark;
    public static SysColor ButtonHighlight => ControlLightLight;
    public const uint Black = 0x000000FF;
    public const uint White = 0xFFFFFFFF;

    // Classic (non-themed) caption gradient stops.
    public const uint ActiveCaptionTop = 0x000080FF;
    public const uint ActiveCaptionBottom = 0x1084D0FF;

    /// <summary>Active caption gradient end (cf. GradientActiveCaption).</summary>
    public static SysColor GradientActiveCaption => SysColor.FromArgb(0xB9, 0xD1, 0xEA);

    /// <summary>Inactive caption gradient end (cf. GradientInactiveCaption).</summary>
    public static SysColor GradientInactiveCaption => SysColor.FromArgb(0xD7, 0xE4, 0xF2);
    public const uint InactiveCaptionTop = 0x808080FF;
    public const uint InactiveCaptionBottom = 0xB5B5B5FF;

    // Themed (Windows 10 visual-styles) control palette.
    public const uint ThemedButtonBorder = 0xADADADFF;
    public const uint ThemedButtonFill = 0xE1E1E1FF;
    public const uint ThemedButtonHotBorder = 0x0078D7FF;
    public const uint ThemedButtonHotFill = 0xE5F1FBFF;
    public const uint ThemedButtonPressedBorder = 0x005499FF;
    public const uint ThemedButtonPressedFill = 0xCCE4F7FF;
    public const uint ThemedButtonDisabledBorder = 0xBFBFBFFF;
    public const uint ThemedButtonDisabledFill = 0xCCCCCCFF;
    public const uint ThemedGlyphBorder = 0x333333FF;
    public const uint ThemedGlyphHotBorder = 0x0078D7FF;
    public const uint ThemedGlyphDisabledBorder = 0xBFBFBFFF;
    public const uint ThemedArrowGlyph = 0x606060FF;
    public const uint ThemedEditBorder = 0xABADB3FF;
    public const uint ThemedScrollTrack = 0xF0F0F0FF;
    public const uint ThemedScrollThumb = 0xCDCDCDFF;
    public const uint ThemedScrollThumbHot = 0xA6A6A6FF;
    public const uint ThemedScrollThumbPressed = 0x606060FF;
    public const uint ThemedMenuHotFill = 0xE5F1FBFF;
    public const uint ThemedMenuHotBorder = 0xCCE4F7FF;
    public const uint ThemedMenuGutter = 0xEFEFEFFF;
    public const uint ThemedSeparator = 0xD4D4D4FF;
    public const uint ThemedTabBorder = 0xABADB3FF;
    public const uint ThemedGroupBorder = 0xBFBFBFFF;
    public const uint ThemedGridHeaderTop = 0xF9F9F9FF;
    public const uint ThemedGridHeaderBottom = 0xEAEAEAFF;
    public const uint ThemedCaptionActive = 0xFFFFFFFF;
    public const uint ThemedCaptionInactive = 0xF0F0F0FF;

    /// <summary>Lighter variant (cf. ControlPaint.Light): halfway to white.</summary>
    public static uint Light(uint rgba) => AdjustLightness(rgba, 0.5f, lighten: true);

    /// <summary>Lightest variant (cf. ControlPaint.LightLight).</summary>
    public static uint LightLight(uint rgba) => AdjustLightness(rgba, 1.0f, lighten: true);

    /// <summary>Darker variant (cf. ControlPaint.Dark): halfway to black.</summary>
    public static uint Dark(uint rgba) => AdjustLightness(rgba, 0.5f, lighten: false);

    /// <summary>Darkest variant (cf. ControlPaint.DarkDark).</summary>
    public static uint DarkDark(uint rgba) => AdjustLightness(rgba, 1.0f, lighten: false);

    private static uint AdjustLightness(uint rgba, float amount, bool lighten)
    {
        byte r = (byte)(rgba >> 24);
        byte g = (byte)((rgba >> 16) & 0xFF);
        byte b = (byte)((rgba >> 8) & 0xFF);
        byte a = (byte)(rgba & 0xFF);
        RgbToHls(r, g, b, out double h, out double l, out double s);
        l = lighten ? l + ((1 - l) * amount) : l * (1 - amount);
        (byte nr, byte ng, byte nb) = HlsToRgb(h, Math.Clamp(l, 0, 1), s);
        return ((uint)nr << 24) | ((uint)ng << 16) | ((uint)nb << 8) | a;
    }

    private static void RgbToHls(byte r, byte g, byte b, out double h, out double l, out double s)
    {
        double dr = r / 255.0, dg = g / 255.0, db = b / 255.0;
        double max = Math.Max(dr, Math.Max(dg, db));
        double min = Math.Min(dr, Math.Min(dg, db));
        l = (max + min) / 2;
        if (max == min)
        {
            h = 0;
            s = 0;
            return;
        }

        double delta = max - min;
        s = l <= 0.5 ? delta / (max + min) : delta / (2 - max - min);
        if (max == dr)
            h = ((dg - db) / delta) + (dg < db ? 6 : 0);
        else if (max == dg)
            h = ((db - dr) / delta) + 2;
        else
            h = ((dr - dg) / delta) + 4;
        h *= 60;
    }

    private static (byte R, byte G, byte B) HlsToRgb(double h, double l, double s)
    {
        if (s == 0)
        {
            byte v = (byte)Math.Round(l * 255);
            return (v, v, v);
        }

        double q = l < 0.5 ? l * (1 + s) : (l + s) - (l * s);
        double p = (2 * l) - q;
        double hk = h / 360;
        return (
            (byte)Math.Round(Channel(p, q, hk + (1.0 / 3)) * 255),
            (byte)Math.Round(Channel(p, q, hk) * 255),
            (byte)Math.Round(Channel(p, q, hk - (1.0 / 3)) * 255));
    }

    private static double Channel(double p, double q, double t)
    {
        if (t < 0)
            t += 1;
        if (t > 1)
            t -= 1;
        if (t < 1.0 / 6)
            return p + ((q - p) * 6 * t);
        if (t < 0.5)
            return q;
        if (t < 2.0 / 3)
            return p + ((q - p) * ((2.0 / 3) - t) * 6);
        return p;
    }
}
