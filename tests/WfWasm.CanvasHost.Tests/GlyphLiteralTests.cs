using System.Text;

namespace WfWasm.CanvasHost.Tests;

// Glyph-literal contract: WinFormsAppHost.cs once carried 13 double-UTF8-
// mojibake caption/icon literals (the form close box painted "A"+dash
// instead of x, toolbar icons painted mojibake). Root cause was an
// edit round-trip through a cp1252-decoding tool, twice. Repairs use
// ASCII \uXXXX escapes (immune to any future round-trip); this locks the
// escapes in and bars the mojibake marker from the file. Written
// entirely in ASCII so the test itself cannot corrupt.
public sealed class GlyphLiteralTests
{
    private static string RepoFile(params string[] parts)
    {
        string? dir = Path.GetDirectoryName(SourceFile());
        return Path.GetFullPath(Path.Combine(new[] { "../../" }.Concat(parts).ToArray()), dir!);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string HostSource() =>
        File.ReadAllText(RepoFile("src", "WfWasm.CanvasHost", "WinFormsAppHost.cs"), Encoding.UTF8);

    [Fact]
    public void CaptionCloseGlyph_Uses_Multiplication_Sign()
    {
        Assert.Contains("\\u00d7", HostSource());
    }

    [Fact]
    public void MenuGlyphs_Use_Escaped_Check_And_Arrows()
    {
        string text = HostSource();
        Assert.Contains("\\u2713", text);
        Assert.Contains("\\u25b6", text);
        Assert.Contains("\\u25be", text);
    }

    [Fact]
    public void ToolbarGlyphTable_Uses_Escaped_Icons()
    {
        string text = HostSource();
        foreach (string esc in new[] { "\\u2022", "\\u2702", "\\u2715", "\\u21b6", "\\u21b7", "\\u2699", "\\u25aa" })
            Assert.Contains(esc, text);
    }

    [Fact]
    public void HostSource_Has_No_Mojibake_Marker()
    {
        // \u00C3 (A-tilde) leads every cp1252 double-encoding scar.
        Assert.DoesNotContain("\u00c3", HostSource());
    }
}
