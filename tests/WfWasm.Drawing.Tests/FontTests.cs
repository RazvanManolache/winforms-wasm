public sealed class FontTests
{
    [Fact]
    public void Families_Have_Real_Names()
    {
        Assert.Equal("Microsoft Sans Serif", FontFamily.GenericSansSerif.Name);
        Assert.Equal("Times New Roman", FontFamily.GenericSerif.Name);
        Assert.Equal("Courier New", FontFamily.GenericMonospace.Name);
    }

    [Fact]
    public void Ctor_Shapes_Roundtrip()
    {
        var font = new Font("Arial", 12f, FontStyle.Bold | FontStyle.Italic);
        Assert.Equal("Arial", font.Name);
        Assert.Equal("Arial", font.FontFamily.Name);
        Assert.Equal(12f, font.Size);
        Assert.Equal(FontStyle.Bold | FontStyle.Italic, font.Style);
        Assert.True(font.Bold);
        Assert.True(font.Italic);
        Assert.False(font.Underline);
        Assert.False(font.Strikeout);
        Assert.Equal(GraphicsUnit.Point, font.Unit);
    }

    [Fact]
    public void Height_Approximates_Points_At_96dpi()
    {
        Assert.Equal(16, new Font("Arial", 12f).Height);
        Assert.Equal(12, new Font("Arial", 12f, FontStyle.Regular, GraphicsUnit.Pixel).Height);
    }

    [Fact]
    public void Prototype_Ctor_Keeps_Family_And_Size()
    {
        var first = new Font("Arial", 10f, FontStyle.Bold);
        var second = new Font(first, FontStyle.Italic);
        Assert.Equal("Arial", second.Name);
        Assert.Equal(10f, second.Size);
        Assert.Equal(FontStyle.Italic, second.Style);
    }

    [Fact]
    public void Equality_Is_Value_Based()
    {
        Assert.Equal(new Font("Arial", 10f), new Font("Arial", 10f));
        Assert.NotEqual(new Font("Arial", 10f), new Font("Arial", 11f));
        Assert.NotEqual(new Font("Arial", 10f), new Font("Arial", 10f, FontStyle.Bold));
    }

    [Fact]
    public void Clone_Copies_And_Dispose_Does_Not_Throw()
    {
        var font = new Font("Arial", 9f, FontStyle.Underline);
        var clone = (Font)font.Clone();
        Assert.Equal(font, clone);
        clone.Dispose();
        font.Dispose();
    }
}
