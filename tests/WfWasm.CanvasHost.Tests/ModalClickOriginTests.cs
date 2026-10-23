using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsControl = System.Windows.Forms.Control;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;

// Modal/dock windows sit at different canvas origins than the active
// form. RaiseMouseEvent used to anchor every delivered Location to
// _activeForm, shifting modal targets by the inter-form delta (a
// ColorGrid cell click indexed outside the grid and died as a swallowed
// IOORE with zero pixels changed). These pins lock the owner anchor.
public sealed class ModalClickOriginTests
{
    private sealed class ClickRecControl : FormsControl
    {
        public int DownX = int.MinValue;
        public int DownY = int.MinValue;
        public int ClickX = int.MinValue;
        public int ClickY = int.MinValue;
        protected override void OnMouseDown(FormsMouseEventArgs e)
        {
            DownX = e.X;
            DownY = e.Y;
        }
        protected override void OnMouseClick(FormsMouseEventArgs e)
        {
            ClickX = e.X;
            ClickY = e.Y;
        }
    }

    private static void RaiseOn(WinFormsAppHost host, string field, FormsControl target, int x, int y)
    {
        var hostType = typeof(WinFormsAppHost);
        var method = (MethodInfo)hostType.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        hostType.GetMethod("RaiseMouseEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, new object[] { method, target, System.Windows.Forms.MouseButtons.Left, 1, x, y, 0 });
    }

    [Fact]
    public void Modal_Target_Gets_Owner_Relative_Coords()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var active = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(active);
        // A modal/dock window far from the active form's origin.
        using var dlg = new System.Windows.Forms.Form
        {
            Location = new System.Drawing.Point(active.Location.X + 400, active.Location.Y + 200),
        };
        var rec = new ClickRecControl { Left = 10, Top = 20, Width = 100, Height = 60 };
        dlg.Controls.Add(rec);
        int chrome = WinFormsAppHost.ChromeHeight(dlg);
        int cx = dlg.Location.X + 10 + 7;
        int cy = dlg.Location.Y + chrome + 20 + 9;
        RaiseOn(host, "s_onMouseDownMethod", rec, cx, cy);
        Assert.Equal(7, rec.DownX);
        Assert.Equal(9, rec.DownY);
        RaiseOn(host, "s_onMouseClickMethod", rec, cx, cy);
        Assert.Equal(7, rec.ClickX);
        Assert.Equal(9, rec.ClickY);
    }

    [Fact]
    public void ActiveForm_Target_Keeps_Legacy_Coords()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var active = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(active);
        var rec = new ClickRecControl { Left = 30, Top = 40, Width = 100, Height = 60 };
        active.Controls.Add(rec);
        try
        {
            int chrome = WinFormsAppHost.ChromeHeight(active);
            int cx = active.Location.X + 30 + 5;
            int cy = active.Location.Y + chrome + 40 + 11;
            RaiseOn(host, "s_onMouseDownMethod", rec, cx, cy);
            Assert.Equal(5, rec.DownX);
            Assert.Equal(11, rec.DownY);
        }
        finally
        {
            active.Controls.Remove(rec);
        }
    }
}
