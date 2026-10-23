using WfWasm.CanvasHost;
using FormsButton = System.Windows.Forms.Button;
using FormsPaintEventArgs = System.Windows.Forms.PaintEventArgs;

// Routing rule for the flat Button fast-path (SfButton style images,
// 2026-10-23): Button itself overrides OnPaint upstream, so managed
// paint must win only when the most-derived paint method is declared
// strictly below Button. Exact Button and non-overriding subclasses
// keep the flat renderer.
public sealed class ButtonPaintRoutingTests
{
    private class SelfPaintButton : FormsButton
    {
        protected override void OnPaint(FormsPaintEventArgs e) { }
    }

    private sealed class PlainButton : FormsButton
    {
    }

    private sealed class GrandchildButton : SelfPaintButton
    {
    }

    [Fact]
    public void Exact_Button_Is_Flat()
    {
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(FormsButton)));
    }

    [Fact]
    public void Non_Overriding_Subclass_Is_Flat()
    {
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(PlainButton)));
    }

    [Fact]
    public void OnPaint_Override_Routes_Managed()
    {
        Assert.True(WinFormsAppHost.ButtonPaintsItself(typeof(SelfPaintButton)));
    }

    [Fact]
    public void Inherited_Override_Routes_Managed()
    {
        Assert.True(WinFormsAppHost.ButtonPaintsItself(typeof(GrandchildButton)));
    }

    [Fact]
    public void Non_Button_Type_Is_Flat()
    {
        Assert.False(WinFormsAppHost.ButtonPaintsItself(typeof(object)));
    }
}
