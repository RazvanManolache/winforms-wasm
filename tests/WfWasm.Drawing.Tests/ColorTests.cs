// Behavioral tests for WfWasm.Drawing Color/ColorTranslator. Every literal
// below was observed on the real .NET 9 framework (enum-oracle probes).
public sealed class ColorTests
{
    [Fact]
    public void Empty_Shape()
    {
        Assert.True(Color.Empty.IsEmpty);
        Assert.False(Color.Empty.IsKnownColor);
        Assert.False(Color.Empty.IsNamedColor);
        Assert.False(Color.Empty.IsSystemColor);
        Assert.Equal("0", Color.Empty.Name);
        Assert.Equal("Color [Empty]", Color.Empty.ToString());
        Assert.Equal(0, Color.Empty.ToArgb());
    }

    [Fact]
    public void Known_NonSystem_Color()
    {
        Assert.Equal("Red", Color.Red.Name);
        Assert.Equal("Color [Red]", Color.Red.ToString());
        Assert.True(Color.Red.IsKnownColor);
        Assert.True(Color.Red.IsNamedColor);
        Assert.False(Color.Red.IsSystemColor);
        Assert.Equal(unchecked((int)0xFFFF0000), Color.Red.ToArgb());
        Assert.Equal(255, Color.Red.A);
        Assert.Equal(255, Color.Red.R);
        Assert.Equal(0, Color.Red.G);
        Assert.Equal(0, Color.Red.B);
        Assert.Equal(KnownColor.Red, Color.Red.ToKnownColor());
        Assert.Equal(0f, Color.Red.GetHue());
        Assert.Equal(1f, Color.Red.GetSaturation());
        Assert.Equal(0.5f, Color.Red.GetBrightness());
    }

    [Fact]
    public void Known_System_Color()
    {
        var ab = Color.FromKnownColor(KnownColor.ActiveBorder);
        Assert.True(ab.IsKnownColor);
        Assert.True(ab.IsNamedColor);
        Assert.True(ab.IsSystemColor);
        Assert.Equal("ActiveBorder", ab.Name);
        Assert.Equal("Color [ActiveBorder]", ab.ToString());
        Assert.Equal(unchecked((int)0xFFB4B4B4), ab.ToArgb());
        Assert.Equal(KnownColor.ActiveBorder, ab.ToKnownColor());
        Assert.Equal("MenuHighlight", Color.FromKnownColor(KnownColor.MenuHighlight).Name);
    }

    [Fact]
    public void FromArgb_Forms()
    {
        Assert.Equal(unchecked((int)0xFF010203), Color.FromArgb(1, 2, 3).ToArgb());
        Assert.Equal(unchecked((int)0x0A141E28), Color.FromArgb(10, 20, 30, 40).ToArgb());
        Assert.Equal(unchecked((int)0x0A141E28), Color.FromArgb(0x0A141E28).ToArgb());
        Assert.Equal(unchecked((int)0x80FF0000), Color.FromArgb(0x80, Color.Red).ToArgb());
        Assert.Throws<ArgumentException>(() => Color.FromArgb(300, 0, 0));
        Assert.Throws<ArgumentException>(() => Color.FromArgb(-1, 0, 0, 0));
    }

    [Fact]
    public void Custom_Color_Name_And_String()
    {
        var c = Color.FromArgb(10, 20, 30, 40);
        Assert.Equal("a141e28", c.Name);
        Assert.Equal("Color [A=10, R=20, G=30, B=40]", c.ToString());
        Assert.False(c.IsEmpty);
        Assert.False(c.IsNamedColor);
        Assert.Equal((KnownColor)0, c.ToKnownColor());
    }

    [Fact]
    public void Equality_Compares_State_Not_Just_Argb()
    {
        // Famous quirk: same ARGB, different construction => not equal.
        Assert.True(Color.Red != Color.FromArgb(255, 255, 0, 0));
        Assert.True(Color.Red == Color.Red);
        Assert.True(Color.Red.Equals((object)Color.Red));
        Assert.False(Color.Red.Equals((object)Color.Blue));
        Assert.Equal(Color.Red.GetHashCode(), Color.Red.GetHashCode());
    }

    [Fact]
    public void FromName_Rules()
    {
        Assert.Equal(unchecked((int)0xFFFF0000), Color.FromName("red").ToArgb());
        Assert.True(Color.FromName("red").IsNamedColor);
        var bogus = Color.FromName("banana");
        Assert.Equal(0, bogus.ToArgb());
        Assert.Equal("banana", bogus.Name);
        Assert.False(bogus.IsEmpty);
        Assert.True(bogus.IsNamedColor);
    }

    [Fact]
    public void Translator_Html()
    {
        Assert.Equal(unchecked((int)0xFFFF0000), ColorTranslator.FromHtml("#F00").ToArgb());
        Assert.Equal(unchecked((int)0xFFB4B4B4), ColorTranslator.FromHtml("ActiveBorder").ToArgb());
        Assert.Throws<ArgumentException>(() => ColorTranslator.FromHtml("FF0000"));
        Assert.Throws<ArgumentException>(() => ColorTranslator.FromHtml("banana"));
        Assert.Throws<FormatException>(() => ColorTranslator.FromHtml("#GGGGGG"));
        Assert.Equal("Red", ColorTranslator.ToHtml(Color.Red));
        Assert.Equal("#010203", ColorTranslator.ToHtml(Color.FromArgb(255, 1, 2, 3)));
        Assert.Equal("#010203", ColorTranslator.ToHtml(Color.FromArgb(128, 1, 2, 3)));
        Assert.Equal("activeborder", ColorTranslator.ToHtml(Color.FromKnownColor(KnownColor.ActiveBorder)));
        Assert.Equal("Red", ColorTranslator.ToHtml(Color.FromKnownColor(KnownColor.Red)));
    }

    [Fact]
    public void Translator_Html_Trims_Whitespace_DesktopParity()
    {
        // Oracle-verified against desktop .NET 9 (mdi-oracle color mode):
        // surrounding whitespace is ignored, null/empty read Empty,
        // unknown names still throw.
        Assert.Equal(unchecked((int)0xFF444444), ColorTranslator.FromHtml(" #444444").ToArgb());
        Assert.Equal(unchecked((int)0xFFC5C5C5), ColorTranslator.FromHtml(" #c5c5c5 ").ToArgb());
        Assert.Equal(Color.Red.ToArgb(), ColorTranslator.FromHtml("  red  ").ToArgb());
        Assert.Equal(Color.White.ToArgb(), ColorTranslator.FromHtml("  #ffffff").ToArgb());
        Assert.Equal(unchecked((int)0xFF123456), ColorTranslator.FromHtml("  #123456  ").ToArgb());
        Assert.Equal(unchecked((int)0x00012345), ColorTranslator.FromHtml("#12345").ToArgb());
        Assert.Equal(unchecked((int)0x00000FFF), ColorTranslator.FromHtml("\t #fff\t").ToArgb());
        Assert.True(ColorTranslator.FromHtml("").IsEmpty);
        Assert.True(ColorTranslator.FromHtml("   ").IsEmpty);
        Assert.True(ColorTranslator.FromHtml(null).IsEmpty);
        Assert.Throws<ArgumentException>(() => ColorTranslator.FromHtml("NotAColor"));
        Assert.Throws<FormatException>(() => ColorTranslator.FromHtml("#GGGGGG"));
    }

    [Fact]
    public void Translator_Ole_Win32()
    {
        Assert.Equal(unchecked((int)0xFFFF0000), ColorTranslator.FromOle(0xFF).ToArgb());
        Assert.Equal("ScrollBar", ColorTranslator.FromOle(unchecked((int)0x80000000)).Name);
        Assert.Equal(unchecked((int)0xFF190000), ColorTranslator.FromOle(unchecked((int)0x80000019)).ToArgb());
        Assert.Equal(unchecked((int)0xFF1F0000), ColorTranslator.FromOle(unchecked((int)0x8000001F)).ToArgb());
        Assert.Equal(0xFF, ColorTranslator.ToOle(Color.Red));
        Assert.Equal(unchecked((int)0x8000000A), ColorTranslator.ToOle(Color.FromKnownColor(KnownColor.ActiveBorder)));
        Assert.Equal(unchecked((int)0xFFFF0000), ColorTranslator.FromWin32(0xFF).ToArgb());
        Assert.Equal(0xFF, ColorTranslator.ToWin32(Color.Red));
    }

    [Fact]
    public void KnownColor_Table_SpotChecks()
    {
        Assert.Equal(unchecked((int)0x00FFFFFF), Color.FromKnownColor(KnownColor.Transparent).ToArgb());
        Assert.Equal(unchecked((int)0xFF3399FF), Color.FromKnownColor(KnownColor.MenuHighlight).ToArgb());
        Assert.Equal(unchecked((int)0xFF663399), Color.FromKnownColor(KnownColor.RebeccaPurple).ToArgb());
        Assert.Equal(175, (int)KnownColor.RebeccaPurple);
        Assert.Equal("Color [Transparent]", Color.Transparent.ToString());
    }
}
