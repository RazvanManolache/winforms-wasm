extern alias WfDraw;

using WfBitmap = WfDraw::System.Drawing.Bitmap;
using WfColor = WfDraw::System.Drawing.Color;
using WfFont = WfDraw::System.Drawing.Font;
using WfGraphics = WfDraw::System.Drawing.Graphics;
using WfGraphicsUnit = WfDraw::System.Drawing.GraphicsUnit;
using WfImage = WfDraw::System.Drawing.Image;
using WfPoint = WfDraw::System.Drawing.Point;
using WfPointF = WfDraw::System.Drawing.PointF;
using WfRectangle = WfDraw::System.Drawing.Rectangle;
using WfRectangleF = WfDraw::System.Drawing.RectangleF;
using WfSize = WfDraw::System.Drawing.Size;
using WfSolidBrush = WfDraw::System.Drawing.SolidBrush;
using WfKnownColor = WfDraw::System.Drawing.KnownColor;
using WfPixelFormat = WfDraw::System.Drawing.Imaging.PixelFormat;
using WfDitherType = WfDraw::System.Drawing.Imaging.DitherType;
using WfPaletteType = WfDraw::System.Drawing.Imaging.PaletteType;
using WfColorPalette = WfDraw::System.Drawing.Imaging.ColorPalette;
using WfImageAttributes = WfDraw::System.Drawing.Imaging.ImageAttributes;
using WfColorMatrix = WfDraw::System.Drawing.Imaging.ColorMatrix;
using WfBlur = WfDraw::System.Drawing.Imaging.Effects.BlurEffect;
using WfSharpen = WfDraw::System.Drawing.Imaging.Effects.SharpenEffect;
using WfContrast = WfDraw::System.Drawing.Imaging.Effects.ContrastCurveEffect;
using WfCurveChannel = WfDraw::System.Drawing.Imaging.Effects.CurveChannel;
using WfShadow = WfDraw::System.Drawing.Imaging.Effects.ShadowCurveEffect;
using WfPathGradientBrush = WfDraw::System.Drawing.Drawing2D.PathGradientBrush;
using WfWrapMode = WfDraw::System.Drawing.Drawing2D.WrapMode;
using WfCoordinateSpace = WfDraw::System.Drawing.Drawing2D.CoordinateSpace;
using WfPathData = WfDraw::System.Drawing.Drawing2D.PathData;
using WfGraphicsPath = WfDraw::System.Drawing.Drawing2D.GraphicsPath;
using WfMatrix = WfDraw::System.Drawing.Drawing2D.Matrix;
using WfSystemColors = WfDraw::System.Drawing.SystemColors;
using WfSystemBrushes = WfDraw::System.Drawing.SystemBrushes;
using WfSystemPens = WfDraw::System.Drawing.SystemPens;
using WfSystemIcons = WfDraw::System.Drawing.SystemIcons;
using WfStockIconId = WfDraw::System.Drawing.StockIconId;
using WfBufferedManager = WfDraw::System.Drawing.BufferedGraphicsManager;
using WfPrivateFonts = WfDraw::System.Drawing.Text.PrivateFontCollection;
using WfFontConverter = WfDraw::System.Drawing.FontConverter;
using WfImageFormatConverter = WfDraw::System.Drawing.ImageFormatConverter;
using WfImageFormat = WfDraw::System.Drawing.Imaging.ImageFormat;

public sealed class NewSlicesTests
{
    [Fact]
    public void Blur_ZeroRadius_IsNoop_PositiveRadius_Blends()
    {
        using var bitmap = new WfBitmap(5, 5);
        using (var g = WfGraphics.FromImage(bitmap))
            g.Clear(WfColor.Black);
        bitmap.SetPixel(2, 2, WfColor.White);
        using (var blur = new WfBlur(0f, false))
            bitmap.ApplyEffect(blur, new WfRectangle(0, 0, 5, 5));
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(2, 2).ToArgb());
        using (var blur = new WfBlur(2f, false))
        {
            Assert.Equal(2f, blur.Radius);
            Assert.False(blur.ExpandEdge);
            bitmap.ApplyEffect(blur, new WfRectangle(0, 0, 5, 5));
        }
        int center = bitmap.GetPixel(2, 2).ToArgb();
        Assert.NotEqual(WfColor.White.ToArgb(), center);
        Assert.NotEqual(WfColor.Black.ToArgb(), center);
        using var e = new WfBlur(1f, true);
        e.Dispose();
    }

    [Fact]
    public void Sharpen_ZeroAmount_IsNoop()
    {
        using var bitmap = new WfBitmap(3, 3);
        using (var g = WfGraphics.FromImage(bitmap))
            g.Clear(WfColor.Gray);
        using var sharpen = new WfSharpen(1f, 0f);
        Assert.Equal(1f, sharpen.Radius);
        Assert.Equal(0f, sharpen.Amount);
        bitmap.ApplyEffect(sharpen, new WfRectangle(0, 0, 3, 3));
        Assert.Equal(WfColor.Gray.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void CurveEffect_Channel_Gating_Works()
    {
        using var bitmap = new WfBitmap(2, 2);
        var start = WfColor.FromArgb(255, 100, 150, 200);
        using (var g = WfGraphics.FromImage(bitmap))
            g.Clear(start);
        using var contrast = new WfContrast(WfCurveChannel.Red, 100);
        Assert.Equal(WfCurveChannel.Red, contrast.Channel);
        Assert.Equal(100, contrast.Contrast);
        bitmap.ApplyEffect(contrast, new WfRectangle(0, 0, 2, 2));
        WfColor after = bitmap.GetPixel(0, 0);
        Assert.Equal(150, after.G);
        Assert.Equal(200, after.B);
        Assert.NotEqual(100, after.R);
        using var shadow = new WfShadow(WfCurveChannel.All, 0);
        bitmap.ApplyEffect(shadow, new WfRectangle(0, 0, 2, 2));
    }

    [Fact]
    public void ApplyEffect_Respects_Region()
    {
        using var bitmap = new WfBitmap(4, 4);
        using (var g = WfGraphics.FromImage(bitmap))
            g.Clear(WfColor.White);
        using var invert = new WfDraw::System.Drawing.Imaging.Effects.InvertEffect();
        bitmap.ApplyEffect(invert, new WfRectangle(0, 0, 2, 2));
        Assert.Equal(WfColor.Black.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(WfColor.White.ToArgb(), bitmap.GetPixel(3, 3).ToArgb());
    }

    [Fact]
    public void ConvertFormat_Quantizes_And_Tracks_Palette()
    {
        using var bitmap = new WfBitmap(2, 2);
        bitmap.SetPixel(0, 0, WfColor.Red);
        bitmap.ConvertFormat(WfPixelFormat.Format24bppRgb);
        Assert.Equal(WfPixelFormat.Format24bppRgb, bitmap.PixelFormat);
        bitmap.ConvertFormat(WfPixelFormat.Format8bppIndexed, WfDitherType.None,
            WfPaletteType.FixedHalftone256, new WfColorPalette(0), 0f);
        Assert.Equal(WfPixelFormat.Format8bppIndexed, bitmap.PixelFormat);
        Assert.Equal(256, bitmap.Palette.Entries.Length);
        using var custom = new WfBitmap(1, 1);
        custom.SetPixel(0, 0, WfColor.Blue);
        custom.ConvertFormat(WfPixelFormat.Format4bppIndexed);
        Assert.Equal(16, custom.Palette.Entries.Length);
    }

    [Fact]
    public void DrawImage_Overloads_Draw_And_Abort_Works()
    {
        using var src = new WfBitmap(4, 4);
        using (var g = WfGraphics.FromImage(src))
            g.Clear(WfColor.Red);
        using var dst = new WfBitmap(8, 8);
        using var g2 = WfGraphics.FromImage(dst);
        g2.Clear(WfColor.White);
        g2.DrawImage(src, 1f, 1f, 2f, 2f);
        Assert.Equal(WfColor.Red.ToArgb(), dst.GetPixel(1, 1).ToArgb());
        g2.DrawImage(src, 4, 4, 2, 2);
        Assert.Equal(WfColor.Red.ToArgb(), dst.GetPixel(4, 4).ToArgb());
        g2.DrawImage(src, [new WfPoint(0, 6), new WfPoint(2, 6), new WfPoint(0, 8)]);
        g2.DrawImage(src, [new WfPointF(6, 0), new WfPointF(8, 0), new WfPointF(6, 2)],
            new WfRectangle(0, 0, 4, 4), WfGraphicsUnit.Pixel, null);
        bool aborted = false;
        g2.DrawImage(src, new WfRectangle(0, 0, 2, 2), 0, 0, 4, 4,
            WfGraphicsUnit.Pixel, null, _ => { aborted = true; return true; });
        Assert.True(aborted);
        g2.DrawImageUnscaledAndClipped(src, new WfRectangle(6, 6, 2, 2));
        Assert.Equal(WfColor.Red.ToArgb(), dst.GetPixel(6, 6).ToArgb());
    }

    [Fact]
    public void DrawImage_Effect_And_Attributes_Work()
    {
        using var src = new WfBitmap(4, 4);
        using (var g = WfGraphics.FromImage(src))
            g.Clear(WfColor.White);
        using var dst = new WfBitmap(4, 4);
        using var g2 = WfGraphics.FromImage(dst);
        using var gray = new WfDraw::System.Drawing.Imaging.Effects.GrayScaleEffect();
        g2.DrawImage(src, gray);
        WfColor after = dst.GetPixel(0, 0);
        Assert.Equal(after.R, after.G);
        var attrs = new WfImageAttributes();
        attrs.SetColorMatrix(new WfColorMatrix());
        g2.DrawImage(src, new WfRectangle(0, 0, 4, 4), 0f, 0f, 4f, 4f, WfGraphicsUnit.Pixel, attrs);
        Assert.Throws<ArgumentNullException>(() =>
            g2.EnumerateMetafile(null!, new WfPoint(0, 0), (t, f, s, d, cb) => true));
    }

    [Fact]
    public void TransformElements_And_MatrixElements_Roundtrip()
    {
        using var bitmap = new WfBitmap(2, 2);
        using var g = WfGraphics.FromImage(bitmap);
        var m = new WfMatrix(2f, 0f, 0f, 3f, 5f, 7f);
        g.Transform = m;
        var elements = g.TransformElements;
        Assert.Equal(2f, elements.M11);
        Assert.Equal(7f, elements.M32);
        var pts = new[] { new WfPoint(1, 1) };
        g.TransformPoints(WfCoordinateSpace.World, WfCoordinateSpace.Device, pts);
        g.TransformPoints(WfCoordinateSpace.World, WfCoordinateSpace.Device,
            (System.ReadOnlySpan<WfPoint>)pts);
        Assert.Equal(IntPtr.Zero, WfGraphics.GetHalftonePalette());
        Assert.NotNull(g.GetContextInfo());
    }

    [Fact]
    public void SystemColors_Brushes_Pens_Icons_Work()
    {
        Assert.Equal(WfColor.FromKnownColor(WfKnownColor.Window).ToArgb(),
            WfSystemColors.Window.ToArgb());
        Assert.False(WfSystemColors.UseAlternativeColorSet);
        using var b = WfSystemBrushes.Highlight;
        using var p = WfSystemPens.FromSystemColor(WfColor.Red);
        using var b2 = WfSystemBrushes.FromSystemColor(WfColor.Blue);
        Assert.NotNull(b);
        Assert.NotNull(p);
        Assert.NotNull(b2);
        using var app = WfSystemIcons.Application;
        Assert.Equal(32, app.Width);
        using var err = WfSystemIcons.Error;
        using var stock = WfSystemIcons.GetStockIcon(WfStockIconId.Error, 0);
        Assert.Equal(32, stock.Width);
        Assert.NotSame(app, err);
    }

    [Fact]
    public void BufferedGraphics_Render_Works()
    {
        using var target = new WfBitmap(10, 10);
        using (var g = WfGraphics.FromImage(target))
            g.Clear(WfColor.White);
        var manager = WfBufferedManager.Current;
        Assert.NotNull(manager);
        using var buffer = manager.Allocate(WfGraphics.FromImage(target), new WfRectangle(0, 0, 6, 6));
        buffer.Graphics.Clear(WfColor.Red);
        using var g2 = WfGraphics.FromImage(target);
        buffer.Render(g2);
        Assert.Equal(WfColor.Red.ToArgb(), target.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void LogFont_Roundtrips_FaceName()
    {
        using var font = new WfFont("Arial", 10f);
        font.ToLogFont(out var logFont);
        Assert.Equal("Arial", new string(logFont.lfFaceName).TrimEnd((char)0));
        var back = WfFont.FromLogFont(logFont);
        Assert.Equal("Arial", back.Name);
    }

    [Fact]
    public void PathData_Thumbnail_PrivateFonts_Work()
    {
        using var path = new WfGraphicsPath();
        path.AddLine(0f, 0f, 5f, 5f);
        WfPathData data = path.PathData;
        Assert.Equal(2, data.Points.Length);
        Assert.Equal(2, data.Types.Length);
        using var bitmap = new WfBitmap(8, 8);
        using WfImage thumb = bitmap.GetThumbnailImage(4, 4, null!, IntPtr.Zero);
        Assert.Equal(new WfSize(4, 4), thumb.Size);
        using var fonts = new WfPrivateFonts();
        fonts.Dispose();
        var conv = new WfFontConverter();
        var parsed = (WfFont)conv.ConvertFrom("Arial, 10pt")!;
        Assert.Equal("Arial", parsed.Name);
        var fmt = (string)conv.ConvertTo(parsed, typeof(string))!;
        Assert.Contains("Arial", fmt);
    }

    [Fact]
    public void ImageFormatConverter_Roundtrips()
    {
        var conv = new WfImageFormatConverter();
        var png = (WfImageFormat)conv.ConvertFrom("Png")!;
        Assert.Equal(WfImageFormat.Png.Guid, png.Guid);
        Assert.Equal("Png", conv.ConvertTo(png, typeof(string)));
        Assert.True(conv.GetStandardValuesSupported(null));
        Assert.True(conv.GetStandardValues(null)!.Count >= 10);
    }
}
