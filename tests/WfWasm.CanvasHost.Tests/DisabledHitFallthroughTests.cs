using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsForm = System.Windows.Forms.Form;

public sealed class DisabledHitFallthroughTests
{
    [Fact]
    public void Click_Falls_Through_Disabled_Control_To_Parent()
    {
        // Win32 never routes mouse input to disabled windows; the click
        // falls through to the parent. The Syncfusion DropDownList drop-button
        // is disabled (SfButton Enabled=false in the host layout dump) yet
        // paints an arrow: clicks on it must reach the combo toggle instead
        // of dying on the button. HitTestDeep used to test only Visible.
        //
        // The oracle is ActiveControl (what NoteFocus routed the hit to),
        // not Click delivery: Click callbacks cross the application pump and
        // flake under full-suite parallel static pollution, while routing
        // state is synchronous. Click delivery itself is proven by browser
        // evidence (DDL popup opens on the formerly-dead arrow click).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Fallthrough", Width = 400, Height = 300 };
        using var bottom = new FormsButton { Text = "Bottom", Left = 50, Top = 50, Width = 120, Height = 40 };
        using var top = new FormsButton { Text = "Top", Left = 50, Top = 50, Width = 120, Height = 40 };
        dlg.Controls.Add(bottom);
        dlg.Controls.Add(top);
        top.Enabled = false;
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        dlg.Left = 100;
        dlg.Top = 100;
        dlg.Show();
        try
        {
            int mx = dlg.Left + 50 + (120 / 2);
            int my = dlg.Top + 30 + 50 + (40 / 2);
            host.OnMouseDown(mx, my);
            Assert.Same(bottom, dlg.ActiveControl);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
