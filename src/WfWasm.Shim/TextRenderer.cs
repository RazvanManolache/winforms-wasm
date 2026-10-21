extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// GDI text painter (cf. System.Windows.Forms.TextRenderer). Measuring works
/// headless through the CPU rasterizer's font metrics; drawing routes to
/// <c>Graphics.DrawString</c> and therefore throws
/// <c>NotSupportedException</c> on bitmap targets until the text-raster
/// milestone lands (same contract as every other text path in this stack).
/// </summary>
public static class TextRenderer
{
    private static Draw.Font DefaultFont(Draw.Font? font) =>
        font ?? new Draw.Font("Microsoft Sans Serif", 8.25f);

    // Desktop DrawText parity: WordBreak without SingleLine wraps at the
    // layout width; anything else (Default, SingleLine, ellipsis) lays a
    // single row (embedded \n still breaks). The old code dropped flags
    // entirely, so SingleLine measured 3 rows and wrapped paints never
    // honored ellipsis-less single-row callers.
    private static bool WrapsRows(TextFormatFlags flags) =>
        (flags & TextFormatFlags.WordBreak) != 0 && (flags & TextFormatFlags.SingleLine) == 0;

    private static Draw.StringFormat WrapFormat(TextFormatFlags flags) =>
        WrapsRows(flags) ? new Draw.StringFormat()
            : new Draw.StringFormat(Draw.StringFormatFlags.NoWrap);

    // Bounded session census for tile-text-class diagnosis (which
    // DrawText calls arrive, with what bounds/flags): counters plus a
    // ring of the last 8 calls. Surfaced via DumpCensus (host tile dump
    // appends it). Bounded and allocation-light by design.
    private static int s_drawCalls;
    private static int s_drawEmptyBounds;
    private static readonly string[] s_lastDraws = new string[8];
    private static int s_lastDrawIndex;

    private static void NoteDraw(string text, Draw.Rectangle bounds, TextFormatFlags flags)
    {
        s_drawCalls++;
        if (bounds.Width <= 0 || bounds.Height <= 0) s_drawEmptyBounds++;
        string preview = text.Length > 24 ? text.Substring(0, 24) : text;
        s_lastDraws[s_lastDrawIndex++ % 8] =
            $"'{preview.Replace('\r', ' ').Replace('\n', ' ')}' {bounds.Width}x{bounds.Height} f={(int)flags}";
    }

    /// <summary>Session DrawText census (diagnostic): call counts plus the
    /// last-8 call ring (text preview, bounds, flags).</summary>
    public static string DumpCensus()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("draws=").Append(s_drawCalls).Append(" emptyBounds=").Append(s_drawEmptyBounds).Append(';');
        for (int i = 0; i < 8; i++)
        {
            string? entry = s_lastDraws[(s_lastDrawIndex + i) % 8];
            if (entry is not null) sb.Append('[').Append(entry).Append(']');
        }

        return sb.ToString();
    }

    private static Draw.Size MeasureCore(string text, Draw.Font? font, Draw.Size proposedSize) =>
        MeasureCore(text, font, proposedSize, TextFormatFlags.Default);

    private static Draw.Size MeasureCore(string text, Draw.Font? font, Draw.Size proposedSize, TextFormatFlags flags)
    {
        using var bitmap = new Draw.Bitmap(1, 1);
        using var graphics = Draw.Graphics.FromImage(bitmap);
        using Draw.StringFormat format = WrapFormat(flags);
        Draw.SizeF size = proposedSize.Width > 0 || proposedSize.Height > 0
            ? graphics.MeasureString(text, DefaultFont(font), new Draw.SizeF(proposedSize.Width, proposedSize.Height), format)
            : graphics.MeasureString(text, DefaultFont(font), new Draw.SizeF(0, 0), format);
        return Draw.Size.Ceiling(size);
    }

    private static void DrawCore(Draw.IDeviceContext dc, string text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, Draw.Color backColor) =>
        DrawCore(dc, text, font, bounds, foreColor, backColor, TextFormatFlags.Default);

    private static void DrawCore(Draw.IDeviceContext dc, string text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, Draw.Color backColor, TextFormatFlags flags)
    {
        if (dc is not Draw.Graphics graphics)
            throw new ArgumentException("Device context is not a canvas graphics.", nameof(dc));
        NoteDraw(text, bounds, flags);
        if (backColor.A == 255)
        {
            using var back = new Draw.SolidBrush(backColor);
            graphics.FillRectangle(back, bounds);
        }
        using var fore = new Draw.SolidBrush(foreColor);
        using Draw.StringFormat format = WrapFormat(flags);
        graphics.DrawString(text, DefaultFont(font), fore, bounds, format);
    }

    private static void DrawCore(Draw.IDeviceContext dc, string text, Draw.Font? font, Draw.Point point, Draw.Color foreColor, Draw.Color backColor, bool fillBack)
    {
        if (dc is not Draw.Graphics graphics)
            throw new ArgumentException("Device context is not a canvas graphics.", nameof(dc));
        if (fillBack && backColor.A == 255)
        {
            Draw.Size size = MeasureCore(text, font, Draw.Size.Empty);
            using var back = new Draw.SolidBrush(backColor);
            graphics.FillRectangle(back, new Draw.Rectangle(point, size));
        }
        using var brush = new Draw.SolidBrush(foreColor);
        graphics.DrawString(text, DefaultFont(font), brush, point);
    }

    /// <summary>Measures text with no constraints (cf. MeasureText(string, Font)).</summary>
    public static Draw.Size MeasureText(string? text, Draw.Font? font) =>
        MeasureCore(text ?? string.Empty, font, Draw.Size.Empty);

    /// <summary>Measures a span with no constraints (cf. MeasureText(ReadOnlySpan, Font)).</summary>
    public static Draw.Size MeasureText(ReadOnlySpan<char> text, Draw.Font? font) =>
        MeasureCore(text.ToString(), font, Draw.Size.Empty);

    /// <summary>Measures text within a proposed size (cf. MeasureText(string, Font, Size)).</summary>
    public static Draw.Size MeasureText(string? text, Draw.Font? font, Draw.Size proposedSize) =>
        MeasureCore(text ?? string.Empty, font, proposedSize);

    /// <summary>Measures a span within a proposed size (cf. MeasureText(ReadOnlySpan, Font, Size)).</summary>
    public static Draw.Size MeasureText(ReadOnlySpan<char> text, Draw.Font? font, Draw.Size proposedSize) =>
        MeasureCore(text.ToString(), font, proposedSize);

    /// <summary>Measures text within a proposed size with flags (cf. MeasureText(string, Font, Size, TextFormatFlags)).</summary>
    public static Draw.Size MeasureText(string? text, Draw.Font? font, Draw.Size proposedSize, TextFormatFlags flags) =>
        MeasureCore(text ?? string.Empty, font, proposedSize, flags);

    /// <summary>Measures a span within a proposed size with flags (cf. MeasureText(ReadOnlySpan, Font, Size, TextFormatFlags)).</summary>
    public static Draw.Size MeasureText(ReadOnlySpan<char> text, Draw.Font? font, Draw.Size proposedSize, TextFormatFlags flags) =>
        MeasureCore(text.ToString(), font, proposedSize, flags);

    /// <summary>Measures text on a device context (cf. MeasureText(IDeviceContext, string, Font)).</summary>
    public static Draw.Size MeasureText(Draw.IDeviceContext dc, string? text, Draw.Font? font) =>
        MeasureCore(text ?? string.Empty, font, Draw.Size.Empty);

    /// <summary>Measures a span on a device context (cf. MeasureText(IDeviceContext, ReadOnlySpan, Font)).</summary>
    public static Draw.Size MeasureText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font) =>
        MeasureCore(text.ToString(), font, Draw.Size.Empty);

    /// <summary>Measures text on a device context within a size (cf. MeasureText(IDeviceContext, string, Font, Size)).</summary>
    public static Draw.Size MeasureText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Size proposedSize) =>
        MeasureCore(text ?? string.Empty, font, proposedSize);

    /// <summary>Measures a span on a device context within a size (cf. MeasureText(IDeviceContext, ReadOnlySpan, Font, Size)).</summary>
    public static Draw.Size MeasureText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Size proposedSize) =>
        MeasureCore(text.ToString(), font, proposedSize);

    /// <summary>Measures text on a device context with flags (cf. MeasureText(IDeviceContext, string, Font, Size, TextFormatFlags)).</summary>
    public static Draw.Size MeasureText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Size proposedSize, TextFormatFlags flags) =>
        MeasureCore(text ?? string.Empty, font, proposedSize, flags);

    /// <summary>Measures a span on a device context with flags (cf. MeasureText(IDeviceContext, ReadOnlySpan, Font, Size, TextFormatFlags)).</summary>
    public static Draw.Size MeasureText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Size proposedSize, TextFormatFlags flags) =>
        MeasureCore(text.ToString(), font, proposedSize, flags);

    /// <summary>Draws text at a point (cf. DrawText(IDeviceContext, string, Font, Point, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor) =>
        DrawCore(dc, text ?? string.Empty, font, pt, foreColor, Draw.Color.Empty, false);

    /// <summary>Draws a span at a point (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Point, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor) =>
        DrawCore(dc, text.ToString(), font, pt, foreColor, Draw.Color.Empty, false);

    /// <summary>Draws text at a point over a background (cf. DrawText(IDeviceContext, string, Font, Point, Color, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor, Draw.Color backColor) =>
        DrawCore(dc, text ?? string.Empty, font, pt, foreColor, backColor, true);

    /// <summary>Draws a span at a point over a background (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Point, Color, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor, Draw.Color backColor) =>
        DrawCore(dc, text.ToString(), font, pt, foreColor, backColor, true);

    /// <summary>Draws text at a point with flags (cf. DrawText(IDeviceContext, string, Font, Point, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor, TextFormatFlags flags) =>
        DrawCore(dc, text ?? string.Empty, font, pt, foreColor, Draw.Color.Empty, false);

    /// <summary>Draws a span at a point with flags (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Point, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor, TextFormatFlags flags) =>
        DrawCore(dc, text.ToString(), font, pt, foreColor, Draw.Color.Empty, false);

    /// <summary>Draws text at a point over a background with flags (cf. DrawText(IDeviceContext, string, Font, Point, Color, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor, Draw.Color backColor, TextFormatFlags flags) =>
        DrawCore(dc, text ?? string.Empty, font, pt, foreColor, backColor, true);

    /// <summary>Draws a span at a point over a background with flags (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Point, Color, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Point pt, Draw.Color foreColor, Draw.Color backColor, TextFormatFlags flags) =>
        DrawCore(dc, text.ToString(), font, pt, foreColor, backColor, true);

    /// <summary>Draws text in bounds (cf. DrawText(IDeviceContext, string, Font, Rectangle, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor) =>
        DrawCore(dc, text ?? string.Empty, font, bounds, foreColor, Draw.Color.Empty);

    /// <summary>Draws a span in bounds (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Rectangle, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor) =>
        DrawCore(dc, text.ToString(), font, bounds, foreColor, Draw.Color.Empty);

    /// <summary>Draws text in bounds over a background (cf. DrawText(IDeviceContext, string, Font, Rectangle, Color, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, Draw.Color backColor) =>
        DrawCore(dc, text ?? string.Empty, font, bounds, foreColor, backColor);

    /// <summary>Draws a span in bounds over a background (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Rectangle, Color, Color)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, Draw.Color backColor) =>
        DrawCore(dc, text.ToString(), font, bounds, foreColor, backColor);

    /// <summary>Draws text in bounds with flags (cf. DrawText(IDeviceContext, string, Font, Rectangle, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, TextFormatFlags flags) =>
        DrawCore(dc, text ?? string.Empty, font, bounds, foreColor, Draw.Color.Empty, flags);

    /// <summary>Draws a span in bounds with flags (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Rectangle, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, TextFormatFlags flags) =>
        DrawCore(dc, text.ToString(), font, bounds, foreColor, Draw.Color.Empty, flags);

    /// <summary>Draws text in bounds over a background with flags (cf. DrawText(IDeviceContext, string, Font, Rectangle, Color, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, string? text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, Draw.Color backColor, TextFormatFlags flags) =>
        DrawCore(dc, text ?? string.Empty, font, bounds, foreColor, backColor, flags);

    /// <summary>Draws a span in bounds over a background with flags (cf. DrawText(IDeviceContext, ReadOnlySpan, Font, Rectangle, Color, Color, TextFormatFlags)).</summary>
    public static void DrawText(Draw.IDeviceContext dc, ReadOnlySpan<char> text, Draw.Font? font, Draw.Rectangle bounds, Draw.Color foreColor, Draw.Color backColor, TextFormatFlags flags) =>
        DrawCore(dc, text.ToString(), font, bounds, foreColor, backColor, flags);
}
