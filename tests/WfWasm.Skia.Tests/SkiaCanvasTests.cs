using WfWasm.Renderer;
using WfWasm.Shim;
using WfWasm.Skia;

public class SkiaCanvasTests
{
    private static SkiaCanvasRenderer SmokeScene()
    {
        var renderer = new SkiaCanvasRenderer();
        renderer.BeginFrame(800, 450);
        var form = new Form("form") { Width = 800, Height = 450, Text = "Form1" };
        form.AddChild(new Label("label1")
        {
            X = 180, Y = 169, Width = 300, Height = 50,
            Text = "Hello .NET", FontCss = "36px sans-serif",
        });
        form.AddChild(new Button("ok") { X = 690, Y = 380, Width = 90, Height = 30, Text = "OK", IsDefault = true });
        form.Render(renderer);
        renderer.EndFrame();
        return renderer;
    }

    [Fact]
    public void SmokeScene_SolidAreasHaveExactColors()
    {
        using var renderer = SmokeScene();

        // Themed caption is flat white; the default button carries a blue border.
        Assert.Equal(0xFFFFFFFFu, renderer.GetPixelRgba(400, 10));
        Assert.Equal(0xFFFFFFFFu, renderer.GetPixelRgba(400, 200));
        Assert.Equal(0xE1E1E1FFu, renderer.GetPixelRgba(700, 435));
        Assert.Equal(0x0078D7FFu, renderer.GetPixelRgba(700, 411));
        Assert.Equal(0xFFFFFFFFu, renderer.GetPixelRgba(780, 10));
    }

    [Fact]
    public void SmokeScene_LabelTextRendersDarkPixels()
    {
        using var renderer = SmokeScene();

        int dark = 0;
        for (int x = 185; x < 385; x += 2)
            for (int y = 195; y < 245; y += 2)
            {
                uint pixel = renderer.GetPixelRgba(x, y);
                byte r = (byte)(pixel >> 24);
                byte g = (byte)((pixel >> 16) & 0xFF);
                byte b = (byte)((pixel >> 8) & 0xFF);
                if (r + g + b < 300)
                    dark++;
            }

        Assert.True(dark > 20, $"Expected rendered text pixels, found {dark}.");
    }

    [Fact]
    public void RoundedRect_RendersFillColor()
    {
        using var renderer = new SkiaCanvasRenderer();
        renderer.BeginFrame(200, 200);
        renderer.FillRoundedRectangle(10, 20, 100, 80, 15, 0xFFD2DCFFu);
        renderer.EndFrame();

        Assert.Equal(0xFFD2DCFFu, renderer.GetPixelRgba(60, 60));
        Assert.Equal(0x00000000u, renderer.GetPixelRgba(11, 21));
    }

    [Fact]
    public void ToPng_ProducesValidPngBytes()
    {
        using var renderer = SmokeScene();
        byte[] png = renderer.ToPng();

        Assert.True(png.Length > 1000, $"Suspiciously small PNG: {png.Length} bytes.");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }

    private static int CountDarkPixels(SkiaCanvasRenderer renderer, int x0, int y0, int x1, int y1)
    {
        int dark = 0;
        for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
            {
                uint pixel = renderer.GetPixelRgba(x, y);
                byte r = (byte)(pixel >> 24);
                byte g = (byte)((pixel >> 16) & 0xFF);
                byte b = (byte)((pixel >> 8) & 0xFF);
                if (r + g + b < 300)
                    dark++;
            }
        return dark;
    }

    [Fact]
    public void BoldText_RendersHeavierStrokesThanRegular()
    {
        // Real control fonts arrive as e.g. "bold 13px 'Segoe UI', sans-serif".
        // A "bold " prefix must produce visibly heavier glyphs than regular.
        using var regular = new SkiaCanvasRenderer();
        regular.BeginFrame(400, 120);
        regular.FillRectangle(0, 0, 400, 120, 0xFFFFFFFFu);
        regular.DrawTextAligned("Bold?", 20, 80, "36px 'Segoe UI', sans-serif",
            0x000000FF, TextHAlign.Left, TextVAlign.Baseline);
        regular.EndFrame();

        using var bold = new SkiaCanvasRenderer();
        bold.BeginFrame(400, 120);
        bold.FillRectangle(0, 0, 400, 120, 0xFFFFFFFFu);
        bold.DrawTextAligned("Bold?", 20, 80, "bold 36px 'Segoe UI', sans-serif",
            0x000000FF, TextHAlign.Left, TextVAlign.Baseline);
        bold.EndFrame();

        int regularDark = CountDarkPixels(regular, 0, 0, 400, 120);
        int boldDark = CountDarkPixels(bold, 0, 0, 400, 120);
        Assert.True(regularDark > 100, $"Regular text did not render, found {regularDark}.");
        Assert.True(boldDark > regularDark * 1.2,
            $"Bold ({boldDark}) should be heavier than regular ({regularDark}).");
    }

    [Fact]
    public void DrawImage_RendersDecodedPixels()
    {
        // 2x2 opaque red drawn 1:1.
        byte[] rgba = [255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255];
        using var renderer = new SkiaCanvasRenderer();
        renderer.BeginFrame(100, 100);
        renderer.FillRectangle(0, 0, 100, 100, 0xFFFFFFFFu);
        renderer.DrawImage(rgba, 2, 2, 10, 20, 2, 2);
        renderer.EndFrame();

        Assert.Equal(0xFF0000FFu, renderer.GetPixelRgba(10, 20));
        Assert.Equal(0xFF0000FFu, renderer.GetPixelRgba(11, 21));
        Assert.Equal(0xFFFFFFFFu, renderer.GetPixelRgba(12, 22));
    }

    [Fact]
    public void DrawImage_ShortBufferThrows()
    {
        using var renderer = new SkiaCanvasRenderer();
        renderer.BeginFrame(100, 100);
        Assert.Throws<ArgumentException>(() => renderer.DrawImage([1, 2, 3], 2, 2, 0, 0, 2, 2));
    }

    [Fact]
    public void DrawBeforeBegin_Throws()
    {
        using var renderer = new SkiaCanvasRenderer();
        Assert.Throws<InvalidOperationException>(() => renderer.FillRectangle(0, 0, 1, 1, 0));
        Assert.Throws<InvalidOperationException>(() => renderer.GetPixelRgba(0, 0));
        Assert.Throws<InvalidOperationException>(() => renderer.ToPng());
    }
}
