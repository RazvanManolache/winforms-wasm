using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using Xunit;

namespace WfWasm.CanvasHost.Tests;

[Collection("Paint")]
public class PaintSceneTests
{
    private readonly WinFormsAppHost _host;

    public PaintSceneTests(PaintHostFixture fx)
    {
        _host = fx.Host;
    }

    [Fact]
    public void PaintScene_CreatesFormAndAppWorkspace()
    {
        // The fixture boots one shared PDN app (single-instance parity)
        // and waits for readiness; assert the settled structure.
        System.Windows.Forms.Form? form = _host.GetOrCreateForm(AppScene.Paint);
        Assert.NotNull(form);
        // Real MainForm now loads once PDN bootstrap succeeds; the wrapper
        // fallback (hardcoded Text) only appears when MainForm fails.
        Assert.True(form.Text == "Paint.NET v5.1.12 (WebAssembly)" || form.GetType().FullName == "PaintDotNet.Dialogs.MainForm", "Unexpected Paint form " + form.GetType().FullName + " Text=" + form.Text);
        Assert.True(HasAppWorkspace(form), "Paint form has no AppWorkspace");
    }

    [Fact]
    public void PaintBoot_Pins_Resource_Lookup_To_Invariant()
    {
        // No satellites ship in the bundle, so fallback is always
        // English: the boot pins DefaultThreadCurrentUICulture to
        // invariant to skip the per-ResourceManager satellite probes
        // (first-chance FileNotFound noise). Formatting culture stays
        // out of this contract.
        Assert.NotNull(_host.GetOrCreateForm(AppScene.Paint));
        Assert.Equal(
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture);
    }

    private static bool HasAppWorkspace(System.Windows.Forms.Control c)
    {
        if (c.GetType().FullName == "PaintDotNet.Controls.AppWorkspace") return true;
        foreach (System.Windows.Forms.Control ch in c.Controls) if (HasAppWorkspace(ch)) return true;
        return false;
    }
}
