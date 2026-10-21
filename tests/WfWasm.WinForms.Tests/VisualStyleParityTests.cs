using FormsVisualStyles = System.Windows.Forms.VisualStyles;

namespace WfWasm.WinForms.Tests;

// The browser has no uxtheme engine: OpenThemeData answers null, so the
// only honest headless report is "visual styles not enabled", matching a
// desktop with visual styles disabled. Reporting IsAppThemed()=true while
// every theme handle fails puts vendors on the try-themed path and costs
// one first-chance InvalidOperationException per paint (observed:
// PdnTrackBar.OnPaint -> PaintThemed -> VisualStyleRenderer..ctor on every
// Paint.NET effect-dialog repaint, falling back to PaintClassic anyway).
public sealed class VisualStyleParityTests
{
    [Fact]
    public void IsEnabledByUser_Is_False_Headless()
    {
        Assert.False(FormsVisualStyles.VisualStyleInformation.IsEnabledByUser);
    }

    [Fact]
    public void Renderer_IsSupported_Is_False_Headless()
    {
        Assert.False(FormsVisualStyles.VisualStyleRenderer.IsSupported);
    }

    [Fact]
    public void RenderWithVisualStyles_Is_False_Headless()
    {
        Assert.False(System.Windows.Forms.Application.RenderWithVisualStyles);
    }
}
