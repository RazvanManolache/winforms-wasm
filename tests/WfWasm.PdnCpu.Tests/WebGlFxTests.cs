using System.Text.Json;
using WfWasm.PdnCpu;
using Xunit;

public sealed class WebGlFxTests
{
    [Fact]
    public void MapEffect_GaussianBlur_Maps_Radius()
    {
        var props = new Dictionary<string, object?> { ["Radius"] = 6f };
        var mapped = WebGlFx.MapEffect("PaintDotNet.Direct2D1.Effects.GaussianBlurEffect", props);
        Assert.NotNull(mapped);
        Assert.Equal("kawase", mapped.Value.Filter);
        using var doc = JsonDocument.Parse(mapped.Value.ParamsJson);
        Assert.Equal(6, doc.RootElement.GetProperty("radius").GetDouble());
    }

    [Fact]
    public void MapEffect_ColorMatrix_Requires_20_Floats()
    {
        var shortProps = new Dictionary<string, object?> { ["Matrix"] = new float[19] };
        Assert.Null(WebGlFx.MapEffect("ColorMatrixEffect", shortProps));

        float[] m = new float[20];
        m[0] = m[6] = m[12] = m[18] = 1f;
        var props = new Dictionary<string, object?> { ["Matrix"] = m };
        var mapped = WebGlFx.MapEffect("ColorMatrixEffect", props);
        Assert.NotNull(mapped);
        Assert.Equal("colormatrix", mapped.Value.Filter);
        using var doc = JsonDocument.Parse(mapped.Value.ParamsJson);
        Assert.Equal(20, doc.RootElement.GetProperty("matrix").GetArrayLength());
    }

    [Fact]
    public void MapEffect_Unknown_Effect_Returns_Null()
    {
        var props = new Dictionary<string, object?>();
        Assert.Null(WebGlFx.MapEffect("MorphologyErodeEffect", props));
        Assert.Null(WebGlFx.MapEffect("", props));
    }

    [Fact]
    public void MapEffect_Brightness_Defaults_To_One()
    {
        var mapped = WebGlFx.MapEffect("BrightnessEffect", new Dictionary<string, object?>());
        Assert.NotNull(mapped);
        Assert.Equal("brightness", mapped.Value.Filter);
        using var doc = JsonDocument.Parse(mapped.Value.ParamsJson);
        Assert.Equal(1, doc.RootElement.GetProperty("value").GetDouble());
    }

    [Fact]
    public void Swap_RoundTrips_Pixels()
    {
        uint[] bgra = [0xFF112233, 0x80112233, 0x00000000, 0xFFFFFFFF];
        byte[] rgba = new byte[bgra.Length * 4];
        WebGlFx.BgraToRgba(bgra, rgba);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0xFF }, rgba[0..4]);
        uint[] back = new uint[bgra.Length];
        WebGlFx.RgbaToBgra(rgba, back);
        Assert.Equal(bgra, back);
    }

    [Fact]
    public void TryRender_Without_Browser_Returns_Null()
    {
        // No JS runtime in the test host: total function, null everywhere.
        Assert.False(WebGlFx.IsAvailable);
        var props = new Dictionary<string, object?> { ["Radius"] = 2f };
        Assert.Null(WebGlFx.TryRender("GaussianBlurEffect", props, new uint[4], 2, 2));
        Assert.Null(WebGlFx.TryRender("GaussianBlurEffect", props, new uint[3], 2, 2));
        Assert.Null(WebGlFx.TryRender("Nope", props, new uint[4], 2, 2));
    }
}
