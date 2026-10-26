extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfPointF = WfDraw::System.Drawing.PointF;
using WPath = WfDraw::System.Drawing.Drawing2D.GraphicsPath;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;

// GDI+ connect rule: appending a line segment to a non-empty figure joins
// the previous endpoint to the new segment start. The old AddLine dropped
// the start point, collapsing disjoint multi-line figures (chart round-rect
// bars became triangles). Contiguous segments and post-close figures are
// unaffected by the join.
public sealed class AddLineConnectionTests
{
    [Fact]
    public void Disjoint_Second_Line_Inserts_Join_Point()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        path.AddLine(10f, 10f, 0f, 10f);

        Assert.Equal(4, path.PointCount);
        Assert.Equal(new WfPointF(0f, 0f), path.PathPoints[0]);
        Assert.Equal(new WfPointF(10f, 0f), path.PathPoints[1]);
        Assert.Equal(new WfPointF(10f, 10f), path.PathPoints[2]);
        Assert.Equal(new WfPointF(0f, 10f), path.PathPoints[3]);
        Assert.Equal(new byte[] { 0, 1, 1, 1 }, path.PathTypes);
    }

    [Fact]
    public void Contiguous_Second_Line_Does_Not_Duplicate_Joint()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        path.AddLine(10f, 0f, 10f, 10f);

        Assert.Equal(3, path.PointCount);
        Assert.Equal(new WfPointF(10f, 10f), path.PathPoints[2]);
    }

    [Fact]
    public void Line_After_CloseFigure_Starts_New_Figure()
    {
        using var path = new WPath();
        path.AddLine(0f, 0f, 10f, 0f);
        path.CloseFigure();
        path.AddLine(20f, 20f, 30f, 20f);

        Assert.Equal(4, path.PointCount);
        Assert.Equal((byte)0, path.PathTypes[2]);
        Assert.Equal(new WfPointF(20f, 20f), path.PathPoints[2]);
        Assert.Equal(new WfPointF(30f, 20f), path.PathPoints[3]);
    }

    [Fact]
    public void Chart_Bar_Path_Fills_Bottom_Right_Corner()
    {
        // Zero-radius round rect: top edge + bottom edge as disjoint lines.
        using var path = new WPath();
        path.AddLine(2f, 2f, 18f, 2f);
        path.AddLine(18f, 18f, 2f, 18f);
        path.CloseFigure();

        using var bmp = new WfBitmap(20, 20);
        using var g = WfGraphics.FromImage(bmp);
        g.Clear(WfColor.White);
        using var red = new WfSolidBrush(WfColor.Red);
        g.FillPath(red, path);

        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(17, 17).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bmp.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
    }
}
