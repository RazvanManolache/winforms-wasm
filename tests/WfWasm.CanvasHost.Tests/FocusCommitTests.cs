using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsForm = System.Windows.Forms.Form;
using FormsTextBox = System.Windows.Forms.TextBox;

public sealed class FocusCommitTests
{
    [Fact]
    public void Typing_Replaces_Selected_Text_In_Focused_Box()
    {
        // Host edit emulation (no native EDIT control headless): Ctrl+A
        // selects, char keys replace the selection (desktop KeyPress
        // default). PDN numeric edits rely on this to receive values.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "TypeDlg", Width = 360, Height = 240 };
        var box = new FormsTextBox
        {
            Name = "edit",
            Text = "0",
            Left = 20,
            Top = 20,
            Width = 100,
            Height = 24,
        };
        dlg.Controls.Add(box);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + box.Left + (box.Width / 2),
                dlg.Location.Y + 30 + box.Top + (box.Height / 2));
            host.OnKeyDown("a", ctrl: true);
            host.OnKeyDown("1");
            host.OnKeyDown("0");
            host.OnKeyDown("0");
            Assert.Equal("100", box.Text);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Enter_Accept_Leaves_Edit_Before_Activate()
    {
        // Keyboard OK (AcceptButton) must commit the edit too: desktop
        // validates on close, so a typed value applies (Levels Enter-OK).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "AcceptDlg", Width = 360, Height = 240 };
        var box = new FormsTextBox
        {
            Name = "edit",
            Text = "0",
            Left = 20,
            Top = 20,
            Width = 100,
            Height = 24,
        };
        var ok = new FormsButton
        {
            Name = "okBtn",
            Text = "OK",
            Left = 200,
            Top = 100,
            Width = 80,
            Height = 26,
        };
        ok.DialogResult = FormsDialogResult.OK;
        dlg.AcceptButton = ok;
        bool left = false;
        box.Leave += (_, _) => left = true;
        dlg.Controls.Add(box);
        dlg.Controls.Add(ok);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + box.Left + (box.Width / 2),
                dlg.Location.Y + 30 + box.Top + (box.Height / 2));
            host.OnKeyDown("Enter");
            Assert.True(left);
            Assert.False(System.Windows.Forms.WasmDialogHost.TopModal(out _));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Focus_Move_Fires_Leave_On_Previous_Control()
    {
        // PDN numeric edits commit (ParseEditText) on Leave. Clicking a
        // dialog button must move focus first (desktop order) so the edit
        // commits before the button activates; otherwise OK applies stale
        // values (Levels output-low repro: typed 100, applied 0).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "LeaveDlg", Width = 360, Height = 240 };
        var box = new FormsTextBox
        {
            Name = "edit",
            Text = "0",
            Left = 20,
            Top = 20,
            Width = 100,
            Height = 24,
        };
        var ok = new FormsButton
        {
            Name = "okBtn",
            Text = "OK",
            Left = 200,
            Top = 100,
            Width = 80,
            Height = 26,
        };
        bool left = false;
        box.Leave += (_, _) => left = true;
        dlg.Controls.Add(box);
        dlg.Controls.Add(ok);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + box.Left + (box.Width / 2),
                dlg.Location.Y + 30 + box.Top + (box.Height / 2));
            Assert.False(left);
            host.OnClick(
                dlg.Location.X + ok.Left + (ok.Width / 2),
                dlg.Location.Y + 30 + ok.Top + (ok.Height / 2));
            Assert.True(left);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
