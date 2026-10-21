extern alias WfDraw;
using GdiTextMetrics = WfDraw::System.Drawing.GdiTextMetrics;
using GdiTmRow = WfDraw::System.Drawing.GdiTmRow;

public sealed class GdiTextMetricsTests
{
    // Oracle: GetTextMetricsW on Windows GDI (lfHeight=-em). tmHeight is what
    // AutoScaleMode.Font divides the designer dims by, so these ints must be
    // exact (Microsoft YaHei UI 12px -> 17 un-squashes the todo form).
    [Theory]
    [InlineData("Microsoft YaHei UI", 9, 14, 11, 3, 4, 0, 0)]
    [InlineData("Microsoft YaHei UI", 12, 17, 13, 4, 6, 17, 0)]
    [InlineData("Microsoft YaHei UI", 16, 21, 17, 4, 8, 0, 0)]
    [InlineData("Microsoft YaHei UI", 24, 31, 25, 6, 12, 0, 0)]
    [InlineData("Microsoft YaHei UI", 40, 52, 42, 10, 19, 0, 0)]
    [InlineData("Microsoft YaHei", 12, 17, 13, 4, 6, 17, 0)]
    [InlineData("SimSun", 12, 12, 10, 2, 6, 12, 2)]
    [InlineData("SimSun", 16, 16, 14, 2, 8, 0, 2)]
    [InlineData("PMingLiU", 12, 15, 12, 3, 5, 32, 0)]
    [InlineData("Malgun Gothic", 12, 15, 12, 3, 6, 26, 0)]
    [InlineData("Malgun Gothic", 16, 21, 17, 4, 7, 0, 0)]
    [InlineData("Meiryo UI", 12, 15, 12, 3, 5, 32, 0)]
    [InlineData("Microsoft JhengHei UI", 12, 15, 12, 3, 6, 17, 0)]
    [InlineData("Segoe UI", 9, 12, 10, 2, 5, 23, 0)]
    [InlineData("Segoe UI", 12, 15, 12, 3, 6, 31, 0)]
    [InlineData("Segoe UI", 16, 21, 17, 4, 9, 0, 0)]
    [InlineData("Segoe UI", 24, 32, 26, 6, 13, 0, 0)]
    [InlineData("Segoe UI", 40, 54, 43, 11, 22, 0, 0)]
    [InlineData("Microsoft Sans Serif", 8, 9, 7, 2, 4, 16, 0)]
    [InlineData("Microsoft Sans Serif", 11, 13, 11, 2, 5, 23, 0)]
    [InlineData("Microsoft Sans Serif", 16, 20, 16, 4, 7, 0, 0)]
    [InlineData("Microsoft Sans Serif", 24, 29, 23, 6, 11, 0, 0)]
    [InlineData("Tahoma", 11, 13, 11, 2, 5, 27, 0)]
    [InlineData("Tahoma", 16, 19, 16, 3, 7, 0, 0)]
    [InlineData("Arial", 13, 16, 13, 3, 6, 35, 0)]
    [InlineData("Arial", 16, 18, 15, 3, 7, 0, 1)]
    [InlineData("Courier New", 10, 12, 9, 3, 6, 0, 0)]
    [InlineData("Courier New", 12, 15, 12, 3, 7, 0, 0)]
    [InlineData("Lucida Console", 9, 9, 7, 2, 5, 0, 0)]
    public void Probed_Rows_Are_Exact(string family, int em, int h, int asc, int desc, int aveW, int maxW, int extLead)
    {
        GdiTmRow row = GdiTextMetrics.GetRow(family, em);
        Assert.Equal(h, row.Height);
        Assert.Equal(asc, row.Ascent);
        Assert.Equal(desc, row.Descent);
        Assert.Equal(aveW, row.AveWidth);
        Assert.Equal(maxW, row.MaxWidth);
        Assert.Equal(extLead, row.ExternalLeading);
    }

    [Theory]
    [InlineData("Microsoft YaHei UI", 0x27)]
    [InlineData("Segoe UI", 0x27)]
    [InlineData("Microsoft Sans Serif", 0x27)]
    [InlineData("Tahoma", 0x27)]
    [InlineData("Arial", 0x27)]
    [InlineData("SimSun", 0x07)]
    [InlineData("Courier New", 0x36)]
    [InlineData("Lucida Console", 0x36)]
    [InlineData("Trebuchet MS", 0x27)] // unknown -> variable fallback
    public void Pitch_And_Family_Matches_Oracle(string family, byte pitch)
    {
        Assert.Equal(pitch, GdiTextMetrics.GetPitchAndFamily(family));
    }

    [Fact]
    public void Unknown_Family_Falls_Back_To_Sans_Serif_Shape()
    {
        // Exact at the classic 11px default so MSSS-8.25pt apps keep scaling 1.0.
        GdiTmRow row = GdiTextMetrics.GetRow("Trebuchet MS", 11);
        Assert.Equal(13, row.Height);
        Assert.Equal(11, row.Ascent);
        Assert.Equal(2, row.Descent);
        Assert.Equal(5, row.AveWidth);
    }

    [Fact]
    public void Vertical_Face_Prefix_Uses_Base_Face()
    {
        GdiTmRow row = GdiTextMetrics.GetRow("@SimSun", 12);
        Assert.Equal(12, row.Height);
        Assert.Equal(2, row.ExternalLeading);
    }

    [Fact]
    public void Heights_Are_Monotone_Between_Rows()
    {
        int prev = 0;
        for (float em = 9; em <= 40; em += 1)
        {
            int h = GdiTextMetrics.GetRow("Microsoft YaHei UI", em).Height;
            Assert.True(h >= prev, $"em {em}: height {h} < {prev}");
            prev = h;
        }
    }
}
