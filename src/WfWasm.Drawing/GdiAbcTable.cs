// GENERATED from E:/tmp/gdi-abc-table.json (desktop GetTextExtentPoint32 per-char
// oracle, 2026-10-17). Do not hand-edit: regenerate via E:/tmp/gen-abc-table.py.
// Desktop GDI advances are purely additive per-char integers (verified 9/9 on the
// spell line); canvas outline sums run +4-9% wide on these families, so the GDI
// measure path (MeasureExtent / EM layout) sums this table instead of measuring
// outlines. Coverage: ASCII 32..126 per listed family/size; anything else falls
// back to the tight outline measure.
namespace System.Drawing;

internal static class GdiAbcTable
{
    private sealed record Table(string Family, float EmPx, int[] Widths);

    // Microsoft Sans Serif 8.25pt (em 11.0000px), chars 32..126.
    private static readonly int[] s_Msss825 = new int[]
    {
        3, 3, 5, 7, 6, 8, 6, 2, 3, 3, 4, 6, 3, 3, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 3, 3, 6, 6, 6, 6,
        11, 7, 7, 7, 8, 7, 6, 8, 8, 3, 5, 7, 6, 9, 8, 8,
        7, 8, 8, 7, 7, 8, 7, 11, 7, 7, 7, 3, 5, 3, 6, 6,
        3, 6, 6, 6, 6, 6, 3, 6, 6, 2, 2, 6, 2, 8, 6, 6,
        6, 6, 3, 5, 3, 6, 6, 8, 5, 5, 5, 4, 2, 4, 7,
    };

    // Segoe UI 9.0pt (em 12.0000px), chars 32..126.
    private static readonly int[] s_Segoe9 = new int[]
    {
        3, 3, 5, 7, 6, 10, 10, 3, 4, 4, 5, 8, 3, 5, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 3, 3, 8, 8, 8, 5,
        11, 8, 7, 8, 8, 6, 6, 8, 9, 3, 4, 7, 6, 11, 9, 9,
        7, 9, 7, 6, 7, 8, 7, 11, 7, 7, 7, 4, 5, 4, 8, 5,
        3, 6, 7, 6, 7, 6, 4, 7, 7, 3, 3, 6, 3, 11, 7, 7,
        7, 7, 4, 5, 4, 7, 6, 9, 5, 6, 5, 4, 3, 4, 8,
    };

    // Segoe UI 8.25pt (em 11.0000px), chars 32..126.
    private static readonly int[] s_Segoe825 = new int[]
    {
        3, 3, 4, 7, 6, 9, 9, 3, 3, 3, 5, 8, 3, 4, 3, 4,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 3, 3, 8, 8, 8, 5,
        11, 7, 7, 7, 8, 6, 6, 8, 8, 3, 4, 6, 5, 10, 8, 9,
        6, 8, 7, 6, 5, 8, 7, 11, 6, 5, 6, 3, 4, 3, 8, 5,
        3, 6, 7, 5, 7, 6, 4, 7, 7, 3, 3, 6, 3, 9, 7, 7,
        7, 7, 4, 5, 4, 7, 5, 9, 5, 5, 5, 3, 3, 3, 8,
    };

    // Tahoma 8.0pt (em 10.6667px), chars 32..126.
    private static readonly int[] s_Tahoma8 = new int[]
    {
        3, 4, 4, 8, 6, 11, 7, 2, 4, 4, 6, 8, 4, 4, 4, 4,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 4, 4, 8, 8, 8, 5,
        10, 7, 6, 7, 7, 6, 6, 7, 7, 4, 5, 6, 5, 8, 7, 8,
        6, 8, 7, 6, 6, 7, 6, 10, 6, 6, 6, 4, 4, 4, 8, 6,
        6, 6, 6, 5, 6, 6, 4, 6, 6, 2, 3, 5, 2, 8, 6, 6,
        6, 6, 4, 5, 4, 6, 6, 8, 6, 6, 5, 5, 4, 5, 8,
    };

    // Tahoma 9.0pt (em 12.0000px), chars 32..126.
    private static readonly int[] s_Tahoma9 = new int[]
    {
        4, 4, 5, 9, 7, 12, 8, 3, 5, 5, 7, 8, 4, 4, 4, 5,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 4, 4, 9, 9, 9, 6,
        11, 8, 7, 7, 8, 7, 6, 8, 8, 4, 5, 7, 6, 9, 8, 9,
        7, 9, 7, 7, 8, 8, 8, 12, 7, 8, 7, 5, 5, 5, 9, 7,
        7, 6, 7, 6, 7, 7, 4, 7, 7, 2, 3, 6, 2, 10, 7, 7,
        7, 7, 4, 5, 5, 7, 6, 10, 6, 6, 5, 6, 5, 6, 9,
    };

    // Arial 9.0pt (em 12.0000px), chars 32..126.
    private static readonly int[] s_Arial9 = new int[]
    {
        3, 3, 4, 7, 7, 11, 8, 2, 4, 4, 5, 7, 3, 4, 3, 3,
        7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 3, 3, 7, 7, 7, 7,
        12, 7, 8, 9, 9, 8, 7, 9, 9, 3, 6, 8, 7, 9, 9, 9,
        8, 9, 9, 8, 7, 9, 7, 11, 7, 7, 7, 3, 3, 3, 5, 7,
        4, 7, 7, 6, 7, 7, 3, 7, 7, 3, 3, 6, 3, 11, 7, 7,
        7, 7, 4, 7, 3, 7, 5, 9, 5, 5, 5, 4, 3, 4, 7,
    };

    // MS Shell Dlg 8.0pt (em 10.6667px), chars 32..126.
    private static readonly int[] s_ShellDlg8 = new int[]
    {
        3, 3, 5, 7, 6, 8, 6, 2, 3, 3, 4, 6, 3, 3, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 3, 3, 6, 6, 6, 6,
        11, 7, 7, 7, 8, 7, 6, 8, 8, 3, 5, 7, 6, 9, 8, 8,
        7, 8, 8, 7, 7, 8, 7, 11, 7, 7, 7, 3, 5, 3, 6, 6,
        3, 6, 6, 6, 6, 6, 3, 6, 6, 2, 2, 6, 2, 8, 6, 6,
        6, 6, 3, 5, 3, 6, 6, 8, 5, 5, 5, 4, 2, 4, 7,
    };

    private static readonly Table[] s_tables = new Table[]
    {
        new("Microsoft Sans Serif", 11.0000f, s_Msss825),
        new("Segoe UI", 12.0000f, s_Segoe9),
        new("Segoe UI", 11.0000f, s_Segoe825),
        new("Tahoma", 10.6667f, s_Tahoma8),
        new("Tahoma", 12.0000f, s_Tahoma9),
        new("Arial", 12.0000f, s_Arial9),
        new("MS Shell Dlg", 10.6667f, s_ShellDlg8),
    };

    private static int[]? Find(string? family, float emPx)
    {
        if (string.IsNullOrEmpty(family))
            return null;
        foreach (Table t in s_tables)
        {
            if (t.Family.Equals(family, System.StringComparison.OrdinalIgnoreCase)
                && System.Math.Abs(t.EmPx - emPx) < 0.01f)
                return t.Widths;
        }

        return null;
    }

    internal static bool TryGetAdvance(string? family, float emPx, char c, out int width)
    {
        width = 0;
        int[]? w = Find(family, emPx);
        if (w is null || c < 32 || c > 126)
            return false;
        width = w[c - 32];
        return true;
    }

    internal readonly record struct GdiWordRun(string Word, int XOffset);

    // Word-break plan for GDI paint: words with oracle-advance x offsets
    // (spaces advance the offset but produce no run). The painter draws
    // each word at lineX + XOffset so ink lands on desktop advances
    // instead of browser-shaped whole-line advances. False unless the
    // whole line is tabled ASCII with at least one space (single words,
    // empty, whitespace-only, tabs, newlines, CJK and unknown families
    // keep the whole-line blit).
    internal static bool TryPlanWords(string? family, float emPx, string text, out GdiWordRun[] runs, out int total)
    {
        runs = [];
        total = 0;
        if (string.IsNullOrEmpty(text) || text.IndexOf(' ') < 0)
            return false;
        int[]? w = Find(family, emPx);
        if (w is null)
            return false;
        var list = new List<GdiWordRun>();
        int x = 0, i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == ' ')
            {
                x += w[0];
                i++;
                continue;
            }
            if (c < 32 || c > 126)
                return false;
            int j = i;
            while (j < text.Length && text[j] != ' ')
            {
                if (text[j] < 32 || text[j] > 126)
                    return false;
                j++;
            }
            list.Add(new GdiWordRun(text.Substring(i, j - i), x));
            for (int k = i; k < j; k++)
                x += w[text[k] - 32];
            i = j;
        }
        if (list.Count == 0)
            return false;
        runs = [.. list];
        total = x;
        return true;
    }

    internal static bool TryMeasureString(string? family, float emPx, string text, out int width)
    {
        width = 0;
        int[]? w = Find(family, emPx);
        if (w is null || text is null)
            return false;
        if (text.Length == 0)
        {
            width = 0;
            return true;
        }
        int sum = 0;
        foreach (char c in text)
        {
            if (c < 32 || c > 126)
                return false;
            sum += w[c - 32];
        }

        width = sum;
        return true;
    }
}
