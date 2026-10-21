// Fork-owned: headless RichTextBoxDrawing.Draw replacement. The vendor
// Draw renders RTF through EM_FORMATRANGE (its own user32 SendMessage),
// which cannot survive headless, and both of its callers (diagram
// RichTextNode.RenderRTFToMetafile/RenderRTFToBitmap) funnel through it.
// The managed equivalent draws the clear text with the box's own font,
// color and word-wrap via the Drawing fork: content is preserved, RTF
// run formatting (mixed fonts/colors/sizes) is not. The rewriter (rev78)
// swaps the vendor Draw(Graphics, Rectangle) body for a trampoline here;
// the 8-argument EM_FORMATRANGE overload stays native and unreached.

using System.Drawing;

namespace System.Windows.Forms;

public static class WasmRtfDraw
{
    public static void DrawRtfManaged(RichTextBox box, Graphics g, Rectangle layout)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(g);
        string text = box.Text;
        if (text.Length == 0)
        {
            return;
        }

        using var brush = new SolidBrush(box.ForeColor);
        g.DrawString(text, box.Font, brush, new RectangleF(layout.X, layout.Y, layout.Width, layout.Height), BuildFormat(box.WordWrap));
    }

    // Pure format mapping behind the helper: pinned directly by unit tests
    // (a live RichTextBox cannot be constructed in every harness).
    public static StringFormat BuildFormat(bool wordWrap)
    {
        var format = new StringFormat(StringFormat.GenericDefault);
        if (!wordWrap)
        {
            format.FormatFlags |= StringFormatFlags.NoWrap;
        }

        return format;
    }
}
