extern alias WfDraw;

using WfPrivateFonts = WfDraw::System.Drawing.Text.PrivateFontCollection;
using WfInstalledFonts = WfDraw::System.Drawing.Text.InstalledFontCollection;

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

    [Fact]
    public void PrivateFontCollection_Starts_Empty()
    {
        using var fonts = new WfPrivateFonts();
        Assert.Empty(fonts.Families);
    }

    [Fact]
    public void InstalledFontCollection_Still_Returns_Generics()
    {
        using var fonts = new WfInstalledFonts();
        Assert.Equal(3, fonts.Families.Length);
    }

    [Fact]
    public void AddMemoryFont_Exposes_Parsed_Family_Name()
    {
        byte[] ttf = MinimalTtf("TestFam");
        IntPtr memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(ttf.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(ttf, 0, memory, ttf.Length);
            using var fonts = new WfPrivateFonts();
            fonts.AddMemoryFont(memory, ttf.Length);
            var families = fonts.Families;
            Assert.Single(families);
            Assert.Equal("TestFam", families[0].Name);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void AddMemoryFont_Unparseable_Blob_Keeps_Empty()
    {
        byte[] blob = [0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D];
        IntPtr memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(blob.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(blob, 0, memory, blob.Length);
            using var fonts = new WfPrivateFonts();
            fonts.AddMemoryFont(memory, blob.Length);
            Assert.Empty(fonts.Families);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);
        }
    }

    [Fact]
    public void AddFontFile_Names_Family_For_File()
    {
        using var fonts = new WfPrivateFonts();
        fonts.AddFontFile("SomeFont.ttf");
        var families = fonts.Families;
        Assert.Single(families);
        Assert.Equal("SomeFont", families[0].Name);
    }

    private static byte[] MinimalTtf(string family)
    {
        var nameBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(family);
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.AddRange([0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("name"));
        bytes.AddRange([0x00, 0x00, 0x00, 0x00]);
        bytes.AddRange([0x00, 0x00, 0x00, 0x1C]);
        int nameTableLength = 6 + 12 + nameBytes.Length;
        bytes.AddRange([0x00, 0x00, (byte)(nameTableLength >> 8), (byte)nameTableLength]);
        bytes.AddRange([0x00, 0x00, 0x00, 0x01, 0x00, 0x12]);
        bytes.AddRange([0x00, 0x03, 0x00, 0x01, 0x04, 0x09, 0x00, 0x01]);
        bytes.AddRange([(byte)(nameBytes.Length >> 8), (byte)nameBytes.Length, 0x00, 0x00]);
        bytes.AddRange(nameBytes);
        return [.. bytes];
    }
}
