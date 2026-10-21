extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfImageLockMode = WfDraw::System.Drawing.Imaging.ImageLockMode;
using WfPixelFormat = WfDraw::System.Drawing.Imaging.PixelFormat;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfRegion = WfDraw::System.Drawing.Region;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;

// Redundant-fill skip, pixel-sink coverage (companion to FillSkipTests).
// Contract: a flat fill may skip only for the SAME bitmap whose
// PaintVersion is unchanged since its own clear with an exactly matching
// ARGB, and only when the fill is identity on those pixels (opaque in any
// mode, anything under SourceCopy, fully transparent under SourceOver).
// Every mutation path must void the record; read-only paths must preserve
// it. Every test asserts resulting pixels; version assertions additionally
// pin skip-vs-repaint. Pixel assertions hold under either per-surface
// shape (most-recent-only or full per-surface memory); only
// FillSkipTests.Alternating_* requires full per-surface memory.
public sealed class FillSkipSinksTests
{
    [Fact]
    public void SetPixel_Voids_Redundant_Skip()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        bmp.SetPixel(1, 1, WfColor.Black);
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void LockBits_WriteBack_Voids_Redundant_Skip()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        var data = bmp.LockBits(
            new WfRectangle(0, 0, 4, 4),
            WfImageLockMode.WriteOnly,
            WfPixelFormat.Format32bppArgb);
        try
        {
            // BGRA bytes: paint pixel (2,2) opaque black through the buffer.
            int o = 2 * data.Stride + 2 * 4;
            byte[] patch = [0, 0, 0, 255];
            System.Runtime.InteropServices.Marshal.Copy(
                patch, 0, IntPtr.Add(data.Scan0, o), patch.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void LockBits_ReadOnly_Preserves_Redundant_Skip()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int v0 = bmp.PaintVersion;
        var data = bmp.LockBits(
            new WfRectangle(0, 0, 4, 4),
            WfImageLockMode.ReadOnly,
            WfPixelFormat.Format32bppArgb);
        try
        {
            byte[] raw = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            Assert.Equal(255, raw[0]);
            Assert.Equal(255, raw[3]);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        Assert.Equal(v0, bmp.PaintVersion);
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(v0, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void PartialRect_DifferentColor_Voids_FullFill_Skip()
    {
        using var bmp = new WfBitmap(8, 8);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var black = new WfSolidBrush(WfColor.Black);
        g.FillRectangle(black, new WfRectangle(2, 2, 2, 2));
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 8, 8));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void PartialRect_SameColor_AfterClear_Skips()
    {
        using var bmp = new WfBitmap(8, 8);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        int v0 = bmp.PaintVersion;
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(2, 2, 2, 2));

        Assert.Equal(v0, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void FillRegion_Voids_Redundant_Skip()
    {
        using var bmp = new WfBitmap(8, 8);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var black = new WfSolidBrush(WfColor.Black);
        using var region = new WfRegion(new WfRectangle(1, 1, 2, 2));
        g.FillRegion(black, region);
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 8, 8));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(7, 7).ToArgb());
    }

    [Fact]
    public void SourceOver_Translucent_Blends_Over_Cleared_Surface()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var halfRed = new WfSolidBrush(WfColor.FromArgb(128, 255, 0, 0));
        g.FillRectangle(halfRed, new WfRectangle(0, 0, 4, 4));

        // SourceOver blend of 50% red over opaque white: R stays 255,
        // G/B halve ((255*127)/255), alpha saturates to opaque.
        int expected = WfColor.FromArgb(255, 255, 127, 127).ToArgb();
        Assert.Equal(expected, bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(expected, bmp.GetPixel(3, 3).ToArgb());

        // The blend is a real write: a later opaque match must repaint.
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void Translucent_SameColor_SourceOver_Must_Blend_Not_Skip()
    {
        // SourceOver blend of a translucent color over itself is NOT
        // identity (alpha accumulates: 128 over 128 -> 191), so the skip
        // must never fire for translucent SourceOver fills even when the
        // ARGB exactly matches the surface's clear color.
        var translucent = WfColor.FromArgb(128, 255, 0, 0);
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(translucent);
        Assert.Equal(translucent.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        using var brush = new WfSolidBrush(translucent);
        g.FillRectangle(brush, new WfRectangle(0, 0, 4, 4));

        int expected = WfColor.FromArgb(191, 255, 0, 0).ToArgb();
        Assert.NotEqual(translucent.ToArgb(), expected);
        Assert.Equal(expected, bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(expected, bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void SourceCopy_Translucent_SameColor_Writes_Exact()
    {
        // SourceCopy replace with identical pixels is identity, so the
        // repeated fill holds exact pixels whether skipped or repainted.
        var translucent = WfColor.FromArgb(128, 255, 0, 0);
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var brush = new WfSolidBrush(translucent);
        g.CompositingMode = WfDraw::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.FillRectangle(brush, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(translucent.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        g.FillRectangle(brush, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(translucent.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(translucent.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void DrawImage_Voids_Target_Freshness()
    {
        using var src = new WfBitmap(4, 4);
        using var gs = WfGraphics.FromImage(src);
        gs.Clear(WfColor.Black);
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        g.DrawImage(src, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Black.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void DrawImage_Preserves_Source_Freshness()
    {
        using var src = new WfBitmap(4, 4);
        using var bmp = new WfBitmap(4, 4);
        using var gs = WfGraphics.FromImage(src);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.Black);
        gs.Clear(WfColor.White);
        int v0 = src.PaintVersion;
        g.DrawImage(src, new WfRectangle(0, 0, 4, 4));

        // The draw landed on the target but must not version the source.
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
        Assert.Equal(v0, src.PaintVersion);

        // Source still matches its own clear: the redundant fill skips.
        using var white = new WfSolidBrush(WfColor.White);
        gs.FillRectangle(white, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(v0, src.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), src.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), src.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void Clear_Replaces_Freshness_Rearms_Skip()
    {
        using var bmp = new WfBitmap(4, 4);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.Black);
        using var white = new WfSolidBrush(WfColor.White);
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        g.Clear(WfColor.White);
        int v0 = bmp.PaintVersion;
        g.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        Assert.Equal(v0, bmp.PaintVersion);
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void CrossBitmap_SameColor_Write_Voids_Only_That_Surface()
    {
        using var a = new WfBitmap(4, 4);
        using var b = new WfBitmap(4, 4);
        using var ga = WfGraphics.FromImage(a);
        using var gb = WfGraphics.FromImage(b);
        ga.Clear(WfColor.White);
        gb.Clear(WfColor.White);
        b.SetPixel(0, 0, WfColor.Black);
        using var white = new WfSolidBrush(WfColor.White);
        gb.FillRectangle(white, new WfRectangle(0, 0, 4, 4));
        Assert.Equal(WfColor.White.ToArgb(), b.GetPixel(0, 0).ToArgb());
        ga.FillRectangle(white, new WfRectangle(0, 0, 4, 4));

        // Pixels either way: A is already white (skipped under full
        // per-surface memory, redundantly repainted under most-recent
        // only); B was really repainted by the fill above.
        Assert.Equal(WfColor.White.ToArgb(), a.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), a.GetPixel(3, 3).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), b.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), b.GetPixel(3, 3).ToArgb());
    }
}
