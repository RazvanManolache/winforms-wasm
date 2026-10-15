using WfWasm.Renderer;

public class RendererTests
{
    private static void PaintSample(ICanvasRenderer renderer)
    {
        renderer.BeginFrame(300, 150);
        renderer.FillRectangle(10, 20, 100, 50, 0xFF0000FF);
        renderer.DrawText("Hi <there> & \"bye\"", 12, 40, "12px sans-serif", 0x000000FF);
        renderer.EndFrame();
    }

    [Fact]
    public void RecordingRenderer_CapturesCommandSequence()
    {
        var renderer = new RecordingRenderer();
        PaintSample(renderer);

        Assert.Equal(new DrawCommand[]
        {
            new FrameCommand(300, 150),
            new FillRectCommand(10, 20, 100, 50, 0xFF0000FF),
            new DrawTextCommand("Hi <there> & \"bye\"", 12, 40, "12px sans-serif", 0x000000FF),
            EndFrameCommand.Instance,
        }, renderer.Commands);
    }

    [Fact]
    public void SvgRenderer_EmitsRectFillAndEscapedText()
    {
        var renderer = new SvgRenderer();
        PaintSample(renderer);
        string svg = renderer.GetSvg();

        Assert.Contains("<svg", svg);
        Assert.Contains("<rect", svg);
        Assert.Contains("fill=\"#ff0000ff\"", svg);
        Assert.Contains("Hi &lt;there&gt; &amp; &quot;bye&quot;", svg);
        Assert.DoesNotContain("<there>", svg);
        Assert.EndsWith("</svg>", svg);
    }

    [Fact]
    public void SvgRenderer_EmptyFrame_IsValidShell()
    {
        var renderer = new SvgRenderer();
        renderer.BeginFrame(64, 64);
        renderer.EndFrame();
        Assert.Equal("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\"></svg>",
            renderer.GetSvg());
    }

    [Fact]
    public void SvgRenderer_DrawBeforeBegin_Throws()
    {
        var renderer = new SvgRenderer();
        Assert.Throws<InvalidOperationException>(() => renderer.FillRectangle(0, 0, 1, 1, 0));
        Assert.Throws<InvalidOperationException>(() => renderer.EndFrame());
    }
}
