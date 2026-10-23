extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfCombineMode = WfDraw::System.Drawing.Drawing2D.CombineMode;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfRegion = WfDraw::System.Drawing.Region;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfCounters = WfDraw::System.Drawing.TextRasterCounters;

// Rectangular clipped solid-fill fast path: a single integer device rect
// constrains the memset to the clip intersection — the exact set the
// per-pixel loop writes (same Contains test, same half-open bounds).
// Multi-rect unions and fractional rects keep the slow path; empty
// clips/intersections write and bump nothing.
public sealed class FillClipFastPathTests
{
    private static int[] Pixels(WfBitmap bmp)
    {
        var arr = new int[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                arr[y * bmp.Width + x] = bmp.GetPixel(x, y).ToArgb();
        return arr;
    }

    [Fact]
    public void Single_Rect_Clip_Fill_Matches_Intersection()
    {
        using var clipped = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.SetClip(new WfRectangleF(4, 4, 10, 10), WfCombineMode.Intersect);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(4, 4, 10, 10));
        }
        Assert.Equal(Pixels(reference), Pixels(clipped));
        Assert.Equal(reference.PaintVersion, clipped.PaintVersion);
        // Spot checks: inside red, outside white.
        Assert.Equal(WfColor.Red.ToArgb(), clipped.GetPixel(4, 4).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), clipped.GetPixel(13, 13).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), clipped.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), clipped.GetPixel(14, 14).ToArgb());
    }

    [Fact]
    public void Empty_Clip_Fill_Is_NoOp()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        g.ExcludeClip(new WfRectangleF(0, 0, 20, 20));
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(before, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void Empty_Intersection_Returns_Without_Bump()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        g.SetClip(new WfRectangleF(0, 0, 5, 5), WfCombineMode.Intersect);
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(10, 10, 5, 5));
        Assert.Equal(before, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(12, 12).ToArgb());
    }

    [Fact]
    public void Multi_Rect_Clip_Keeps_Slow_Path_Pixels()
    {
        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int before = bmp.PaintVersion;
        var union = new WfRegion(new WfRectangleF(0, 0, 6, 20));
        union.Union(new WfRectangleF(14, 0, 6, 20));
        g.Clip = union;
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(0, 10).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(19, 10).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(10, 10).ToArgb());
        // 6*20 + 6*20 written pixels bump the version by the same count
        // the per-pixel loop writes.
        Assert.Equal(before + 240, bmp.PaintVersion);
    }

    [Fact]
    public void Fractional_Rect_Clip_Pixel_Contract()
    {
        using var bmp = new WfBitmap(10, 10);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        g.SetClip(new WfRectangleF(2.5f, 2.5f, 5f, 5f), WfCombineMode.Intersect);
        g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 10, 10));
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(8, 8).ToArgb());
    }

    [Fact]
    public void Infinite_Clip_Behaves_As_Null()
    {
        using var clipped = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(clipped))
        {
            g.Clear(WfColor.White);
            g.Clip = new WfRegion();
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(2, 2, 8, 8));
        }
        using var reference = new WfBitmap(20, 20);
        using (var g = WfGraphics.FromImage(reference))
        {
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(2, 2, 8, 8));
        }
        Assert.Equal(Pixels(reference), Pixels(clipped));
        Assert.Equal(reference.PaintVersion, clipped.PaintVersion);
    }

    [Fact]
    public void Skipped_Fills_Skip_Attribution()
    {
        // Attribution runs after the no-op skips: transparent and
        // clear-color fills (even >50000px) never capture a stack.
        bool saved = WfCounters.PerfEnabled;
        WfCounters.TakeBigFills(out long _, out long _, out string? _, out int _);
        try
        {
            WfCounters.PerfEnabled = true;
            using var bmp = new WfBitmap(300, 300);
            using var g = WfGraphics.FromImage(bmp);
            g.Clear(WfColor.White);
            g.FillRectangle(new WfSolidBrush(WfColor.FromArgb(0, 255, 0, 0)), new WfRectangleF(0, 0, 300, 300));
            g.FillRectangle(new WfSolidBrush(WfColor.White), new WfRectangleF(0, 0, 300, 300));
            WfCounters.TakeBigFills(out long skippedN, out long _, out string? _, out int _);
            Assert.Equal(0, skippedN);
            g.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 300, 300));
            WfCounters.TakeBigFills(out long realN, out long _, out string? _, out int _);
            Assert.Equal(1, realN);
        }
        finally
        {
            WfCounters.PerfEnabled = saved;
            WfCounters.TakeBigFills(out long _, out long _, out string? _, out int _);
        }
    }
}
