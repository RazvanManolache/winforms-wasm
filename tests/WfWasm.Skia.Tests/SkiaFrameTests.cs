using WfWasm.Renderer;
using WfWasm.Skia;

namespace WfWasm.Skia.Tests;

// Frame-lifecycle and pixel-truth net for the desktop snapshot backend.
// Colors are 0xRRGGBBAA; GetPixelRgba returns 0xRRGGBBAA too.
public sealed class SkiaFrameTests
{
    private static SkiaCanvasRenderer Open(int w = 8, int h = 8)
    {
        var r = new SkiaCanvasRenderer();
        r.BeginFrame(w, h);
        return r;
    }

    [Fact]
    public void BeginFrame_Sets_Size_And_ToString()
    {
        using var r = Open(10, 20);
        Assert.Equal(10, r.Width);
        Assert.Equal(20, r.Height);
        Assert.Equal("SkiaCanvasRenderer(10x20)", r.ToString());
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(8, 0)]
    [InlineData(-1, 8)]
    public void BeginFrame_NonPositive_Throws(int w, int h)
    {
        using var r = new SkiaCanvasRenderer();
        Assert.Throws<ArgumentOutOfRangeException>(() => r.BeginFrame(w, h));
    }

    [Fact]
    public void Paint_Before_BeginFrame_Throws()
    {
        using var r = new SkiaCanvasRenderer();
        Assert.Throws<InvalidOperationException>(() => r.FillRectangle(0, 0, 4, 4, 0xFF0000FFu));
        Assert.Throws<InvalidOperationException>(() => r.EndFrame());
        Assert.Throws<InvalidOperationException>(() => r.GetPixelRgba(0, 0));
        Assert.Throws<InvalidOperationException>(() => r.ToPng());
    }

    [Fact]
    public void FillRectangle_Paints_Exact_Pixel()
    {
        using var r = Open();
        r.FillRectangle(2, 2, 3, 3, 0xFF0000FFu);
        r.EndFrame();
        Assert.Equal(0xFF0000FFu, r.GetPixelRgba(3, 3));
        Assert.Equal(0x00000000u, r.GetPixelRgba(0, 0));
        Assert.Equal(0x00000000u, r.GetPixelRgba(7, 7));
    }

    [Fact]
    public void FillRoundedRectangle_Paints_Interior()
    {
        using var r = Open(16, 16);
        r.FillRoundedRectangle(2, 2, 12, 12, 2, 0x00FF00FFu);
        r.EndFrame();
        Assert.Equal(0x00FF00FFu, r.GetPixelRgba(8, 8));
    }

    [Fact]
    public void DrawLine_Paints_Endpoints()
    {
        using var r = Open(16, 16);
        r.DrawLine(1, 1, 14, 1, 2, 0xFFFFFFFFu);
        r.EndFrame();
        uint px = r.GetPixelRgba(7, 1);
        Assert.True(px != 0u, $"expected painted pixel, got 0x{px:X8}");
    }

    [Fact]
    public void DrawEllipse_Fill_Paints_Center()
    {
        using var r = Open(16, 16);
        r.DrawEllipse(2, 2, 12, 12, 1, 0xFF0000FFu, 0x00FF00FFu);
        r.EndFrame();
        Assert.Equal(0x00FF00FFu, r.GetPixelRgba(8, 8));
    }

    [Fact]
    public void FillGradientVertical_Top_Bottom_Differ()
    {
        using var r = Open(4, 16);
        r.FillGradientVertical(0, 0, 4, 16, 0xFF0000FFu, 0x0000FFFFu);
        r.EndFrame();
        uint top = r.GetPixelRgba(2, 1);
        uint bottom = r.GetPixelRgba(2, 14);
        Assert.NotEqual(top, bottom);
        Assert.True((top >> 24) > (bottom >> 24));
    }

    [Fact]
    public void DrawImage_Blit_Paints_Pixels()
    {
        using var r = Open();
        var rgba = new byte[4 * 4 * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = 255;
            rgba[i + 3] = 255;
        }
        r.DrawImage(rgba, 4, 4, 0, 0, 4, 4);
        r.EndFrame();
        Assert.Equal(0xFF0000FFu, r.GetPixelRgba(1, 1));
    }

    [Fact]
    public void DrawImage_Short_Buffer_Throws()
    {
        using var r = Open();
        Assert.Throws<ArgumentException>(() => r.DrawImage(new byte[10], 4, 4, 0, 0, 4, 4));
    }

    [Fact]
    public void DrawImage_NonPositive_Dest_Noop()
    {
        using var r = Open();
        var rgba = new byte[16];
        r.DrawImage(rgba, 2, 2, 0, 0, 0, 2);
        r.DrawImage(rgba, 2, 2, 0, 0, 2, -1);
        r.EndFrame();
        Assert.Equal(0x00000000u, r.GetPixelRgba(0, 0));
    }

    [Fact]
    public void ToPng_Returns_Png_Signature()
    {
        using var r = Open();
        r.FillRectangle(0, 0, 8, 8, 0xFFFFFFFFu);
        r.EndFrame();
        byte[] png = r.ToPng();
        Assert.True(png.Length > 8);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
    }

    [Fact]
    public void Second_BeginFrame_Resets_Frame()
    {
        using var r = Open();
        r.FillRectangle(0, 0, 8, 8, 0xFF0000FFu);
        r.BeginFrame(8, 8);
        r.EndFrame();
        Assert.Equal(0x00000000u, r.GetPixelRgba(0, 0));
    }

    [Fact]
    public void Disposed_Throws_On_Use()
    {
        var r = Open();
        r.Dispose();
        Assert.Throws<ObjectDisposedException>(() => r.BeginFrame(4, 4));
        Assert.Throws<ObjectDisposedException>(() => r.EndFrame());
    }
}
