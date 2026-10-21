// Oracle tests for GdiAbcTable: desktop GetTextExtentPoint32 per-char widths
// (E:/tmp/gdi-abc-table.json). Desktop GDI advances are purely additive
// per-char integers (verified 9/9 on the spell line); canvas outline sums
// run +4-9% wide, so the GDI measure path sums this table instead.
extern alias WfDraw;

using WfAbc = WfDraw::System.Drawing.GdiAbcTable;

namespace WfWasm.Drawing.Tests;

public sealed class GdiAbcTableTests
{
    [Theory]
    [InlineData("The", 19)]
    [InlineData("quick", 26)]
    [InlineData("brown", 29)]
    [InlineData("fox", 14)]
    [InlineData("jamped", 34)]
    [InlineData("voer", 21)]
    [InlineData("the", 15)]
    [InlineData("lazee", 25)]
    [InlineData("dog", 18)]
    public void Msss825_SpellWords_MatchGdiExtents(string word, int expected)
    {
        Assert.True(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, word, out int w));
        Assert.Equal(expected, w);
    }

    [Fact]
    public void Msss825_FullLine_MatchesGdiExtent()
    {
        const string line = "The quick brown fox jamped voer the lazee dog";
        Assert.True(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, line, out int w));
        Assert.Equal(225, w);
    }

    [Theory]
    [InlineData("The", 20)]
    [InlineData("jamped", 40)]
    [InlineData("dog", 21)]
    public void Segoe9_SpotChecks_MatchGdiExtents(string word, int expected)
    {
        Assert.True(WfAbc.TryMeasureString("Segoe UI", 12f, word, out int w));
        Assert.Equal(expected, w);
    }

    [Fact]
    public void FamilyMatch_IsCaseInsensitive()
    {
        Assert.True(WfAbc.TryMeasureString("microsoft sans serif", 11f, "dog", out int w));
        Assert.Equal(18, w);
    }

    [Fact]
    public void EmPxMatch_AllowsTinyTolerance()
    {
        Assert.True(WfAbc.TryGetAdvance("Segoe UI", 12.001f, 'A', out int w));
        Assert.Equal(8, w);
        Assert.False(WfAbc.TryGetAdvance("Segoe UI", 11.5f, 'A', out _));
    }

    [Fact]
    public void UntableFamily_Size_Or_Char_FallsBack()
    {
        Assert.False(WfAbc.TryMeasureString("Comic Sans MS", 12f, "dog", out _));
        Assert.False(WfAbc.TryMeasureString("Microsoft Sans Serif", 16f, "dog", out _));
        Assert.False(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, "caf\u00e9", out _));
        Assert.False(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, "a\nb", out _));
        Assert.False(WfAbc.TryMeasureString(null, 11f, "dog", out _));
    }

    [Fact]
    public void Empty_MeasuresZero()
    {
        Assert.True(WfAbc.TryMeasureString("Segoe UI", 12f, "", out int w));
        Assert.Equal(0, w);
    }
}
