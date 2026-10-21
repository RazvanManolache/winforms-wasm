using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsForm = System.Windows.Forms.Form;

/// <summary>
/// Modal Cancel-button contract (eval 2026-10-17 pass 5): clicking Cancel
/// in the Calendar "Add Event" dialog repaints but never dismisses it
/// (Escape works). The app's handler assigns Form.DialogResult directly
/// (grp2Calendar EventForm.btnCancel_Click: base.DialogResult =
/// DialogResult.Cancel). Driven through the real browser path
/// (OnMouseDown/OnMouseUp, not the synthesized OnClick shortcut), the
/// Click never dismisses the tracked modal. Direct OnClick delivery and
/// explicit Close() both work (controls below), isolating the down/up
/// Click synthesis over modals.
/// </summary>
public sealed class EvalModalCancelTests
{
    [Fact]
    public void MouseDownUp_On_Cancel_Dismisses_Modal()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "CancelDlg", Width = 360, Height = 240 };
        var cancel = new FormsButton
        {
            Name = "cancelBtn",
            Text = "Cancel",
            Left = 100,
            Top = 100,
            Width = 80,
            Height = 26,
        };
        cancel.Click += (_, _) => dlg.DialogResult = FormsDialogResult.Cancel;
        dlg.Controls.Add(cancel);
        dlg.CancelButton = cancel;
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            int cx = dlg.Location.X + cancel.Left + (cancel.Width / 2);
            int cy = dlg.Location.Y + 30 + cancel.Top + (cancel.Height / 2);
            host.OnMouseDown(cx, cy);
            host.OnMouseUp(cx, cy);
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "mouse down/up on Cancel must dismiss the modal");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Button_DialogResult_OnClick_Direct_Closes_Modal()
    {
        // Control: synthesized OnClick delivery to a Button.DialogResult
        // button already dismisses.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "BtnDlg", Width = 360, Height = 240 };
        var cancel = new FormsButton
        {
            Name = "cancelBtn",
            Text = "Cancel",
            Left = 100,
            Top = 100,
            Width = 80,
            Height = 26,
            DialogResult = FormsDialogResult.Cancel,
        };
        dlg.Controls.Add(cancel);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + cancel.Left + (cancel.Width / 2),
                dlg.Location.Y + 30 + cancel.Top + (cancel.Height / 2));
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "direct OnClick on a DialogResultCancel button must dismiss the modal");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Escape_With_Focused_Slider_Dismisses_Modal_Via_CancelButton()
    {
        // Browser repro (Paint.NET Highlights/Shadows): with a slider
        // focused, Escape reaches the control, never the dialog's
        // CancelButton, and the modal stays open. Desktop runs
        // ProcessDialogKey before KeyDown delivery; the host must too.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "EscDlg", Width = 360, Height = 240 };
        var cancel = new FormsButton
        {
            Name = "cancelBtn",
            Text = "Cancel",
            Left = 200,
            Top = 150,
            Width = 80,
            Height = 26,
        };
        bool cancelClicked = false;
        cancel.Click += (_, _) => { cancelClicked = true; dlg.DialogResult = FormsDialogResult.Cancel; };
        var slider = new System.Windows.Forms.TrackBar
        {
            Name = "slider",
            Left = 20,
            Top = 20,
            Width = 200,
        };
        dlg.Controls.Add(cancel);
        dlg.Controls.Add(slider);
        dlg.CancelButton = cancel;
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            dlg.ActiveControl = slider;
            host.OnKeyDown("Escape");
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "Escape with a focused slider must dismiss the modal via CancelButton");
            Assert.True(cancelClicked, "Escape must reach the dialog CancelButton (ProcessDialogKey order), not the host fallback close");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Explicit_Close_OnClick_Direct_Closes_Modal()
    {
        // Control: an explicit Close() from a directly delivered Click
        // already dismisses.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "CloseDlg", Width = 360, Height = 240 };
        var cancel = new FormsButton
        {
            Name = "cancelBtn",
            Text = "Cancel",
            Left = 100,
            Top = 100,
            Width = 80,
            Height = 26,
        };
        cancel.Click += (_, _) => dlg.Close();
        dlg.Controls.Add(cancel);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + cancel.Left + (cancel.Width / 2),
                dlg.Location.Y + 30 + cancel.Top + (cancel.Height / 2));
            Assert.False(
                System.Windows.Forms.WasmDialogHost.TopModal(out _),
                "explicit Close() in the handler must dismiss the modal");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
