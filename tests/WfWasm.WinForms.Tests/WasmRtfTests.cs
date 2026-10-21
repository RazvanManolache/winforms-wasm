extern alias Fork;

using ForkRtf = Fork::System.Windows.Forms.RichTextBox.WasmRtf;

namespace WfWasm.WinForms.Tests;

// Pins the headless EDITSTREAM replacement (rev77): the desktop
// StreamIn/StreamOut drive native RichEdit through
// Marshal.GetFunctionPointerForDelegate, whose thunk traps the wasm
// interpreter with "memory access out of bounds" and no managed frame
// (Syncfusion diagram RichTextNode boot). The byte/text mapping below is
// pure so it pins without constructing a RichTextBox (desktop demands
// MsftEdit for one; headless has none); the thin box glue
// (StreamInManaged/StreamOutManaged) is covered by the rewrite-rule test
// plus the browser run.
public sealed class WasmRtfTests
{
    private const uint SF_RTF = 0x0002;
    private const uint SF_TEXT = 0x0001;
    private const uint SF_UNICODE = 0x0010;

    // The exact NodesDemo RichTextNode payload (ldstr from the demo).
    private const string DemoRtf =
        "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\fnil\\fcharset0 Meiryo UI;}{\\f1\\fnil\\fcharset0 Microsoft Sans Serif;}}\r\n" +
        "{\\colortbl ;\\red255\\green0\\blue0;\\red0\\green64\\blue128;}\r\n" +
        "\\viewkind4\\uc1\\pard\\b\\f0\\fs17 This is a text symbol\\f1  \\b0\\f0 designed using the \\cf1\\b\\i Essential Diagram\\i0  \\cf2\\ul RichText\\ulnone  node\\cf0\\b0 .\\par\r\n" +
        "\\i\\f1\\fs20\\par\r\n}";

    // Single spaces throughout: each control word eats exactly one delimiter
    // space ({\i0␣␣\cf2} and {\ulnone␣␣node} keep one text space each).
    private const string DemoPlain =
        "This is a text symbol designed using the Essential Diagram RichText node.\n\n";

    [Fact]
    public void DemoRtf_ExtractsPlainText()
    {
        Assert.Equal(DemoPlain, ForkRtf.RtfToPlainText(DemoRtf));
    }

    [Fact]
    public void DemoRtf_SkipsMetadataTables()
    {
        string plain = ForkRtf.RtfToPlainText(DemoRtf);
        Assert.DoesNotContain("Meiryo", plain);
        Assert.DoesNotContain("Sans Serif", plain);
        Assert.DoesNotContain("red255", plain);
        Assert.DoesNotContain("fonttbl", plain);
        Assert.Contains("Essential Diagram", plain);
        Assert.Contains("RichText", plain);
    }

    [Fact]
    public void ControlWords_MapToWhitespace()
    {
        Assert.Equal("a\nb\nc\td", ForkRtf.RtfToPlainText("{\\rtf1 a\\par b\\line c\\tab d}"));
    }

    [Fact]
    public void HexEscape_DecodesByte()
    {
        Assert.Equal("A", ForkRtf.RtfToPlainText("{\\rtf1\\ansi \\'41}"));
    }

    [Fact]
    public void UnicodeEscape_SkipsFallbackChars()
    {
        Assert.Equal("\u03B1x", ForkRtf.RtfToPlainText("{\\rtf1\\ansi\\uc1\\u945?x}"));
    }

    [Fact]
    public void Literals_Unescape()
    {
        Assert.Equal(@"A{B}C\D", ForkRtf.RtfToPlainText(@"{\rtf1 A\{B\}C\\D}"));
    }

    [Fact]
    public void EscapedSourceBreaks_CarryNoContent()
    {
        Assert.Equal("ab", ForkRtf.RtfToPlainText("{\\rtf1 a\\\r\nb}"));
    }

    [Fact]
    public void ComputeStreamText_Rtf_YieldsDemoPlain()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(DemoRtf);
        Assert.Equal(DemoPlain, ForkRtf.ComputeStreamText(bytes, SF_RTF));
    }

    [Fact]
    public void ComputeStreamText_Plain_PassesThrough()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("hello");
        Assert.Equal("hello", ForkRtf.ComputeStreamText(bytes, SF_TEXT));
    }

    [Fact]
    public void ComputeStreamText_Unicode_DecodesUtf16()
    {
        byte[] bytes = System.Text.Encoding.Unicode.GetBytes("hi");
        Assert.Equal("hi", ForkRtf.ComputeStreamText(bytes, SF_TEXT | SF_UNICODE));
    }

    [Fact]
    public void ComputeStreamText_Rtf_RejectsMissingTag()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("not rtf");
        Assert.Throws<ArgumentException>(() => ForkRtf.ComputeStreamText(bytes, SF_RTF));
    }

    [Fact]
    public void ComputeStreamPayload_Text_WritesBytes()
    {
        byte[] bytes = ForkRtf.ComputeStreamPayload("hi", SF_TEXT, includeCrLfs: false);
        Assert.Equal("hi", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void ComputeStreamPayload_Crlf_NormalizesEndings()
    {
        byte[] bytes = ForkRtf.ComputeStreamPayload("a\nb", SF_TEXT, includeCrLfs: true);
        Assert.Equal("a\r\nb", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void BuildFormat_Wrap_TogglesNoWrap()
    {
        var wrapped = Fork::System.Windows.Forms.WasmRtfDraw.BuildFormat(wordWrap: true);
        Assert.Equal(0, (int)(wrapped.FormatFlags & global::System.Drawing.StringFormatFlags.NoWrap));
        var nowrap = Fork::System.Windows.Forms.WasmRtfDraw.BuildFormat(wordWrap: false);
        Assert.NotEqual(0, (int)(nowrap.FormatFlags & global::System.Drawing.StringFormatFlags.NoWrap));
    }

    [Fact]
    public void SliceSelection_ClampsToText()
    {
        Assert.Equal("ell", ForkRtf.SliceSelection("hello", 1, 3));
        Assert.Equal(string.Empty, ForkRtf.SliceSelection("hello", 2, 0));
        Assert.Equal(string.Empty, ForkRtf.SliceSelection("hello", 9, 4));
        Assert.Equal("lo", ForkRtf.SliceSelection("hello", 3, 99));
        Assert.Equal("hello", ForkRtf.SliceSelection("hello", -4, 99));
    }

    [Fact]
    public void ComputeStreamPayload_Rtf_RoundTripsText()
    {
        byte[] bytes = ForkRtf.ComputeStreamPayload("hi\nyou", SF_RTF, includeCrLfs: false);
        string payload = System.Text.Encoding.ASCII.GetString(bytes);
        Assert.StartsWith("{\\rtf", payload);
        string back = ForkRtf.RtfToPlainText(payload);
        Assert.Contains("hi", back);
        Assert.Contains("you", back);
    }
}
