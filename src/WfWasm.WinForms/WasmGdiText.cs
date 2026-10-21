using System.Drawing;
using Windows.Win32.Graphics.Gdi;

namespace System.Windows.Forms;

// Real GDI text-extent surface for app-private P/Invokes. Telerik's
// NativeTextRenderer declares its own gdi32 imports in the app
// assembly; the rewriter's generic stub answered success-shaped
// zeros without writing the out Size, so the rich-text measurer
// cached (0,0) for every symbol: each document SpanLayoutBox got
// DesiredTextSize (0,0), typed glyphs never rasterized, and TPF
// document layout never reached a fixed point (a layout post per
// frame forever). The rewriter retargets those imports here
// (rev61); metrics come from the fork GDI text bridge (per-HDC
// selected-font state, oracle ABC advances, GDI cell height) - the
// same implementation backing the fork's own GetTextExtentPoint32W.
// Regression: ZZ invisible <- SpanLayoutBox (0,0) <- RadTextMeasurer
// <- NativeTextRenderer.GetTextExtentPoint32 stub (RTE typing).
public static class WasmGdiText
{
    public static int GetTextExtentPoint32(IntPtr hdc, string? text, int length, ref Size size)
    {
        size = global::Windows.Win32.PInvoke.WasmTextBridge.MeasureExtent((HDC)hdc, Slice(text, length));
        return 1;
    }

    // ExPoint adds fit accounting: partialWidths[i] is the cumulative
    // advance after char i (desktop advances are additive per char),
    // fit[0] counts chars whose cumulative width fits maxExtent.
    public static bool GetTextExtentExPoint(
        IntPtr hdc, string? text, int length, int maxExtent,
        int[]? fit, int[]? partialWidths, ref Size size)
    {
        string s = Slice(text, length);
        var dc = (HDC)hdc;
        size = global::Windows.Win32.PInvoke.WasmTextBridge.MeasureExtent(dc, s);
        int fitted = 0;
        int acc = 0;
        for (int i = 0; i < s.Length; i++)
        {
            acc += global::Windows.Win32.PInvoke.WasmTextBridge.MeasureExtent(dc, s[i].ToString()).Width;
            if (partialWidths is not null && i < partialWidths.Length)
                partialWidths[i] = acc;
            if (acc <= maxExtent)
                fitted = i + 1;
        }

        if (fit is not null && fit.Length > 0)
            fit[0] = fitted;
        return true;
    }

    // Registers the selected HFONT with the bridge so extents above
    // measure with the caller's font instead of the default font.
    // Non-font objects keep the previous-handle shape (1).
    public static IntPtr SelectObject(IntPtr hdc, IntPtr h)
        => global::Windows.Win32.PInvoke.WasmTextBridge.SelectObject((HDC)hdc, (HGDIOBJ)h).Value;

    // Rev81 bridge for app-private gdi32 DrawText P/Invokes (the
    // rewriter decomposes the vendor RECT& into coords because the
    // struct type varies per declaring class: NativeMethods/RECT,
    // NativePaint/RECT, ComboBoxEx/RECT, Win32API.RECT). Paint calls
    // rasterize through the fork GDI text bridge (per-HDC font state,
    // zero-HDC resolution during scoped paint); misses keep stub-zero.
    // Rev82: CALCRECT writes the measured extents back through the out
    // pair (the rewriter stores them into the vendor rect). Tree labels
    // are measured with CALCRECT and painted into the measured rect, so
    // stub-zero extents squeezed every label to slivers. Paint calls
    // report the input rect unchanged (desktop parity: non-CALCRECT
    // DrawText never modifies the rect).
    public static unsafe int DrawText(IntPtr hdc, string? text, int cchText, int left, int top, int right, int bottom, int flags, out int calcRight, out int calcBottom)
    {
        calcRight = right;
        calcBottom = bottom;
        try
        {
            string s = text ?? "";
            if (cchText < 0)
            {
                int nul = s.IndexOf('\0');
                if (nul >= 0) s = s.Substring(0, nul);
            }
            else if (cchText == 0)
            {
                return 0;
            }
            else if (cchText < s.Length)
            {
                s = s.Substring(0, cchText);
            }
            if (s.Length == 0) return 0;
            var rect = new global::Windows.Win32.Foundation.RECT(left, top, right, bottom);
            var format = (global::Windows.Win32.Graphics.Gdi.DRAW_TEXT_FORMAT)flags;
            if (global::Windows.Win32.PInvoke.WasmTextBridge.TryDrawText(
                (global::Windows.Win32.Graphics.Gdi.HDC)hdc, s, &rect,
                format, null, out int height))
            {
                if (format.HasFlag(global::Windows.Win32.Graphics.Gdi.DRAW_TEXT_FORMAT.DT_CALCRECT))
                {
                    calcRight = rect.right;
                    calcBottom = rect.bottom;
                }
                return height;
            }
            // No paint target (measure-only DC): painting stays a no-op,
            // but CALCRECT measurement must still answer - stub-zero
            // extents collapsed Telerik wrapped text and tree labels.
            if (format.HasFlag(global::Windows.Win32.Graphics.Gdi.DRAW_TEXT_FORMAT.DT_CALCRECT)
                && (format.HasFlag(global::Windows.Win32.Graphics.Gdi.DRAW_TEXT_FORMAT.DT_WORDBREAK)
                    || format.HasFlag(global::Windows.Win32.Graphics.Gdi.DRAW_TEXT_FORMAT.DT_SINGLELINE)))
            {
                // No-target CALCRECT without WORDBREAK/SINGLELINE keeps the
                // legacy stub-zero contract (DrawText_UnregisteredHdc test);
                // wrapped/single-line measurement must still answer -
                // stub-zero extents collapsed Telerik wrapped text and
                // BookShelf tile titles (desktop: CALCRECT measures).
                // Rewriter-proof shape: no out-var declaration, no System.Math
                // (the IL rewriter has silently dropped this block before).
                int wrapWidth = right - left;
                int totalWidth = 0;
                int totalHeight = global::Windows.Win32.PInvoke.WasmTextBridge.MeasureHeuristic(
                    s.AsSpan(), wrapWidth, format, 0, 0, out totalWidth);
                calcRight = left + (totalWidth > 0 ? totalWidth : 1);
                calcBottom = top + totalHeight;
                return totalHeight;
            }
            return 0;
        }
        catch (global::System.Exception ex)
        {
            global::Windows.Win32.PInvoke.WasmTextBridge.s_fallbackError =
                ex.GetType().Name + ":" + (ex.Message ?? "");
            return 0;
        }
    }

    // Forwarder for the host tile dump: the same-assembly text bridge
    // is shadowed at the host by its own CsWin32 PInvoke, while this
    // type resolves to the fork there.
    public static string DumpGdiPaintCensus() =>
        global::Windows.Win32.PInvoke.WasmTextBridge.DumpGdiPaintCensus();

    private static string Slice(string? text, int length)
        => string.IsNullOrEmpty(text) || length <= 0
            ? ""
            : length < text.Length ? text.Substring(0, length) : text;
}
