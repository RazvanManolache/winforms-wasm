namespace System.Drawing;

// DirectWrite layout -> browser text raster, for the Paint.NET
// DrawTextLayout rewrite: the layout provider (PaintDotNet.UI.Media
// .TextLayout via its TextFormat base) carries Text, FontFamilyName,
// FontSize, FontStyle, FontWeight and MaxWidth, all read by name so
// this assembly never binds the PDN universe. Anything unreadable or
// unrasterizable answers null and the caller keeps its embedded 5x7
// fallback. The DTO exposes public fields (not properties) so
// rewriter-emitted IL needs only ldfld references.
public sealed class LayoutRaster
{
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Rgba;

    public LayoutRaster(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }
}

public static class TextLayoutRaster
{
    // DIPs (1/96") to points (1/72"): the css builder turns points
    // back into device px at the given dpi, so identity at 96.
    private const double DipToPoint = 72.0 / 96.0;

    // Diagnostic-only record of the last UNEXPECTED raster failure
    // (font construction / engine rasterization throwing). Expected
    // nulls (unreadable input below) never set it, and the null
    // fallback contract is unchanged either way. Recorded only when
    // the WfWasm.TextDiagnostics switch is on; the host reads it for
    // triage, never for control flow.
    internal static string? LastFault { get; private set; }

    public static LayoutRaster? TryRasterLayout(object? layout, float dpi, int maxWidth)
    {
        LastFault = null;
        try
        {
            if (layout is null || dpi <= 0 || maxWidth <= 0)
                return null;
            Type lt = layout.GetType();
            string? text = GetString(lt, layout, "get_Text");
            if (string.IsNullOrEmpty(text))
                return null;
            string? family = GetString(lt, layout, "get_FontFamilyName");
            if (string.IsNullOrWhiteSpace(family))
                family = "sans-serif";
            double size = GetDouble(lt, layout, "get_FontSize");
            if (!(size > 0) || double.IsNaN(size) || double.IsInfinity(size))
                return null;
            FontStyle style = FontStyle.Regular;
            if (GetInt(lt, layout, "get_FontWeight") >= 700)
                style |= FontStyle.Bold;
            string fontStyle = GetName(lt, layout, "get_FontStyle");
            if (fontStyle == "Italic" || fontStyle == "Oblique")
                style |= FontStyle.Italic;
            int maxW = maxWidth;
            double layoutMax = GetDouble(lt, layout, "get_MaxWidth");
            if (!double.IsNaN(layoutMax) && layoutMax > 0 && layoutMax < maxW)
                maxW = (int)layoutMax;
            if (maxW <= 0)
                return null;
            using var font = new Font(family, (float)(size * DipToPoint), style);
            BrowserText.Raster? r = BrowserText.TryRaster(text, font, dpi, maxW);
            if (r is null)
                return null;
            return new LayoutRaster(r.Width, r.Height, r.Rgba);
        }
        catch (Exception ex)
        {
            if (System.AppContext.TryGetSwitch("WfWasm.TextDiagnostics", out bool diag) && diag)
                LastFault = ex.GetType().FullName + ": " + ex.Message;
            return null;
        }
    }

    private static string? GetString(Type lt, object layout, string getter)
    {
        var m = lt.GetMethod(getter, Type.EmptyTypes);
        if (m is null || m.ReturnType != typeof(string))
            return null;
        return (string?)m.Invoke(layout, null);
    }

    private static double GetDouble(Type lt, object layout, string getter)
    {
        try
        {
            var m = lt.GetMethod(getter, Type.EmptyTypes);
            if (m is null)
                return double.NaN;
            return Convert.ToDouble(m.Invoke(layout, null));
        }
        catch
        {
            return double.NaN;
        }
    }

    private static int GetInt(Type lt, object layout, string getter)
    {
        try
        {
            var m = lt.GetMethod(getter, Type.EmptyTypes);
            if (m is null)
                return 0;
            return Convert.ToInt32(m.Invoke(layout, null));
        }
        catch
        {
            return 0;
        }
    }

    private static string GetName(Type lt, object layout, string getter)
    {
        try
        {
            var m = lt.GetMethod(getter, Type.EmptyTypes);
            if (m is null)
                return string.Empty;
            return m.Invoke(layout, null)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
