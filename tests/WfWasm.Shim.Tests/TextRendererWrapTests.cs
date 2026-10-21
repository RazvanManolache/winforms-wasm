extern alias WfDraw;

using WfWasm.Shim;

/// <summary>
/// Telerik BookShelf tile text: TPF measures with
/// MeasureText(text, font, Size(width, int.MaxValue), WordBreak|...) and paints
/// with DrawText(graphics, text, font, rect, fore, flags). Wrapped titles must
/// measure multi-line (native tiles show 3-4 wrapped rows under each cover;
/// browser tiles were cover-only).
/// </summary>
public sealed class TextRendererWrapTests
{
    private const string Title = "The Secret by Rhonda Byrne (2006, Hardcover)";

    private static DrawingFont TileFont() => new DrawingFont("Segoe UI", 12f);

    private static DrawingSize SingleLine(string text, DrawingFont font) =>
        TextRenderer.MeasureText(text, font,
            new DrawingSize(90, int.MaxValue), TextFormatFlags.SingleLine);

    [Fact]
    public void MeasureText_WordBreak_Wraps_Title_Within_Width()
    {
        using DrawingFont font = TileFont();
        DrawingSize wrapped = TextRenderer.MeasureText(Title, font,
            new DrawingSize(90, int.MaxValue), TextFormatFlags.WordBreak);
        DrawingSize single = SingleLine(Title, font);
        Assert.True(wrapped.Width <= 90, $"wrapped width {wrapped.Width} exceeds 90");
        Assert.True(wrapped.Height > single.Height,
            $"wrapped height {wrapped.Height} not taller than single-line {single.Height}");
    }

    [Fact]
    public void MeasureText_SingleLine_Stays_One_Row()
    {
        using DrawingFont font = TileFont();
        DrawingSize single = SingleLine("Hi", font);
        DrawingSize wrapped = TextRenderer.MeasureText(Title, font,
            new DrawingSize(90, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.SingleLine);
        Assert.Equal(single.Height, wrapped.Height);
    }

    [Fact]
    public void DrawText_Census_Records_Bounds_Call()
    {
        using var bmp = new WfDraw::System.Drawing.Bitmap(60, 20);
        using var g = WfDraw::System.Drawing.Graphics.FromImage(bmp);
        using DrawingFont font = TileFont();
        try
        {
            TextRenderer.DrawText(g, "Hi", font,
                new DrawingRectangle(0, 0, 50, 18), WfDraw::System.Drawing.Color.Black,
                TextFormatFlags.WordBreak);
        }
        catch (System.NotSupportedException)
        {
            // Headless: no browser text raster; the census records first.
        }

        string census = TextRenderer.DumpCensus();
        Assert.Contains("draws=", census);
        Assert.Contains("Hi", census);
    }
}
