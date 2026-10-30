using System.Drawing;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Managed pixel parity for retained scroll reuse: shifting the retained
// bitmap by the consumed (dx,dy) and repainting only the
// ComputeScrollNarrow set (a single merged rect in every case below) must
// be byte-identical to a full repaint of translated content. Both sides
// use production APIs (Bitmap.ShiftPixels,
// Graphics.SetClip like PaintRegionPixels, ToRgbaBytes slice/full). The
// pattern is a pure function of the content origin, mirroring an OnPaint
// that draws from the scrolled position; the strip side paints through
// the same clip the host sets. A mismatch here would mean the browser
// shows torn scrolled content, so these fail before any narrowing or
// shift-math change that breaks the moved==repainted invariant.
public sealed class ScrollShiftPixelParityTests
{
    private const int W = 320;
    private const int H = 240;

    private static readonly Color[] Bands =
    {
        Color.FromArgb(200, 40, 40), Color.FromArgb(40, 160, 40),
        Color.FromArgb(40, 80, 200), Color.FromArgb(220, 180, 40),
    };

    // Content painter: everything derives from the origin so painting
    // with origin (dx,dy) is exactly what a full post-scroll repaint
    // draws. Includes offscreen/partial shapes and an ellipse so edges
    // cross strip boundaries.
    private static void PaintContent(Graphics g, int ox, int oy)
    {
        using var bg = new SolidBrush(Color.White);
        g.FillRectangle(bg, 0, 0, W, H);
        for (int i = 0; i < 9; i++)
        {
            using var b = new SolidBrush(Bands[i % Bands.Length]);
            g.FillRectangle(b, ox + i * 53 - 40, oy + (i % 3) * 71 - 20, 47, 29);
        }
        using var red = new SolidBrush(Color.FromArgb(220, 30, 30));
        g.FillEllipse(red, ox + 100, oy + 120, 90, 60);
        using var dark = new SolidBrush(Color.FromArgb(30, 30, 30));
        g.FillRectangle(dark, ox - 30, oy + H - 12, W + 60, 8);
    }

    private static byte[] FullRepaint(int ox, int oy)
    {
        using var bmp = new Bitmap(W, H);
        using (Graphics g = Graphics.FromImage(bmp))
            PaintContent(g, ox, oy);
        return bmp.ToRgbaBytes();
    }

    private static byte[] ShiftAndStrip(byte[] pre, int dx, int dy, Rectangle dirty)
    {
        Rectangle[]? narrow = WinFormsAppHost.ComputeScrollNarrow(W, H, dx, dy, dirty);
        Assert.NotNull(narrow);
        Rectangle stripRect = Assert.Single(narrow);
        // Rebuild the pre-scroll bitmap from bytes is not exposed, so
        // repaint pre-scroll content, then shift + repaint the strip.
        using var bmp = new Bitmap(W, H);
        using (Graphics g = Graphics.FromImage(bmp))
            PaintContent(g, 0, 0);
        byte[] before = bmp.ToRgbaBytes();
        Assert.Equal(pre, before);
        bmp.ShiftPixels(dx, dy);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SetClip(stripRect);
            PaintContent(g, dx, dy);
        }
        // The strip slice must equal slicing the full extract (slice
        // twin contract), and the whole bitmap must equal full repaint.
        byte[] strip = bmp.ToRgbaBytes(stripRect);
        byte[] full = bmp.ToRgbaBytes();
        Assert.Equal(strip.Length, stripRect.Width * stripRect.Height * 4);
        for (int y = 0; y < stripRect.Height; y++)
        {
            int src = ((stripRect.Y + y) * W + stripRect.X) * 4;
            int dst = y * stripRect.Width * 4;
            Assert.Equal(full.AsSpan(src, stripRect.Width * 4).ToArray(),
                strip.AsSpan(dst, stripRect.Width * 4).ToArray());
        }
        return full;
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(-45, 0)]
    [InlineData(0, 40)]
    [InlineData(0, -33)]
    public void ShiftPlusStrip_EqualsFullRepaint(int dx, int dy)
    {
        // Pure scroll: the scrolled control dirties exactly the strip it
        // vacates (pre-scroll coords), matching ScrollWindowEx behavior.
        Rectangle dirty = dx > 0 ? new Rectangle(0, 0, dx, H)
            : dx < 0 ? new Rectangle(W + dx, 0, -dx, H)
            : dy > 0 ? new Rectangle(0, 0, W, dy)
            : new Rectangle(0, H + dy, W, -dy);
        byte[] pre = FullRepaint(0, 0);
        Assert.Equal(FullRepaint(dx, dy), ShiftAndStrip(pre, dx, dy, dirty));
    }

    [Fact]
    public void ShiftPlusStrip_WithNearbyEdit_EqualsFullRepaint()
    {
        // An edit rect wider than the exposed strip widens the union;
        // the repaint must still be exact, not just narrow.
        var dirty = new Rectangle(0, 0, 120, H);
        byte[] pre = FullRepaint(0, 0);
        Assert.Equal(FullRepaint(60, 0), ShiftAndStrip(pre, 60, 0, dirty));
    }

    [Fact]
    public void StripUpload_SmallerThanFullUpload()
    {
        // The whole point of the strip path: bytes on the wire scale
        // with the strip, not the surface.
        Rectangle[]? narrow = WinFormsAppHost.ComputeScrollNarrow(
            W, H, 60, 0, new Rectangle(0, 0, 60, H));
        Assert.NotNull(narrow);
        Rectangle stripRect = Assert.Single(narrow);
        using var bmp = new Bitmap(W, H);
        using (Graphics g = Graphics.FromImage(bmp))
            PaintContent(g, 0, 0);
        bmp.ShiftPixels(60, 0);
        Assert.True(bmp.ToRgbaBytes(stripRect).Length < bmp.ToRgbaBytes().Length);
    }
}
