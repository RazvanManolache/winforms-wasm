extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfTextureBrush = WfDraw::System.Drawing.TextureBrush;

// Redundant-fill skip: freshness must describe the pixels of THAT
// surface (version-guarded), never a global. The global scheme failed
// across bitmaps (clear B black, clear A white, fill B white left B
// black) and after pixel sinks that never staled it (texture fills,
// per-pixel paths). Every test below verifies resulting pixels; the
// red cases fail on the global scheme and pass on per-surface
// versioning. The rest guard the skip's valid cases and side paths.
public sealed class FillSkipTests
{
    [Fact]
    public void CrossBitmap_ClearAWhite_FillBWhite_PaintsB()
    {
        using var a = new WfBitmap(4, 4);
        using var b = new WfBitmap(4, 4);
        using var ga = WfGraphics.FromImage(a);
        using var gb = WfGraphics.FromImage(b);
        gb.Clear(WfColor.Black);
        ga.Clear(WfColor.White);
        using var white = new WfSolidBrush(WfColor.White);
        gb.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(WfColor.White.ToArgb(), b.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), b.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), a.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Intervening_TextureFill_Voids_Redundant_Skip()
    {
        using var tile = new WfBitmap(2, 2);
        tile.SetPixel(0, 0, WfColor.Black);
        tile.SetPixel(1, 1, WfColor.Black);
        tile.SetPixel(1, 0, WfColor.White);
        tile.SetPixel(0, 1, WfColor.White);
        using var bmp = new WfBitmap(8, 8);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var texture = new WfTextureBrush(tile);
        g.FillRectangle(texture, new WfRectangle(0, 0, 8, 8));
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 8, 8));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(4, 4).ToArgb());
    }

    [Fact]
    public void SharedGraphics_Skip_Holds_Until_Write()
    {
        using var bmp = new WfBitmap(8, 8);
        using var g1 = WfGraphics.FromImage(bmp);
        g1.Clear(WfColor.White);
        int v0 = bmp.PaintVersion;
        using (var g2 = WfGraphics.FromImage(bmp))
        {
            using var white = new WfSolidBrush(WfColor.White);
            g2.FillRectangle(white, new WfRectangle(0, 0, 8, 8));
            // Redundant: skipped, version exact, pixels hold.
            Assert.Equal(v0, bmp.PaintVersion);
            Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        }
        using (var g3 = WfGraphics.FromImage(bmp))
        {
            using var black = new WfSolidBrush(WfColor.Black);
            g3.FillRectangle(black, new WfRectangle(0, 0, 1, 1));
        }
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        using var g4 = WfGraphics.FromImage(bmp);
        using var white2 = new WfSolidBrush(WfColor.White);
        g4.FillRectangle(white2, new WfRectangle(0, 0, 8, 8));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void Clipped_Fill_Still_Clips()
    {
        using var bmp = new WfBitmap(8, 8);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        g.SetClip(new WfRectangle(2, 2, 2, 2));
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 8, 8));
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        using var black = new WfSolidBrush(WfColor.Black);
        g.FillRectangle(black, new WfRectangle(0, 0, 8, 8));

        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void Transparent_SourceCopy_Writes_SourceOver_NoOp()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var transparent = new WfSolidBrush(WfColor.Transparent);
        g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.FillRectangle(transparent, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.Transparent.ToArgb(), bmp.GetPixel(0, 0).ToArgb());

        g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceOver;
        g.Clear(WfColor.White);
        g.FillRectangle(transparent, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Alternating_Surfaces_Each_Skip_Only_Own_Clear()
    {
        using var a = new WfBitmap(4, 4);
        using var b = new WfBitmap(4, 4);
        using var ga = WfGraphics.FromImage(a);
        using var gb = WfGraphics.FromImage(b);
        ga.Clear(WfColor.White);
        gb.Clear(WfColor.Black);
        using var white = new WfSolidBrush(WfColor.White);
        using var black = new WfSolidBrush(WfColor.Black);
        int va = a.PaintVersion;
        int vb = b.PaintVersion;
        ga.FillRectangle(white, new WfRectangle(0, 0, 4, 4));
        gb.FillRectangle(black, new WfRectangle(0, 0, 4, 4));
        // Both redundant: versions exact.
        Assert.Equal(va, a.PaintVersion);
        Assert.Equal(vb, b.PaintVersion);
        // Opposite colors must paint (and void that surface only).
        ga.FillRectangle(black, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.Black.ToArgb(), a.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), b.GetPixel(0, 0).ToArgb());
        int vb2 = b.PaintVersion;
        gb.FillRectangle(white, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.White.ToArgb(), b.GetPixel(0, 0).ToArgb());
        Assert.NotEqual(vb2, b.PaintVersion);
    }
}
