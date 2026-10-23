using WfWasm.Shim;

/// <summary>
/// GDI parity: TextRenderer.MeasureText includes trailing spaces
/// (DT_CALCRECT does); Graphics.MeasureString drops them. Without the
/// add-back, space-carrying runs ("word ") measure as their trimmed word
/// and positioned words butt together. Bare whitespace keeps the
/// whitespace-only branch (TPF gap elements unchanged).
/// </summary>
public sealed class TextRendererTrailingSpaceTests
{
    [Fact]
    public void MeasureText_TrailingSpace_Widens_Run()
    {
        using DrawingFont font = new("Segoe UI", 9f);
        DrawingSize plain = TextRenderer.MeasureText("word", font, DrawingSize.Empty);
        DrawingSize trailed = TextRenderer.MeasureText("word ", font, DrawingSize.Empty);
        Assert.True(trailed.Width > plain.Width,
            $"trailed width {trailed.Width} not wider than plain {plain.Width}");
        Assert.Equal(plain.Height, trailed.Height);
    }

    [Fact]
    public void MeasureText_BareSpace_Stays_Nonzero()
    {
        using DrawingFont font = new("Segoe UI", 9f);
        DrawingSize bare = TextRenderer.MeasureText(" ", font, DrawingSize.Empty);
        Assert.True(bare.Width > 0, $"bare space measured {bare.Width}");
    }

    [Fact]
    public void MeasureText_MultipleTrailingSpaces_Accumulate()
    {
        using DrawingFont font = new("Segoe UI", 9f);
        DrawingSize one = TextRenderer.MeasureText("word ", font, DrawingSize.Empty);
        DrawingSize two = TextRenderer.MeasureText("word  ", font, DrawingSize.Empty);
        Assert.True(two.Width > one.Width,
            $"double-trailed width {two.Width} not wider than single {one.Width}");
    }
}
