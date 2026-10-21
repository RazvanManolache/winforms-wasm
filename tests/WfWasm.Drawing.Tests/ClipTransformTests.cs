extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfCombineMode = WfDraw::System.Drawing.Drawing2D.CombineMode;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfRegion = WfDraw::System.Drawing.Region;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;

// GDI+ clip space contract: setters take WORLD rects (mapped through the
// current transform into device storage), getters report WORLD. Telerik
// grid paint nests translated SetClip(local Bounds) calls; storing the
// world rect raw emptied the intersection and hid every cell.
public sealed class ClipTransformTests
{
    [Fact]
    public void SetClip_Under_Translate_Maps_To_Device()
    {
        using var bitmap = new WfBitmap(20, 20);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.TranslateTransform(10f, 5f);
        graphics.SetClip(new WfRectangleF(0, 0, 8, 8), WfCombineMode.Intersect);
        Assert.Equal(new WfRectangleF(0, 0, 8, 8), graphics.ClipBounds);
        graphics.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 8, 8));
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(10, 5).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(17, 12).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(9, 5).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(18, 5).ToArgb());
    }

    [Fact]
    public void Nested_Intersect_Clips_Combine_In_Device_Space()
    {
        using var bitmap = new WfBitmap(30, 20);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.TranslateTransform(10f, 0f);
        graphics.SetClip(new WfRectangleF(0, 0, 15, 20), WfCombineMode.Intersect);
        graphics.TranslateTransform(5f, 0f);
        graphics.SetClip(new WfRectangleF(0, 0, 10, 20), WfCombineMode.Intersect);
        graphics.FillRectangle(new WfSolidBrush(WfColor.Blue), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), bitmap.GetPixel(15, 10).ToArgb());
        Assert.Equal(WfColor.Blue.ToArgb(), bitmap.GetPixel(24, 10).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(25, 10).ToArgb());
    }

    [Fact]
    public void Clip_Property_Roundtrips_World_Space_Under_Transform()
    {
        using var bitmap = new WfBitmap(20, 20);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.TranslateTransform(7f, 3f);
        graphics.Clip = new WfRegion(new WfRectangleF(1, 2, 4, 5));
        Assert.Equal(new WfRectangleF(1, 2, 4, 5), graphics.Clip.GetBounds(null));
        Assert.Equal(new WfRectangleF(1, 2, 4, 5), graphics.ClipBounds);
    }

    [Fact]
    public void ExcludeClip_Under_Translate_Maps_To_Device()
    {
        using var bitmap = new WfBitmap(30, 20);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.TranslateTransform(10f, 0f);
        graphics.SetClip(new WfRectangleF(0, 0, 20, 20), WfCombineMode.Replace);
        graphics.ExcludeClip(new WfRectangleF(0, 0, 5, 20));
        graphics.FillRectangle(new WfSolidBrush(WfColor.Red), new WfRectangleF(0, 0, 20, 20));
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(14, 10).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(15, 10).ToArgb());
        Assert.Equal(WfColor.Red.ToArgb(), bitmap.GetPixel(29, 10).ToArgb());
    }

    [Fact]
    public void SetClip_Identity_Transform_Unchanged()
    {
        using var bitmap = new WfBitmap(10, 10);
        using var graphics = WfGraphics.FromImage(bitmap);
        graphics.Clear(WfColor.White);
        graphics.SetClip(new WfRectangleF(2, 2, 4, 4), WfCombineMode.Replace);
        Assert.Equal(new WfRectangleF(2, 2, 4, 4), graphics.ClipBounds);
        graphics.FillRectangle(new WfSolidBrush(WfColor.Lime), new WfRectangleF(0, 0, 10, 10));
        Assert.Equal(WfColor.Lime.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(9, 9).ToArgb());
    }
}
