using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using Xunit;

namespace WfWasm.CanvasHost.Tests;

public class PaintSceneTests
{
    [Fact]
    public void PaintScene_CreatesFormAndAppWorkspace()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Paint);
        var form = host.GetOrCreateForm(AppScene.Paint);
        Assert.NotNull(form);
        Assert.Equal("Paint.NET v5.1.12 (WebAssembly)", form.Text);
    }

    [Fact]
    public void PaintBoot_Pins_Resource_Lookup_To_Invariant()
    {
        // No satellites ship in the bundle, so fallback is always
        // English: the boot pins DefaultThreadCurrentUICulture to
        // invariant to skip the per-ResourceManager satellite probes
        // (first-chance FileNotFound noise). Formatting culture stays
        // out of this contract.
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Paint);
        Assert.NotNull(host.GetOrCreateForm(AppScene.Paint));
        Assert.Equal(
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture);
    }
}



