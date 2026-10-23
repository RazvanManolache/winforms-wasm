// Headless spin-button painter: VisualStyleRenderer.DrawBackground is a
// UxTheme P/Invoke stubbed to S_OK (HDC is null headless), so themed spin
// arrows vanish and PDN's UpDownButtons keeps its Violet debug fill as the
// visible face. This helper paints SPIN (up/down) parts through the
// working GDI+ path; every other class/part stays a silent no-op
// (preserves the no-op contract the stub documents). A rewriter rule
// retargets DrawBackground calls here (the upstream class is sealed and
// non-partial, so no seam override is possible).
// Regression: PDN Colors window spinner chips solid magenta (arrows
// missing) while the toolbar (standard WinForms) spinners paint.
using System.Drawing;

namespace System.Windows.Forms.VisualStyles;

internal static class VisualStyleSpinPainter
{
    public static void DrawSpinBackground(object? renderer, object? dc, Rectangle bounds)
    {
        DrawSpinBackground(renderer, dc, bounds, bounds);
    }

    public static void DrawSpinBackground(object? renderer, object? dc, Rectangle bounds, Rectangle clip)
    {
        if (renderer is not VisualStyleRenderer vsr)
            return;
        if (!string.Equals(vsr.Class, "SPIN", StringComparison.OrdinalIgnoreCase))
            return;
        int part = vsr.Part;
        if (part != 1 && part != 2)
            return;
        if (dc is not Graphics g)
            return;
        Rectangle r = Rectangle.Intersect(bounds, clip);
        if (r.Width <= 0 || r.Height <= 0)
            return;
        int state = vsr.State;
        bool disabled = state == 4;
        bool pressed = state == 3;
        Color face = disabled ? SystemColors.Control
            : pressed ? SystemColors.ControlDark
            : SystemColors.Control;
        using (var brush = new SolidBrush(face))
            g.FillRectangle(brush, r);
        using (var pen = new Pen(SystemColors.ControlDark, 1f))
            g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + r.Height / 2f;
        float hw = Math.Max(2.5f, Math.Min(6f, r.Width * 0.28f));
        float hh = Math.Max(2f, Math.Min(5f, r.Height * 0.30f));
        PointF[] tri = part == 1
            ? new PointF[] { new PointF(cx - hw, cy + hh / 2f), new PointF(cx + hw, cy + hh / 2f), new PointF(cx, cy - hh / 2f) }
            : new PointF[] { new PointF(cx - hw, cy - hh / 2f), new PointF(cx + hw, cy - hh / 2f), new PointF(cx, cy + hh / 2f) };
        using (var arrow = new SolidBrush(disabled ? SystemColors.GrayText : SystemColors.ControlText))
            g.FillPolygon(arrow, tri);
    }
}
