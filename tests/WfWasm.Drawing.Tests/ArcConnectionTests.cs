extern alias WfDraw;

using WPath = WfDraw::System.Drawing.Drawing2D.GraphicsPath;
using WBitmap = WfDraw::System.Drawing.Bitmap;
using WGraphics = WfDraw::System.Drawing.Graphics;
using WPen = WfDraw::System.Drawing.Pen;

public sealed class ArcConnectionTests
{
    [Fact]
    public void Appended_Arc_Connects_Existing_Figure_To_Its_Start()
    {
        using var path = new WPath();
        path.AddLine(0, 0, 5, 0);
        path.AddArc(20, 10, 10, 10, 0, 90);

        Assert.Equal(6, path.PointCount);
        Assert.Equal((byte)1, path.PathTypes[2]);
        Assert.Equal(30f, path.PathPoints[2].X);
        Assert.Equal(15f, path.PathPoints[2].Y);
        Assert.Equal((byte)3, path.PathTypes[3]);
    }

    [Theory]
    [InlineData(270)]
    [InlineData(-270)]
    public void Arc_Chunks_Do_Not_Add_Connectors_Inside_A_Continuous_Arc(int sweep)
    {
        using var path = new WPath();
        path.AddLine(0, 0, 5, 0);
        path.AddArc(20, 10, 10, 10, 0, sweep);

        Assert.Equal(12, path.PointCount);
        Assert.Equal(2, path.PathTypes.Count(t => t == 1));
        Assert.All(path.PathTypes.Skip(3), t => Assert.Equal((byte)3, t));
    }

    [Fact]
    public void Arc_Starting_At_Existing_Endpoint_Does_Not_Duplicate_It()
    {
        using var path = new WPath();
        path.AddLine(0, 0, 30, 15);
        path.AddArc(20, 10, 10, 10, 0, 90);

        Assert.Equal(5, path.PointCount);
        Assert.Equal((byte)3, path.PathTypes[2]);
    }

    [Fact]
    public void Arc_After_A_Closed_Figure_Starts_A_New_Figure()
    {
        using var path = new WPath();
        path.AddRectangle(new Rectangle(0, 0, 5, 5));
        path.AddArc(20, 10, 10, 10, 0, 90);

        Assert.Equal((byte)0, path.PathTypes[4]);
        Assert.Equal(30f, path.PathPoints[4].X);
        Assert.Equal(15f, path.PathPoints[4].Y);
    }

    [Fact]
    public void Four_Corner_Arcs_Produce_Straight_Button_Sides()
    {
        using var path = new WPath();
        path.AddArc(10, 10, 10, 10, 180, 90);
        path.AddArc(190, 10, 10, 10, 270, 90);
        path.AddArc(190, 26, 10, 10, 0, 90);
        path.AddArc(10, 26, 10, 10, 90, 90);
        path.CloseFigure();

        Assert.Equal(3, path.PathTypes.Count(t => t == 1));
        using var bitmap = new WBitmap(215, 46);
        using var graphics = WGraphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        using var pen = new WPen(Color.Black);
        graphics.DrawPath(pen, path);

        for (int x = 25; x <= 180; x++)
        {
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(x, 10).ToArgb());
            Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(x, 36).ToArgb());
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(x, 13).ToArgb());
            Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(x, 33).ToArgb());
        }
    }
}
