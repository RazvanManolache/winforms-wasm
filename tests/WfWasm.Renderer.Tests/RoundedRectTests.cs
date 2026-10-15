using WfWasm.Renderer;

public class RoundedRectTests
{
    [Fact]
    public void RecordingRenderer_CapturesRoundedRect()
    {
        var renderer = new RecordingRenderer();
        renderer.BeginFrame(200, 200);
        renderer.FillRoundedRectangle(10, 20, 100, 80, 15, 0xFFD2DCFF);
        renderer.EndFrame();

        Assert.Contains(renderer.Commands.OfType<FillRoundedRectCommand>(),
            c => c.X == 10 && c.Y == 20 && c.Radius == 15 && c.Rgba == 0xFFD2DCFF);
    }

    [Fact]
    public void SvgRenderer_EmitsRxAttribute()
    {
        var renderer = new SvgRenderer();
        renderer.BeginFrame(200, 200);
        renderer.FillRoundedRectangle(10, 20, 100, 80, 15, 0xFFD2DCFF);
        renderer.EndFrame();

        Assert.Contains("<rect x=\"10\" y=\"20\" width=\"100\" height=\"80\" rx=\"15\" fill=\"#ffd2dcff\"/>",
            renderer.GetSvg());
    }
}
