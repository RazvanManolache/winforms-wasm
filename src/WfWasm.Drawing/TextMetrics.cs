// GDI text metrics: oracle-fitted TEXTMETRIC rows probed with GetTextMetricsW
// on Windows GDI (lfHeight=-em, DEFAULT_CHARSET). GDI cell heights differ
// from GDI+ line boxes (winAscent+winDescent with grid fitting vs typo
// metrics), so this table stands apart from GdiLineBoxFactor. It backs
// PInvoke.GetTextMetrics and the GetTextExtentPoint32W heights; because
// AutoScaleMode.Font divides the designer dims by these, exact ints at
// designer sizes are load-bearing (e.g. Microsoft YaHei UI 12px -> 17).
namespace System.Drawing;

/// <summary>One probed TEXTMETRIC row (cf. GetTextMetricsW).</summary>
internal readonly record struct GdiTmRow(int Em, int Height, int Ascent, int Descent, int AveWidth, int MaxWidth = 0, int ExternalLeading = 0);

/// <summary>Oracle-fitted GDI text metrics per family.</summary>
internal static class GdiTextMetrics
{
    // Pitch/family bytes oracled with the rows: 0x27 = variable Truetype Swiss
    // (proportional outline fonts), 0x36 = fixed-pitch Truetype Modern, 0x07 =
    // SimSun's DontCare triple. TMPF_FIXED_PITCH (0x01) SET means the font is
    // proportional in the inverted Windows convention, routing autoscale to
    // the GetTextExtentPoint32W average instead of tmAveCharWidth.
    private const byte VariableSwiss = 0x27;
    private const byte FixedModern = 0x36;

    private static readonly (byte Pitch, GdiTmRow[] Rows) MicrosoftYaHeiUI = (VariableSwiss, new[]
    {
        new GdiTmRow(9, 14, 11, 3, 4),
        new GdiTmRow(12, 17, 13, 4, 6, 17),
        new GdiTmRow(16, 21, 17, 4, 8),
        new GdiTmRow(24, 31, 25, 6, 12),
        new GdiTmRow(40, 52, 42, 10, 19),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) SegoeUI = (VariableSwiss, new[]
    {
        new GdiTmRow(9, 12, 10, 2, 5, 23),
        new GdiTmRow(12, 15, 12, 3, 6, 31),
        new GdiTmRow(16, 21, 17, 4, 9),
        new GdiTmRow(24, 32, 26, 6, 13),
        new GdiTmRow(40, 54, 43, 11, 22),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) MicrosoftSansSerif = (VariableSwiss, new[]
    {
        new GdiTmRow(8, 9, 7, 2, 4, 16),
        new GdiTmRow(11, 13, 11, 2, 5, 23),
        new GdiTmRow(16, 20, 16, 4, 7),
        new GdiTmRow(24, 29, 23, 6, 11),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) Tahoma = (VariableSwiss, new[]
    {
        new GdiTmRow(11, 13, 11, 2, 5, 27),
        new GdiTmRow(16, 19, 16, 3, 7),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) Arial = (VariableSwiss, new[]
    {
        new GdiTmRow(13, 16, 13, 3, 6, 35),
        new GdiTmRow(16, 18, 15, 3, 7, 0, 1),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) MicrosoftYaHei = (VariableSwiss, new[]
    {
        new GdiTmRow(12, 17, 13, 4, 6, 17),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) SimSun = (0x07, new[]
    {
        new GdiTmRow(12, 12, 10, 2, 6, 12, 2),
        new GdiTmRow(16, 16, 14, 2, 8, 0, 2),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) MalgunGothic = (VariableSwiss, new[]
    {
        new GdiTmRow(12, 15, 12, 3, 6, 26),
        new GdiTmRow(16, 21, 17, 4, 7),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) PMingLiU = (VariableSwiss, new[]
    {
        new GdiTmRow(12, 15, 12, 3, 5, 32),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) MeiryoUI = (VariableSwiss, new[]
    {
        new GdiTmRow(12, 15, 12, 3, 5, 32),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) JhengHeiUI = (VariableSwiss, new[]
    {
        new GdiTmRow(12, 15, 12, 3, 6, 17),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) CourierNew = (FixedModern, new[]
    {
        new GdiTmRow(10, 12, 9, 3, 6),
        new GdiTmRow(12, 15, 12, 3, 7),
    });

    private static readonly (byte Pitch, GdiTmRow[] Rows) LucidaConsole = (FixedModern, new[]
    {
        new GdiTmRow(9, 9, 7, 2, 5),
    });

    private static (byte Pitch, GdiTmRow[] Rows)? ForFamily(string? family)
    {
        if (string.IsNullOrEmpty(family))
            return null;
        // Vertical-face "@" prefix selects the same metrics as the base face.
        if (family[0] == '@')
            family = family.Substring(1);
        if (family.Equals("Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase)) return MicrosoftYaHeiUI;
        if (family.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase)) return SegoeUI;
        if (family.Equals("Microsoft Sans Serif", StringComparison.OrdinalIgnoreCase)) return MicrosoftSansSerif;
        if (family.Equals("Tahoma", StringComparison.OrdinalIgnoreCase)) return Tahoma;
        if (family.Equals("Arial", StringComparison.OrdinalIgnoreCase)) return Arial;
        if (family.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase)) return MicrosoftYaHei;
        if (family.Equals("SimSun", StringComparison.OrdinalIgnoreCase) || family.Equals("NSimSun", StringComparison.OrdinalIgnoreCase)) return SimSun;
        if (family.Equals("Malgun Gothic", StringComparison.OrdinalIgnoreCase)) return MalgunGothic;
        if (family.Equals("PMingLiU", StringComparison.OrdinalIgnoreCase)) return PMingLiU;
        if (family.Equals("Meiryo UI", StringComparison.OrdinalIgnoreCase) || family.Equals("Meiryo", StringComparison.OrdinalIgnoreCase)) return MeiryoUI;
        if (family.Equals("Microsoft JhengHei UI", StringComparison.OrdinalIgnoreCase) || family.Equals("Microsoft JhengHei", StringComparison.OrdinalIgnoreCase)) return JhengHeiUI;
        if (family.Equals("Courier New", StringComparison.OrdinalIgnoreCase)) return CourierNew;
        if (family.Equals("Lucida Console", StringComparison.OrdinalIgnoreCase)) return LucidaConsole;
        return null;
    }

    private static int Round(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static GdiTmRow ScaleRow(GdiTmRow row, float em)
    {
        float k = em / row.Em;
        return new GdiTmRow(
            (int)em,
            Math.Max(1, Round(row.Height * k)),
            Math.Max(1, Round(row.Ascent * k)),
            Math.Max(1, Round(row.Descent * k)),
            Math.Max(1, Round(row.AveWidth * k)),
            row.MaxWidth == 0 ? 0 : Math.Max(1, Round(row.MaxWidth * k)),
            row.ExternalLeading);
    }

    private static GdiTmRow LerpRow(GdiTmRow lo, GdiTmRow hi, float em)
    {
        float t = (em - lo.Em) / (hi.Em - lo.Em);
        int Lerp(int a, int b) => Round(a + (b - a) * t);
        return new GdiTmRow(
            (int)em,
            Math.Max(1, Lerp(lo.Height, hi.Height)),
            Math.Max(1, Lerp(lo.Ascent, hi.Ascent)),
            Math.Max(1, Lerp(lo.Descent, hi.Descent)),
            Math.Max(1, Lerp(lo.AveWidth, hi.AveWidth)),
            lo.MaxWidth == 0 || hi.MaxWidth == 0 ? 0 : Math.Max(1, Lerp(lo.MaxWidth, hi.MaxWidth)),
            t < 0.5f ? lo.ExternalLeading : hi.ExternalLeading);
    }

    /// <summary>
    /// GDI cell metrics for a family at an em size in pixels. Exact at the
    /// probed sizes, lerped between rows, ratio-scaled outside; unknown
    /// families fall back to the Microsoft Sans Serif shape (exact 13/11/2/5
    /// at 11px, the classic WinForms default).
    /// </summary>
    internal static GdiTmRow GetRow(string? family, float emPx)
    {
        float em = Math.Max(1f, emPx);
        var table = ForFamily(family);
        if (table is null)
        {
            // Microsoft Sans Serif 11px shape: 13/11/2/5 (classic default).
            return new GdiTmRow(
                (int)em,
                Math.Max(1, Round(em * 13f / 11f)),
                Math.Max(1, Round(em * 11f / 11f)),
                Math.Max(1, Round(em * 2f / 11f)),
                Math.Max(1, Round(em * 5f / 11f)));
        }

        GdiTmRow[] rows = table.Value.Rows;
        if (em <= rows[0].Em)
            return em == rows[0].Em ? rows[0] : ScaleRow(rows[0], em);
        for (int i = 1; i < rows.Length; i++)
        {
            if (em == rows[i].Em)
                return rows[i];
            if (em < rows[i].Em)
                return LerpRow(rows[i - 1], rows[i], em);
        }

        return ScaleRow(rows[rows.Length - 1], em);
    }

    /// <summary>Pitch/family byte for a family (cf. tmPitchAndFamily).</summary>
    internal static byte GetPitchAndFamily(string? family) => ForFamily(family)?.Pitch ?? VariableSwiss;
}
