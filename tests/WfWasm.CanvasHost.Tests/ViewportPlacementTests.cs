using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class ViewportPlacementTests
{
    [Fact]
    public void Resize_ReCenters_AutoPlaced_Form()
    {
        using var host = new WinFormsAppHost(); // defaults 1200x950
        host.SelectScene(AppScene.Smoke); // 500x350 -> (350, 300)
        var form = host.GetOrCreateForm(AppScene.Smoke)!;
        Assert.Equal(350, form.Location.X);
        Assert.Equal(300, form.Location.Y);

        host.ViewWidth = 1078;
        host.ViewHeight = 831;
        host.OnViewportChanged();

        Assert.Equal(289, form.Location.X);
        Assert.Equal(240, form.Location.Y);
    }

    [Fact]
    public void Resize_Leaves_Dragged_Form_Alone()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var form = host.GetOrCreateForm(AppScene.Smoke)!;

        host.OnMouseDown(360, 305); // inside the title bar
        host.OnMouseMove(370, 310);
        host.OnMouseUp(370, 310);
        int placedX = form.Location.X;
        int placedY = form.Location.Y;
        Assert.True(placedX != 350 || placedY != 300, "drag should move the form");

        host.ViewWidth = 1078;
        host.ViewHeight = 831;
        host.OnViewportChanged();

        Assert.Equal(placedX, form.Location.X);
        Assert.Equal(placedY, form.Location.Y);
    }
}
