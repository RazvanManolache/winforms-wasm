using WfWasm.Renderer;
using WfWasm.Shim;

public class OverlayPainterTests
{
    private static DialogPageModel SmokePage()
    {
        var page = new DialogPageModel { Heading = "Form1", Text = "Hello .NET" };
        page.Buttons.Add(new DialogButtonModel("ok", "OK") { IsDefault = true });
        return page;
    }

    [Fact]
    public void Paint_SmokePage_ContainsTitleHeadingBodyAndButton()
    {
        var renderer = new RecordingRenderer();
        DialogOverlayPainter.Paint(renderer, SmokePage(), "Form1");

        var texts = renderer.Commands.OfType<DrawTextCommand>().Select(c => c.Text).ToList();
        Assert.Contains("Form1", texts);
        Assert.Contains("Hello .NET", texts);
        Assert.Contains("OK", texts);
        Assert.IsType<FrameCommand>(renderer.Commands[0]);
        Assert.IsType<EndFrameCommand>(renderer.Commands[^1]);
        Assert.Contains(renderer.Commands.OfType<FillRectCommand>(),
            c => c.Width == 480 && c.Height == 260);
    }

    [Fact]
    public void Paint_SvgOutput_ContainsOverlayContent()
    {
        var renderer = new SvgRenderer();
        var page = SmokePage();
        page.ProgressState = DialogProgressState.Normal;
        page.ProgressValue = 50;
        page.VerificationText = "I know what I'm doing";
        DialogOverlayPainter.Paint(renderer, page, "Form1");

        string svg = renderer.GetSvg();
        Assert.Contains("Hello .NET", svg);
        Assert.Contains("[ ] I know what I'm doing", svg);
        Assert.Contains("fill=\"#0078d7ff\"", svg);
        Assert.EndsWith("</svg>", svg);
    }

    [Fact]
    public void Paint_NullArguments_Throw()
    {
        var renderer = new RecordingRenderer();
        Assert.Throws<ArgumentNullException>(() => DialogOverlayPainter.Paint(null!, SmokePage(), "T"));
        Assert.Throws<ArgumentNullException>(() => DialogOverlayPainter.Paint(renderer, null!, "T"));
        Assert.Throws<ArgumentNullException>(() => DialogOverlayPainter.Paint(renderer, SmokePage(), null!));
    }
}
