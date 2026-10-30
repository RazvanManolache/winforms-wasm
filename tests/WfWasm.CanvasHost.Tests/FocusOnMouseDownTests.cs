using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsForm = System.Windows.Forms.Form;

public sealed class FocusOnMouseDownTests
{
    [Fact]
    public void MouseDown_Sets_Win32_Focus_On_Clicked_Control()
    {
        // Desktop focuses the clicked window via DefWindowProc; headless only
        // the host can do it. Focus-gated UI (Syncfusion combo arrow path:
        // ComboBoxPopup.Show returns when !ContainsFocus) stays silent
        // without this. Guards the combined NoteFocus (ActiveControl sync,
        // which focuses via FocusActiveControlInternal once the ancestor
        // has focus) + explicit control.Focus() (the cold-boot bootstrap)
        // chain in WinFormsAppHost.OnMouseDown. A negative control that
        // disables only the explicit Focus() still passes when the test
        // pre-focuses another control, so this asserts the end-to-end
        // contract, not one line.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Focus", Width = 400, Height = 300 };
        using var button = new FormsButton { Text = "B", Left = 50, Top = 50, Width = 120, Height = 40 };
        using var other = new FormsButton { Text = "O", Left = 200, Top = 50, Width = 120, Height = 40 };
        dlg.Controls.Add(button);
        dlg.Controls.Add(other);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        dlg.Left = 100;
        dlg.Top = 100;
        dlg.Show();
        try
        {
            Assert.True(other.Focus());
            Assert.False(button.Focused);
            int mx = dlg.Left + button.Left + (button.Width / 2);
            int my = dlg.Top + 30 + button.Top + (button.Height / 2);
            host.OnMouseDown(mx, my);
            Assert.True(button.Focused);
            Assert.False(other.Focused);
            Assert.True(dlg.ContainsFocus);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
