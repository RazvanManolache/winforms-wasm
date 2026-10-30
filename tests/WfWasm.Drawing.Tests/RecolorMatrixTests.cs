extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfGraphicsUnit = WfDraw::System.Drawing.GraphicsUnit;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfImageAttributes = WfDraw::System.Drawing.Imaging.ImageAttributes;
using WfColorMatrix = WfDraw::System.Drawing.Imaging.ColorMatrix;
using WfColorMap = WfDraw::System.Drawing.Imaging.ColorMap;

// Representative recolor fixtures: vendor demos never drive DrawImage
// with ImageAttributes, so the matrix/remap/clear paths below are
// pinned here to exact pixels. Matrix convention is standard GDI+
// (input rows R,G,B,A,W; output columns), applied per pixel with
// round-half-up clamping.
public sealed class RecolorMatrixTests
{
    private static float[][] SwapRB() => new float[][]
    {
        new float[] { 0, 0, 1, 0, 0 },
        new float[] { 0, 1, 0, 0, 0 },
        new float[] { 1, 0, 0, 0, 0 },
        new float[] { 0, 0, 0, 1, 0 },
        new float[] { 0, 0, 0, 0, 1 },
    };

    private static float[][] Grayscale() => new float[][]
    {
        new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
        new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
        new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
        new float[] { 0, 0, 0, 1, 0 },
        new float[] { 0, 0, 0, 0, 1 },
    };

    [Fact]
    public void DrawImage_SwapMatrix_Swaps_Channels_Exactly()
    {
        using var src = new WfBitmap(2, 2);
        using (var g = WfGraphics.FromImage(src))
            g.Clear(WfColor.FromArgb(255, 200, 100, 50));
        using var dst = new WfBitmap(2, 2);
        using var attrs = new WfImageAttributes();
        attrs.SetColorMatrix(new WfColorMatrix(SwapRB()));
        using (var g2 = WfGraphics.FromImage(dst))
            g2.DrawImage(src, new WfRectangle(0, 0, 2, 2), 0f, 0f, 2f, 2f,
                WfGraphicsUnit.Pixel, attrs);
        Assert.Equal(WfColor.FromArgb(255, 50, 100, 200).ToArgb(),
            dst.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.FromArgb(255, 50, 100, 200).ToArgb(),
            dst.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void DrawImage_GrayscaleMatrix_Matches_Weights()
    {
        using var src = new WfBitmap(2, 2);
        using (var g = WfGraphics.FromImage(src))
            g.Clear(WfColor.FromArgb(255, 200, 100, 50));
        using var dst = new WfBitmap(2, 2);
        using var attrs = new WfImageAttributes();
        attrs.SetColorMatrix(new WfColorMatrix(Grayscale()));
        using (var g2 = WfGraphics.FromImage(dst))
            g2.DrawImage(src, new WfRectangle(0, 0, 2, 2), 0f, 0f, 2f, 2f,
                WfGraphicsUnit.Pixel, attrs);
        // 200*0.299 + 100*0.587 + 50*0.114 = 124.2 -> 124.
        WfColor after = dst.GetPixel(0, 0);
        Assert.Equal(255, after.A);
        Assert.Equal(124, after.R);
        Assert.Equal(124, after.G);
        Assert.Equal(124, after.B);
    }

    [Fact]
    public void DrawImage_RemapTable_Replaces_Before_Matrix()
    {
        using var src = new WfBitmap(2, 2);
        using (var g = WfGraphics.FromImage(src))
            g.Clear(WfColor.FromArgb(255, 255, 0, 0));
        using var dst = new WfBitmap(2, 2);
        using var attrs = new WfImageAttributes();
        attrs.SetRemapTable(new[]
        {
            new WfColorMap
            {
                OldColor = WfColor.FromArgb(255, 255, 0, 0),
                NewColor = WfColor.FromArgb(255, 0, 0, 255),
            },
        });
        using (var g2 = WfGraphics.FromImage(dst))
            g2.DrawImage(src, new WfRectangle(0, 0, 2, 2), 0f, 0f, 2f, 2f,
                WfGraphicsUnit.Pixel, attrs);
        Assert.Equal(WfColor.FromArgb(255, 0, 0, 255).ToArgb(),
            dst.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void ClearColorMatrix_Restores_Identity()
    {
        using var src = new WfBitmap(2, 2);
        using (var g = WfGraphics.FromImage(src))
            g.Clear(WfColor.FromArgb(255, 200, 100, 50));
        using var dst = new WfBitmap(2, 2);
        using var attrs = new WfImageAttributes();
        attrs.SetColorMatrix(new WfColorMatrix(SwapRB()));
        attrs.ClearColorMatrix();
        using (var g2 = WfGraphics.FromImage(dst))
            g2.DrawImage(src, new WfRectangle(0, 0, 2, 2), 0f, 0f, 2f, 2f,
                WfGraphicsUnit.Pixel, attrs);
        Assert.Equal(WfColor.FromArgb(255, 200, 100, 50).ToArgb(),
            dst.GetPixel(0, 1).ToArgb());
    }
}
