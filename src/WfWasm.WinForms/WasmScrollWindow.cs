using System.Drawing;

namespace System.Windows.Forms;

// Managed ScrollWindowEx surface for vendor scroll painters. Native
// ScrollWindowEx blits moved pixels inside the window and reports the
// exposed strip through the update region; headless windows have no
// surface, so the generic stub answered success with no side effects
// and the cracked (empty) update region invalidated nothing. Vendor
// scroll state still advanced, so the grid believed it had scrolled
// while every pixel stayed stale.
// The rewriter retargets ScrollWindowEx imports here (rev289). For an
// axis-aligned scroll we record the true displacement with
// WasmRepaintGate.NoteScroll and invalidate only the exposed strip:
// the pump shifts the retained surface by (dx,dy) and repaints the
// strip (browser TexShift + strip upload). Anything else (diagonal,
// zero, oversize, or a vendor full invalidate alongside) keeps the
// safe full-region fallback; the narrow math or the 90% gate drops
// the shift and the existing region/full paths run unchanged.
// Regression: wheel no-op with advancing scrollbar value <-
// SfDataGrid TableControl scroll.
public static class WasmScrollWindow
{
    public static bool Scroll(
        IntPtr hwnd,
        int dx, int dy,
        int scrollLeft, int scrollTop, int scrollRight, int scrollBottom,
        int clipLeft, int clipTop, int clipRight, int clipBottom)
    {
        try
        {
            Control? control = Control.FromHandle(hwnd);
            if (control is null || control.IsDisposed)
                return false;
            Rectangle scroll = Rectangle.FromLTRB(scrollLeft, scrollTop, scrollRight, scrollBottom);
            Rectangle clip = Rectangle.FromLTRB(clipLeft, clipTop, clipRight, clipBottom);
            if (scroll.IsEmpty)
                scroll = clip;
            if (scroll.IsEmpty)
                scroll = control.ClientRectangle;
            if (!clip.IsEmpty)
                scroll.Intersect(clip);
            scroll.Intersect(control.ClientRectangle);
            if (scroll.IsEmpty)
                return false;
            if ((dx == 0 && dy == 0) || (dx != 0 && dy != 0))
            {
                control.Invalidate(scroll);
                return true;
            }
            WasmRepaintGate.NoteScroll(control, dx, dy);
            Rectangle strip;
            if (dy > 0)
                strip = new Rectangle(scroll.X, scroll.Y, scroll.Width, dy);
            else if (dy < 0)
                strip = new Rectangle(scroll.X, scroll.Bottom + dy, scroll.Width, -dy);
            else if (dx > 0)
                strip = new Rectangle(scroll.X, scroll.Y, dx, scroll.Height);
            else
                strip = new Rectangle(scroll.Right + dx, scroll.Y, -dx, scroll.Height);
            strip.Intersect(scroll);
            if (strip.IsEmpty)
            {
                control.Invalidate(scroll);
                return true;
            }
            control.Invalidate(strip);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
