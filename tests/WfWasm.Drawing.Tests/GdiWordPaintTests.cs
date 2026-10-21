// Word-break plans for GDI paint: words with oracle-advance x offsets so
// ink lands on desktop advances instead of browser-shaped whole-line
// advances (eval6: MS end +8, RTC end -12 — measure-only cannot move ink).
extern alias WfDraw;

using WfAbc = WfDraw::System.Drawing.GdiAbcTable;

namespace WfWasm.Drawing.Tests;

public sealed class GdiWordPaintTests
{
    [Fact]
    public void Msss825_SpellLine_PlansOracleOffsets()
    {
        const string line = "The jamped dog";
        Assert.True(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, " ", out int sp));
        Assert.True(WfAbc.TryPlanWords("Microsoft Sans Serif", 11f, line,
            out var runs, out int total));
        Assert.Equal(3, runs.Length);
        Assert.Equal("The", runs[0].Word);
        Assert.Equal(0, runs[0].XOffset);
        Assert.Equal("jamped", runs[1].Word);
        Assert.Equal(19 + sp, runs[1].XOffset);
        Assert.Equal("dog", runs[2].Word);
        Assert.Equal(19 + sp + 34 + sp, runs[2].XOffset);
        // Plan total coincides with the tabled measure (paint≡measure).
        Assert.True(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, line, out int w));
        Assert.Equal(w, total);
        Assert.Equal(19 + sp + 34 + sp + 18, total);
    }

    [Fact]
    public void Segoe9_SpellLine_PlansOracleOffsets()
    {
        const string line = "The jamped dog";
        Assert.True(WfAbc.TryMeasureString("Segoe UI", 12f, " ", out int sp));
        Assert.True(WfAbc.TryPlanWords("Segoe UI", 12f, line,
            out var runs, out int total));
        Assert.Equal(3, runs.Length);
        Assert.Equal(20 + sp, runs[1].XOffset);
        Assert.Equal(20 + sp + 40 + sp, runs[2].XOffset);
        Assert.True(WfAbc.TryMeasureString("Segoe UI", 12f, line, out int w));
        Assert.Equal(w, total);
    }

    [Fact]
    public void Leading_And_Double_Spaces_Advance_Offsets()
    {
        Assert.True(WfAbc.TryPlanWords("Microsoft Sans Serif", 11f, "  dog",
            out var runs, out int total));
        Assert.Single(runs);
        Assert.Equal("dog", runs[0].Word);
        Assert.True(WfAbc.TryMeasureString("Microsoft Sans Serif", 11f, "  ", out int sp2));
        Assert.Equal(sp2, runs[0].XOffset);
        Assert.Equal(sp2 + 18, total);
    }

    [Theory]
    [InlineData("dog")]          // single word: same raster either way
    [InlineData("")]             // empty
    [InlineData("   ")]          // whitespace-only
    [InlineData("a\tb")]         // tab stops need EM context
    [InlineData("a\nb")]         // multiline keeps whole blit
    [InlineData("a\rb")]         // multiline keeps whole blit
    [InlineData("caf\u00e9 x")]  // untabled char
    public void Fallback_Lines_DeclinePlan(string line)
    {
        Assert.False(WfAbc.TryPlanWords("Microsoft Sans Serif", 11f, line,
            out _, out _));
    }

    [Fact]
    public void Unknown_Family_DeclinesPlan()
    {
        Assert.False(WfAbc.TryPlanWords("Comic Sans MS", 12f, "The dog",
            out _, out _));
    }
}
