// Headless scroll-button painter: ControlPaint.DrawScrollButton's classic
// branch ends in User32 DrawFrameControl, which is a success-stubbed
// P/Invoke headless (null HDC), so every classic scroll arrow paints
// nothing. PDN's UpDownButtons always reaches this path in the browser
// (Application.RenderWithVisualStyles is false headless, pinned by
// VisualStyleParityTests), leaving the Colors-window numeric chips white
// and arrow-less. A rewriter rule retargets both DrawScrollButton
// overloads here (static class, so no seam override is possible); the
// helper paints an honest classic-style face + border + arrow through the
// working GDI+ path.
using System.Drawing;

namespace System.Windows.Forms;

internal static class ControlPaintScrollPainter
{
    public static void DrawScrollButton(Graphics? g, Rectangle rect, ScrollButton button, ButtonState state)
    {
        DrawScrollButton(g, rect.X, rect.Y, rect.Width, rect.Height, button, state);
    }

    public static void DrawScrollButton(Graphics? g, int x, int y, int width, int height, ScrollButton button, ButtonState state)
    {
        if (g is null || width <= 0 || height <= 0)
            return;
        bool pushed = (state & ButtonState.Pushed) != 0;
        bool inactive = (state & ButtonState.Inactive) != 0;
        Color face = pushed ? SystemColors.ControlDark : SystemColors.Control;
        using (var brush = new SolidBrush(face))
            g.FillRectangle(brush, x, y, width, height);
        using (var pen = new Pen(SystemColors.ControlDarkDark, 1f))
            g.DrawRectangle(pen, x, y, width - 1, height - 1);
        int arrow = Math.Max(4, Math.Min(width, height) / 3);
        int cx = x + width / 2 + (pushed ? 1 : 0);
        int cy = y + height / 2 + (pushed ? 1 : 0);
        int h = arrow / 2;
        Point[] tri = button switch
        {
            ScrollButton.Up => new Point[] { new Point(cx - h, cy + h / 2), new Point(cx + h, cy + h / 2), new Point(cx, cy - h / 2) },
            ScrollButton.Down => new Point[] { new Point(cx - h, cy - h / 2), new Point(cx + h, cy - h / 2), new Point(cx, cy + h / 2) },
            ScrollButton.Left => new Point[] { new Point(cx + h / 2, cy - h), new Point(cx + h / 2, cy + h), new Point(cx - h / 2, cy) },
            _ => new Point[] { new Point(cx - h / 2, cy - h), new Point(cx - h / 2, cy + h), new Point(cx + h / 2, cy) },
        };
        using (var arrowBrush = new SolidBrush(inactive ? SystemColors.GrayText : SystemColors.ControlText))
            g.FillPolygon(arrowBrush, tri);
    }
}
