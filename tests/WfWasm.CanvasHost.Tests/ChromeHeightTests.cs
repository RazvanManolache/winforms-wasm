using WfWasm.CanvasHost;
using FormsForm = System.Windows.Forms.Form;

namespace WfWasm.CanvasHost.Tests;

// Host caption is conditional now: the 30px painted titlebar used to
// cover chromeless forms (RadForm paints its own caption; the +30
// client offset also shifted every hit-test). Regression: Telerik
// PhotoAlbum black strip + offset content (photo G1).
public sealed class ChromeHeightTests
{
    [Fact]
    public void Borderless_Form_Gets_No_Host_Chrome()
    {
        using var form = new FormsForm { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None };
        Assert.Equal(0, WinFormsAppHost.ChromeHeight(form));
    }

    [Fact]
    public void Sizable_Form_Keeps_Host_Chrome()
    {
        using var form = new FormsForm { FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable };
        Assert.Equal(30, WinFormsAppHost.ChromeHeight(form));
    }

    [Fact]
    public void Fixed_Form_Keeps_Host_Chrome()
    {
        using var form = new FormsForm { FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle };
        Assert.Equal(30, WinFormsAppHost.ChromeHeight(form));
    }

    // Telerik custom-chrome family self-chromes via NCCALCSIZE while
    // keeping WS_CAPTION (BugTracker RadRibbonForm1 never sets
    // FormBorderStyle.None): no host caption, client fills the window.
    private sealed class FakeRadFormHost : FormsForm;

    private sealed class FakeShapedFormHost : FormsForm;

    [Fact]
    public void RadForm_Chain_Gets_No_Host_Chrome()
    {
        using var form = new FakeRadFormHost { FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable };
        Assert.Equal(0, WinFormsAppHost.ChromeHeight(form));
    }

    [Fact]
    public void ShapedForm_Chain_Gets_No_Host_Chrome()
    {
        using var form = new FakeShapedFormHost { FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable };
        Assert.Equal(0, WinFormsAppHost.ChromeHeight(form));
    }
}
