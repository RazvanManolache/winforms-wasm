// Wiring tests for the GDI ABC oracle table (GdiAbcTable): the GDI measure
// path (TextRenderer.MeasureText -> GetTextExtent -> MeasureExtent) and the
// EM layout path (WasmEditLayout.PrefixWidth) must both return desktop GDI
// advances for tabled fonts (additive per-char integers, verified 9/9),
// falling back to the tight outline measure otherwise. Runs against the
// rewritten fork DLL, so it exercises the shipped wiring (needs a fresh
// rewrite-small after fork edits).
extern alias Fork;
extern alias WasmDraw;

using WfFont = WasmDraw::System.Drawing.Font;
using WfPoint = WasmDraw::System.Drawing.Point;
using ForkForms = Fork::System.Windows.Forms;
using Fork::Windows.Win32;
using Fork::Windows.Win32.Graphics.Gdi;

namespace WfWasm.WinForms.Tests;

public sealed class GdiAbcWiringTests : IDisposable
{
    private readonly ForkForms::TextBox _box;

    public GdiAbcWiringTests()
    {
        _box = new ForkForms::TextBox
        {
            Text = "jamped voer the lazee dog",
            Font = new WfFont("Microsoft Sans Serif", 8.25f),
            Width = 300,
            Height = 23,
        };
        _ = _box.Handle;
    }

    public void Dispose() => _box.Dispose();

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
    public unsafe void GetTextExtent_MsssSpellWords_MatchGdiOracle(string word, int expected)
    {
        // Direct GetTextExtent path (TextRenderer.MeasureText headless takes
        // a metric-only branch with no DC target; the extent entry is what
        // the EDIT control and GDI callers actually use).
        using var font = new WfFont("Microsoft Sans Serif", 8.25f);
        var hfont = (HFONT)font.ToHfont();
        HDC hdc = new((nint)0x3A7F2C10);
        PInvokeCore.SelectObject(hdc, hfont);
        Fork::Windows.Win32.Foundation.SIZE size;
        fixed (char* p = word)
            PInvoke.GetTextExtentPoint32W(hdc, p, word.Length, &size);
        Assert.Equal(expected, size.cx);
    }

    [Fact]
    public void PrefixWidth_JampedSpan_MatchesGdiOracle()
    {
        WfPoint start = ForkForms::WasmEditLayout.PositionFromChar(_box, 0);
        WfPoint end = ForkForms::WasmEditLayout.PositionFromChar(_box, 6);
        Assert.Equal(34, end.X - start.X);
    }

    [Fact]
    public void PrefixWidth_FullLine_MatchesGdiOracle()
    {
        WfPoint start = ForkForms::WasmEditLayout.PositionFromChar(_box, 0);
        WfPoint end = ForkForms::WasmEditLayout.PositionFromChar(_box, _box.Text.Length);
        // "jamped voer the lazee dog": 34+3+21+3+15+3+25+3+18 = 125.
        Assert.Equal(125, end.X - start.X);
    }

    [Fact]
    public void UntableFont_FallsBackToSaneMeasure()
    {
        using var font = new WfFont("Comic Sans MS", 12f);
        var size = ForkForms::TextRenderer.MeasureText("jamped", font);
        Assert.True(size.Width > 0);
        _box.Font = font;
        WfPoint start = ForkForms::WasmEditLayout.PositionFromChar(_box, 0);
        WfPoint end = ForkForms::WasmEditLayout.PositionFromChar(_box, 6);
        Assert.True(end.X - start.X > 0);
    }
}
