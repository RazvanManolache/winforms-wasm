using WfWasm.Renderer;
using WfWasm.Shim;
using WfWasm.Skia;

/// <summary>Paints whole app scenes through the owned Skia backend (no browser).</summary>
public class CanvasPaintTests
{
    private static byte[] Rgb(uint rgba) =>
        new[] { (byte)(rgba >> 24), (byte)((rgba >> 16) & 0xFF), (byte)((rgba >> 8) & 0xFF) };

    private static int DarkPixels(SkiaCanvasRenderer renderer, int x0, int x1, int y0, int y1)
    {
        int dark = 0;
        for (int x = x0; x < x1; x += 2)
            for (int y = y0; y < y1; y += 2)
            {
                byte[] c = Rgb(renderer.GetPixelRgba(x, y));
                if (c[0] + c[1] + c[2] < 300)
                    dark++;
            }

        return dark;
    }

    [Fact]
    public void SmokeScene_PaintsFormWithLabel()
    {
        var app = new CanvasApp();
        using var renderer = new SkiaCanvasRenderer();
        renderer.BeginFrame(CanvasApp.ViewWidth, CanvasApp.ViewHeight);
        app.Paint(renderer);
        renderer.EndFrame();

        // Themed caption is white; client stays white; label paints top-anchored.
        Assert.Equal(0xFFFFFFFFu, renderer.GetPixelRgba(200, 160));
        Assert.Equal(0xFFFFFFFFu, renderer.GetPixelRgba(500, 400));
        Assert.True(DarkPixels(renderer, 285, 483, 348, 392) > 20, "Label text missing.");
    }

    [Fact]
    public void CalendarScene_PaintsGridWithStyling()
    {
        var app = new CanvasApp();
        app.KeyChar('2');
        using var renderer = new SkiaCanvasRenderer();
        renderer.BeginFrame(CanvasApp.ViewWidth, CanvasApp.ViewHeight);
        app.Paint(renderer);
        renderer.EndFrame();

        Assert.Equal(0xFFD2DCFFu, renderer.GetPixelRgba(179, 262));
        Assert.Equal(0xD3D3D3FFu, renderer.GetPixelRgba(279, 262));
        Assert.True(DarkPixels(renderer, 135, 260, 118, 138) > 5, "Weekday header missing.");
    }
}
