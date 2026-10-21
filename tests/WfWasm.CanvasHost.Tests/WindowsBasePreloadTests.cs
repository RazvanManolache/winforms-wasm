using Xunit;

namespace WfWasm.CanvasHost.Tests;

public sealed class WindowsBasePreloadTests
{
    [Fact]
    public void WindowsBase_Binds_Full_Copy_With_Freezable()
    {
        // Guards the PaintSceneTests hang (hangrun2/3/4): the shared
        // WindowsBase facade lacks System.Windows.Freezable; PDN Tool-init
        // TypeLoads it and wedges the class-init lock. The module
        // initializer must bind the full app-local copy first. Telerik
        // (Telerik.WinControls.UI -> WindowsBase) loads it early.
        Type? freezable = Type.GetType("System.Windows.Freezable, WindowsBase");
        Assert.NotNull(freezable);
        string location = freezable.Assembly.Location;
        Assert.DoesNotContain("Microsoft.NETCore.App", location);
    }
}
