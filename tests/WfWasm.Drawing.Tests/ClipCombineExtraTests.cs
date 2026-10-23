extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfCombineMode = WfDraw::System.Drawing.Drawing2D.CombineMode;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;

// Extra GDI+ clip combine contract: disjoint intersection paints
// nothing, ResetClip restores the full surface, Union adds area,
// IsVisible agrees with painted pixels, and double Exclude carves
// two holes. Ground truth is always bitmap pixels, never clip state.
public sealed class ClipCombineExtraTests
{
    [Fact]
    public void Disjoint_Intersect_Paints_Nothing()
    {
        using var bitmap = new WfBitmap(20, 20);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.SetClip(new WfRectangleF(0, 0, 5, 5), WfCombineMode.Replace);
        graphics.SetClip(new WfRectangleF(10, 10, 5, 5), WfCombineMode.Intersect);
        graphics.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(12, 12).ToArgb());
        Assert.False(graphics.IsVisible(new WfRectangleF(0, 0, 20, 20)));
        Assert.True(graphics.IsVisibleClipEmpty);
    }

    [Fact]
    public void ResetClip_Restores_Full_Surface()
    {
        using var bitmap = new WfBitmap(10, 10);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.SetClip(new WfRectangleF(2, 2, 2, 2), WfCombineMode.Replace);
        graphics.ResetClip();
        graphics.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 10, 10));
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(9, 9).ToArgb());
        Assert.True(graphics.IsVisible(0, 0, 10, 10));
    }

    [Fact]
    public void Union_Combine_Adds_Area()
    {
        using var bitmap = new WfBitmap(20, 10);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.SetClip(new WfRectangleF(0, 0, 5, 10), WfCombineMode.Replace);
        graphics.SetClip(new WfRectangleF(15, 0, 5, 10), WfCombineMode.Union);
        graphics.FillRectangle(new WfSolidBrush(WfColor.Blue), new WfRectangleF(0, 0, 20, 10));
        Assert.Equal(WfColor.Blue.ToArgb(), bitmap.GetPixel(2, 5).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), bitmap.GetPixel(17, 5).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(10, 5).ToArgb());
    }

    [Fact]
    public void IsVisible_Rect_Agrees_With_Painted_Pixels()
    {
        using var bitmap = new WfBitmap(20, 20);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.SetClip(new WfRectangleF(5, 5, 10, 10), WfCombineMode.Replace);
        graphics.FillRectangle(new WfSolidBrush(WfColor.Lime), new WfRectangleF(0, 0, 20, 20));
        Assert.True(graphics.IsVisible(new WfRectangleF(5, 5, 10, 10)));
        Assert.True(graphics.IsVisible(5, 5, 10, 10));
        Assert.False(graphics.IsVisible(new WfRectangleF(0, 0, 5, 5)));
        Assert.False(graphics.IsVisible(0, 0, 5, 5));
        Assert.Equal(WfColor.Lime.ToArgb(), bitmap.GetPixel(5, 5).ToArgb());
        Assert.Equal(WfColor.Lime.ToArgb(), bitmap.GetPixel(14, 14).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(4, 4).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(15, 15).ToArgb());
    }

    [Fact]
    public void ExcludeClip_Twice_Carves_Two_Holes()
    {
        using var bitmap = new WfBitmap(30, 10);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.SetClip(new WfRectangleF(0, 0, 30, 10), WfCombineMode.Replace);
        graphics.ExcludeClip(new WfRectangleF(0, 0, 5, 10));
        graphics.ExcludeClip(new WfRectangleF(25, 0, 5, 10));
        graphics.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 30, 10));
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(2, 5).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(27, 5).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(10, 5).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(20, 5).ToArgb());
    }
}
