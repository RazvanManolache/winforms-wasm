// Rev61: app-private GDI text extents backed by the fork text bridge.
// Telerik's NativeTextRenderer declares its own gdi32 imports; the
// generic stub answered 0 without writing the out Size, so the
// document measurer cached (0,0) per symbol and typed glyphs never
// rastered. Runs against the rewritten fork DLL (needs a fresh
// rewrite-small after fork edits).
extern alias WasmDraw;

using WfSize = WasmDraw::System.Drawing.Size;
using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

public sealed class WasmGdiTextTests
{
    [Fact]
    public void GetTextExtentPoint32_WritesNonZeroSize()
    {
        // Rev61: the app-private import this backs was stubbed to
        // return 0 with a zeroed Size, so every rich-text span
        // measured (0,0). A real extent must come back for text.
        WfSize size = default;
        int ok = WasmGdiText.GetTextExtentPoint32(IntPtr.Zero, "Hello", 5, ref size);
        Assert.Equal(1, ok);
        Assert.True(size.Width > 0, $"width was {size.Width}");
        Assert.True(size.Height > 0, $"height was {size.Height}");
    }

    [Fact]
    public void GetTextExtentPoint32_RespectsLength()
    {
        WfSize whole = default, part = default;
        WasmGdiText.GetTextExtentPoint32(IntPtr.Zero, "HelloWorld", 10, ref whole);
        WasmGdiText.GetTextExtentPoint32(IntPtr.Zero, "HelloWorld", 2, ref part);
        Assert.True(part.Width > 0);
        Assert.True(part.Width < whole.Width,
            $"part {part.Width} !< whole {whole.Width}");
    }

    [Fact]
    public void GetTextExtentExPoint_FillsFitAndPartialWidths()
    {
        var fit = new int[1];
        var partial = new int[5];
        WfSize size = default;
        bool ok = WasmGdiText.GetTextExtentExPoint(
            IntPtr.Zero, "Hello", 5, int.MaxValue, fit, partial, ref size);
        Assert.True(ok);
        Assert.Equal(5, fit[0]);
        Assert.True(partial[0] > 0);
        for (int i = 1; i < partial.Length; i++)
            Assert.True(partial[i] > partial[i - 1],
                $"partial[{i}]={partial[i]} !> partial[{i - 1}]={partial[i - 1]}");
        Assert.True(size.Width > 0 && size.Height > 0);
    }

    [Fact]
    public void GetTextExtentExPoint_FitStopsAtMaxExtent()
    {
        var fit = new int[1];
        WfSize size = default;
        bool ok = WasmGdiText.GetTextExtentExPoint(
            IntPtr.Zero, "Hello", 5, 1, fit, null, ref size);
        Assert.True(ok);
        Assert.Equal(0, fit[0]);
    }

    [Fact]
    public void SelectObject_NonFont_KeepsHandleShape()
    {
        IntPtr prev = WasmGdiText.SelectObject(IntPtr.Zero, new IntPtr(12345));
        Assert.NotEqual(IntPtr.Zero, prev);
    }

    // DRAW_TEXT_FORMAT values (tests cannot name the internal enum).
    private const int DT_WORDBREAK = 0x10;
    private const int DT_SINGLELINE = 0x20;
    private const int DT_CALCRECT = 0x400;

    [Fact]
    public unsafe void DrawText_CalcRect_WordBreak_Wraps_Height()
    {
        // No-target measure-only DCs must still answer CALCRECT: stub-zero
        // extents collapsed every wrapped Telerik element (BookShelf tile
        // titles render nothing). A paragraph in a 100px rect must come
        // back more than one 16px line tall.
        string text = "The Secret by Rhonda Byrne (2006, Hardcover)";
        // Dedicated never-registered key: IntPtr.Zero is the shared paint
        // HDC and can resolve when parallel tests hold paint scopes (the
        // census caught height 24 from a foreign target instead of 32+).
        int height = WasmGdiText.DrawText((IntPtr)0x44F0, text, -1,
            0, 0, 100, 16, DT_CALCRECT | DT_WORDBREAK,
            out int calcRight, out int calcBottom);
        Assert.True(height >= 32, $"wrapped height was {height}");
        Assert.True(calcBottom - 0 >= 32, $"calc rect height was {calcBottom}");
        Assert.True(calcRight - 0 > 0, $"calc rect width was {calcRight}");
    }

    [Fact]
    public unsafe void DrawText_CalcRect_SingleLine_Measures()
    {
        int height = WasmGdiText.DrawText((IntPtr)0x44F1, "Hello", -1,
            0, 0, 500, 16, DT_CALCRECT | DT_SINGLELINE,
            out int calcRight, out int calcBottom);
        Assert.True(height >= 16, $"height was {height}");
        Assert.True(calcRight - 0 > 0, $"width was {calcRight}");
    }

    [Fact]
    public unsafe void DrawText_NoCalcRect_NoTarget_Stays_Zero()
    {
        // Painting without a target stays a no-op (desktop parity: only
        // CALCRECT writes extents back); pin the zero, not the measure.
        int height = WasmGdiText.DrawText(IntPtr.Zero, "Hello", -1,
            0, 0, 500, 16, DT_SINGLELINE,
            out int calcRight, out int calcBottom);
        Assert.Equal(0, height);
        Assert.Equal(500, calcRight);
        Assert.Equal(16, calcBottom);
    }
}
