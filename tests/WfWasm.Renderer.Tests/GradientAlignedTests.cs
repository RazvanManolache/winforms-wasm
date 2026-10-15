using WfWasm.Renderer;
using WfWasm.Shim;

public class GradientAlignedTests
{
    [Fact]
    public void Recording_GradientAndAlignedText()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(100, 100);
        r.FillGradientVertical(0, 0, 100, 20, 0x000080FFu, 0x1084D0FFu);
        r.DrawTextAligned("Hi", 50, 10, "13px sans-serif", 0xFFFFFFFFu,
            TextHAlign.Center, TextVAlign.Middle, mnemonicIndex: 1);
        r.EndFrame();

        var gradient = Assert.IsType<FillGradientCommand>(
            r.Commands.OfType<FillGradientCommand>().Single());
        Assert.Equal(0x000080FFu, gradient.TopRgba);
        Assert.Equal(0x1084D0FFu, gradient.BottomRgba);

        var text = Assert.IsType<DrawTextCommand>(
            r.Commands.OfType<DrawTextCommand>().Single());
        Assert.Equal(TextHAlign.Center, text.HAlign);
        Assert.Equal(TextVAlign.Middle, text.VAlign);
        Assert.Equal(1, text.MnemonicIndex);
    }

    [Fact]
    public void Recording_LineAndEllipse()
    {
        var r = new RecordingRenderer();
        r.BeginFrame(100, 100);
        r.DrawLine(1, 2, 30, 40, 3, 0xFF0000FFu);
        r.DrawEllipse(10, 10, 40, 20, 2, 0x000000FFu, 0x00FF00FFu);
        r.DrawEllipse(0, 0, 10, 10, 1, 0x000000FFu);
        r.EndFrame();

        var line = Assert.IsType<DrawLineCommand>(
            r.Commands.OfType<DrawLineCommand>().Single());
        Assert.Equal((1f, 2f, 30f, 40f, 3f), (line.X1, line.Y1, line.X2, line.Y2, line.Width));

        var ellipses = r.Commands.OfType<DrawEllipseCommand>().ToList();
        Assert.Equal(2, ellipses.Count);
        Assert.Equal(0x00FF00FFu, ellipses[0].FillRgba);
        Assert.Null(ellipses[1].FillRgba);
    }

    [Fact]
    public void Svg_LineAndEllipse()
    {
        var svg = new SvgRenderer();
        svg.BeginFrame(100, 100);
        svg.DrawLine(1, 2, 30, 40, 3, 0xFF0000FFu);
        svg.DrawEllipse(10, 10, 40, 20, 2, 0x000000FFu, 0x00FF00FFu);
        svg.EndFrame();

        string xml = svg.GetSvg();
        Assert.Contains("<line", xml);
        Assert.Contains("stroke-linecap=\"round\"", xml);
        Assert.Contains("<ellipse", xml);
    }

    [Fact]
    public void Svg_GradientAndAlignedText()
    {
        var svg = new SvgRenderer();
        svg.BeginFrame(100, 100);
        svg.FillGradientVertical(0, 0, 100, 20, 0x000080FFu, 0x1084D0FFu);
        svg.DrawTextAligned("Hi", 50, 10, "13px sans-serif", 0xFFFFFFFFu,
            TextHAlign.Center, TextVAlign.Middle);
        svg.EndFrame();

        string xml = svg.GetSvg();
        Assert.Contains("linearGradient", xml);
        Assert.Contains("text-anchor=\"middle\"", xml);
        Assert.Contains("dominant-baseline=\"central\"", xml);
    }
}
