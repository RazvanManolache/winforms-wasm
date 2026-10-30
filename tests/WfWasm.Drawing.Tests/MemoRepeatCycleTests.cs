extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;

// Two-layer composite freshness across repeated mutation cycles: the
// Paint.NET background-thumbnail analogue at Drawing level. Two
// independently-colored sources composite onto one canvas; a mutation
// of the second source ("stroke on layer 2") must show fresh on both
// a fresh and a reused target, and restoring the source ("undo") must
// reproduce the pre-stroke bytes exactly. Repeating the cycle guards
// the one-shot stash gate (NoteRepeat): invalidation must work on
// every cycle, not just the first.
public sealed class MemoRepeatCycleTests
{
    private static WfBitmap Solid(int w, int h, WfColor color)
    {
        var bmp = new WfBitmap(w, h);
        using (var g = WfGraphics.FromImage(bmp))
            g.FillRectangle(new WfSolidBrush(color), new WfRectangle(0, 0, w, h));
        return bmp;
    }

    private static void Compose(WfBitmap canvas, WfBitmap left, WfBitmap right)
    {
        using (var g = WfGraphics.FromImage(canvas))
        {
            g.DrawImage(left, new WfRectangle(0, 0, 6, 6));
            g.DrawImage(right, new WfRectangle(6, 0, 6, 6));
        }
    }

    [Fact]
    public void Second_Source_Mutation_Stays_Fresh_On_Reused_Target()
    {
        using var srcA = Solid(6, 6, WfColor.Red);
        using var srcB = Solid(6, 6, WfColor.Green);
        using var canvas = Solid(12, 6, WfColor.White);
        Compose(canvas, srcA, srcB);
        byte[] baseline = canvas.ToRgbaBytes();
        Assert.Equal(WfColor.Red.ToArgb(), canvas.GetPixel(1, 1).ToArgb());
        Assert.Equal(WfColor.Green.ToArgb(), canvas.GetPixel(7, 1).ToArgb());

        // Stroke on the second layer via both mutation paths.
        srcB.SetPixel(1, 1, WfColor.Magenta);
        using (var g = WfGraphics.FromImage(srcB))
            g.FillRectangle(new WfSolidBrush(WfColor.Cyan), new WfRectangle(3, 3, 2, 2));
        Compose(canvas, srcA, srcB);
        Assert.Equal(WfColor.Magenta.ToArgb(), canvas.GetPixel(7, 1).ToArgb());
        Assert.Equal(WfColor.Cyan.ToArgb(), canvas.GetPixel(9, 4).ToArgb());
        // First layer region is untouched by the second-layer stroke.
        Assert.Equal(WfColor.Red.ToArgb(), canvas.GetPixel(1, 1).ToArgb());
        Assert.NotEqual(baseline, canvas.ToRgbaBytes());

        // Undo: restore the exact stroked pixels.
        srcB.SetPixel(1, 1, WfColor.Green);
        using (var g = WfGraphics.FromImage(srcB))
            g.FillRectangle(new WfSolidBrush(WfColor.Green), new WfRectangle(3, 3, 2, 2));
        Compose(canvas, srcA, srcB);
        Assert.Equal(baseline, canvas.ToRgbaBytes());
    }

    [Fact]
    public void Repeat_Cycle_Invalidates_Stash_Every_Time()
    {
        using var srcA = Solid(6, 6, WfColor.Blue);
        using var srcB = Solid(6, 6, WfColor.Yellow);
        // Drive the same composition through the one-shot stash gate
        // (first sighting direct, repeat stashed, then recalled).
        for (int i = 0; i < 3; i++)
        {
            using var warm = Solid(12, 6, WfColor.White);
            Compose(warm, srcA, srcB);
        }
        using var canvas = Solid(12, 6, WfColor.White);
        Compose(canvas, srcA, srcB);
        byte[] baseline = canvas.ToRgbaBytes();

        // Second stroke after the layer is stashed: must miss, not recall.
        srcB.SetPixel(0, 0, WfColor.Black);
        using var canvas2 = Solid(12, 6, WfColor.White);
        Compose(canvas2, srcA, srcB);
        Assert.Equal(WfColor.Black.ToArgb(), canvas2.GetPixel(6, 0).ToArgb());
        Assert.NotEqual(baseline, canvas2.ToRgbaBytes());

        // Undo restores the stashed composition bit-exactly.
        srcB.SetPixel(0, 0, WfColor.Yellow);
        using var canvas3 = Solid(12, 6, WfColor.White);
        Compose(canvas3, srcA, srcB);
        Assert.Equal(baseline, canvas3.ToRgbaBytes());

        // One more full cycle: invalidation is not one-shot.
        srcB.SetPixel(5, 5, WfColor.Black);
        using var canvas4 = Solid(12, 6, WfColor.White);
        Compose(canvas4, srcA, srcB);
        Assert.Equal(WfColor.Black.ToArgb(), canvas4.GetPixel(11, 5).ToArgb());
        srcB.SetPixel(5, 5, WfColor.Yellow);
        using var canvas5 = Solid(12, 6, WfColor.White);
        Compose(canvas5, srcA, srcB);
        Assert.Equal(baseline, canvas5.ToRgbaBytes());
    }
}
