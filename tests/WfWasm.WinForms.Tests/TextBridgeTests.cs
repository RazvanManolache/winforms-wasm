extern alias Fork;
extern alias WasmDraw;

using Fork::Windows.Win32;
using Fork::Windows.Win32.Graphics.Gdi;
using WfBitmap = WasmDraw::System.Drawing.Bitmap;
using WfColor = WasmDraw::System.Drawing.Color;
using WfFont = WasmDraw::System.Drawing.Font;
using WfGraphics = WasmDraw::System.Drawing.Graphics;
using WfRectangle = WasmDraw::System.Drawing.Rectangle;
using WfSize = WasmDraw::System.Drawing.Size;
using ForkTextRenderer = Fork::System.Windows.Forms.TextRenderer;

namespace WfWasm.WinForms.Tests;

// GDI text bridge (Bugtracker F1): DrawTextEx/ExtTextOut used to measure via
// a per-character heuristic and paint nothing, so Telerik grid/ribbon text
// rendered blank. The bridge paints through the Graphics registered for the
// DC scope, using the font/color recorded by the PInvokeCore hooks.
// Regression target: blank grid cells and ribbon labels in BugTracker.
public sealed class TextBridgeTests
{
    // Glyph rasterization is delegated to the browser canvas (Drawing throws
    // NotSupportedException off-browser), so desktop tests verify the bridge
    // contract — no throws, sane metrics, state recorded — and the browser
    // (BugTracker grid text) is the paint oracle.
    [Fact]
    public void TextRenderer_DrawText_Never_Throws_And_Keeps_Sane_Metrics()
    {
        using var bmp = new WfBitmap(160, 48);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var font = new WfFont("Tahoma", 12f);

        ForkTextRenderer.DrawText(g, "Hello grid", font, new WfRectangle(0, 0, 160, 48), WfColor.Red);

        WfSize measured = ForkTextRenderer.MeasureText(g, "Hello grid", font);
        Assert.True(measured.Width > 20, $"Measured width should be text-scale, got {measured.Width}.");
        Assert.True(measured.Height >= 10, $"Measured height should be font-scale, got {measured.Height}.");
    }

    [Fact]
    public unsafe void CreateFontIndirect_Mints_Distinct_Resolvable_Handles()
    {
        LOGFONTW small = new() { lfHeight = -12, lfWeight = 400 };
        small.FaceName = "Tahoma";
        LOGFONTW large = new() { lfHeight = -28, lfWeight = 700 };
        large.FaceName = "Tahoma";

        HFONT hSmall = PInvokeCore.CreateFontIndirect(&small);
        HFONT hLarge = PInvokeCore.CreateFontIndirect(&large);

        Assert.False(hSmall.IsNull);
        Assert.False(hLarge.IsNull);
        Assert.NotEqual(hSmall.Value, hLarge.Value);
        Assert.True(WfFont.TryFromHfont((nint)hSmall, out WfFont? fSmall));
        Assert.True(WfFont.TryFromHfont((nint)hLarge, out WfFont? fLarge));
        Assert.True(fLarge!.Height > fSmall!.Height);
    }

    [Fact]
    public unsafe void DrawTextEx_With_Registered_Target_Returns_Font_Scale_Height()
    {
        using var bmp = new WfBitmap(160, 48);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var target = PInvoke.WasmFillBridge.TrackTarget(default, g);
        using var font = new WfFont("Tahoma", 12f);
        PInvokeCore.SelectObject(default, (HFONT)font.ToHfont());
        PInvokeCore.SetTextColor(default, (Windows.Win32.Foundation.COLORREF)0x000000FFu);

        string text = "Hello grid";
        Windows.Win32.Foundation.RECT rect = new() { left = 0, top = 0, right = 160, bottom = 48 };
        int height;
        fixed (char* p = text)
        {
            height = PInvoke.DrawTextEx(
                default,
                new Windows.Win32.Foundation.PWSTR(p),
                text.Length,
                &rect,
                Windows.Win32.Graphics.Gdi.DRAW_TEXT_FORMAT.DT_LEFT,
                null);
        }

        Assert.True(height >= 10, $"DrawTextEx should return a font-scale height, got {height}.");
    }

    [Fact]
    public void SelectObject_And_SetTextColor_Record_Dc_State()
    {
        using var font = new WfFont("Tahoma", 14f);
        var hfont = (HFONT)font.ToHfont();
        HDC hdc = new((nint)0x3A7F2C01); // nonzero: the zero HDC is process-global DC state, keep tests isolated

        PInvokeCore.SelectObject(hdc, hfont);
        PInvokeCore.SetTextColor(hdc, (Windows.Win32.Foundation.COLORREF)0x000000FFu);

        Assert.Equal(0x000000FFu, PInvoke.GetTextColor(hdc).Value);
    }

    [Fact]
    public unsafe void GetTextMetrics_Fills_Coherent_Ascent_And_Height()
    {
        // Rich-text baseline math divides tmAscent/tmHeight; zeros
        // poison layout with NaN (RTE ParagraphLayoutBox boot death).
        // The stub must report a coherent UI-font set with
        // ascent+descent == height and a sane baseline ratio.
        TEXTMETRICW tm = default;
        HDC freshHdc = new((nint)0x3A7F2C02); // nonzero: isolated from other tests' default-HDC state
        var ok = PInvoke.GetTextMetrics(freshHdc, &tm);
        Assert.Equal(1, ok.Value);
        Assert.Equal(15, tm.tmHeight); // Segoe UI 9 row (was 13: MSSS-8.25 era; desktop GDI heights are 15 for Segoe 9)
        Assert.Equal(12, tm.tmAscent);
        Assert.Equal(3, tm.tmDescent);
        Assert.Equal(tm.tmHeight, tm.tmAscent + tm.tmDescent);
        double ratio = (double)tm.tmAscent / tm.tmHeight;
        Assert.True(ratio > 0.7 && ratio < 1.0, $"Baseline ratio should be font-like, got {ratio}.");
    }
}
