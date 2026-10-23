extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;

// Bitmap.ShiftPixels (retained scroll reuse): pixel (x,y) moves to
// (x+dx,y+dy); exposed strips keep stale bytes for the caller to repaint.
public sealed class ShiftPixelsTests
{
    private static WfBitmap Pattern(int w, int h)
    {
        var bmp = new WfBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                bmp.SetPixel(x, y, WfColor.FromArgb(x + y * 16, x + y * 16, x + y * 16));
        return bmp;
    }

    private static int At(WfBitmap bmp, int x, int y) => bmp.GetPixel(x, y).ToArgb();

    [Fact]
    public void Shift_Right_Moves_Pixels()
    {
        using var bmp = Pattern(4, 3);
        int v00 = At(bmp, 0, 0), v10 = At(bmp, 1, 0), v22 = At(bmp, 2, 2);
        bmp.ShiftPixels(1, 0);
        Assert.Equal(v00, At(bmp, 1, 0));
        Assert.Equal(v10, At(bmp, 2, 0));
        Assert.Equal(v22, At(bmp, 3, 2)); // old (2,2) slid into (3,2); old (3,2) fell off
    }

    [Fact]
    public void Shift_Down_Moves_Pixels()
    {
        using var bmp = Pattern(4, 3);
        int v00 = At(bmp, 0, 0), v11 = At(bmp, 1, 1);
        bmp.ShiftPixels(0, 1);
        Assert.Equal(v00, At(bmp, 0, 1));
        Assert.Equal(v11, At(bmp, 1, 2));
    }

    [Fact]
    public void Shift_Negative_Moves_Pixels()
    {
        using var bmp = Pattern(4, 3);
        int v31 = At(bmp, 3, 1), v12 = At(bmp, 1, 2);
        bmp.ShiftPixels(-1, -1);
        Assert.Equal(v31, At(bmp, 2, 0)); // old (3,1) -> (2,0)
        Assert.Equal(v12, At(bmp, 0, 1)); // old (1,2) -> (0,1)
    }

    [Fact]
    public void Shift_Zero_Is_NoOp_Without_Bump()
    {
        using var bmp = Pattern(4, 3);
        int before = bmp.PaintVersion;
        bmp.ShiftPixels(0, 0);
        Assert.Equal(before, bmp.PaintVersion);
        using var fresh = Pattern(4, 3);
        Assert.Equal(At(fresh, 1, 1), At(bmp, 1, 1));
    }

    [Fact]
    public void Shift_Bumps_Version()
    {
        using var bmp = Pattern(4, 3);
        int before = bmp.PaintVersion;
        bmp.ShiftPixels(1, 1);
        Assert.Equal(before + 1, bmp.PaintVersion);
    }

    [Fact]
    public void Shift_Right_Exposed_Strip_Keeps_Old_Bytes()
    {
        using var bmp = Pattern(4, 3);
        int old00 = At(bmp, 0, 0), old01 = At(bmp, 0, 1);
        bmp.ShiftPixels(2, 0);
        Assert.Equal(At(bmp, 0, 0), old00); // stale, caller repaints
        Assert.Equal(At(bmp, 0, 1), old01);
        Assert.Equal(old00, At(bmp, 2, 0)); // old (0,0) now at (2,0)
    }
}
