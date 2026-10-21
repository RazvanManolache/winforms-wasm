using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsForm = System.Windows.Forms.Form;

public sealed class ModalDialogTests
{
    [Fact]
    public void ShowDialog_Paints_Title_Above_Main_Form_And_Returns_None()
    {
        // File>New must open: the dialog presents topmost through the
        // host (the browser thread cannot run a nested RunDialog pump),
        // so ShowDialog returns immediately while the window stays open.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "NewImageDlg", Width = 360, Height = 240 };
        Assert.Equal(FormsDialogResult.None, dlg.ShowDialog());
        try
        {
            var recorder = new RecordingRenderer();
            host.Paint(recorder);
            Assert.Contains(recorder.Commands,
                c => c is DrawTextCommand dt && dt.Text.Contains("NewImageDlg"));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Click_Reaches_Dialog_Button()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dlg", Width = 360, Height = 240 };
        bool okClicked = false;
        var ok = new FormsButton
        {
            Name = "okBtn",
            Text = "OK",
            Left = 100,
            Top = 100,
            Width = 80,
            Height = 26,
        };
        ok.Click += (_, _) => okClicked = true;
        dlg.Controls.Add(ok);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + ok.Left + (ok.Width / 2),
                dlg.Location.Y + 30 + ok.Top + (ok.Height / 2));
            Assert.True(okClicked);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Click_Reaches_Modal_Over_Document_Canvas()
    {
        // Effect dialogs (Levels) sit over the document page: the click
        // must reach the modal button instead of dying in the canvas
        // early-out. Regression: Cancel click logged "skipped on document
        // canvas" while the Levels dialog was open above the page.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Levels", Width = 460, Height = 275 };
        bool cancelClicked = false;
        var cancel = new FormsButton
        {
            Name = "cancelBtn",
            Text = "Cancel",
            Left = 340,
            Top = 200,
            Width = 80,
            Height = 26,
        };
        cancel.Click += (_, _) => cancelClicked = true;
        dlg.Controls.Add(cancel);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            int cx = dlg.Location.X + cancel.Left + (cancel.Width / 2);
            int cy = dlg.Location.Y + 30 + cancel.Top + (cancel.Height / 2);
            host.SetDocumentPageBounds(cx - 200, cy - 200, 400, 400);
            host.OnClick(cx, cy);
            Assert.True(cancelClicked);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    private sealed class FakePushButton : System.Windows.Forms.Control, System.Windows.Forms.IButtonControl
    {
        public bool Clicked;
        public System.Windows.Forms.DialogResult DialogResult { get; set; } =
            System.Windows.Forms.DialogResult.None;
        public void NotifyDefault(bool value) { }
        public void PerformClick()
        {
            Clicked = true;
            OnClick(System.EventArgs.Empty);
        }
    }

    // NOTE: no unit test for the X Cancel-carry (ModalOnClick stamps
    // Cancel before Close): the fork's DeferAbortClose skip is gated on
    // PDN EffectConfigDialog type names, so a plain test Form closes
    // identically pre/post fix. Proof is browser-side (Levels X stuck
    // pre-fix with vis=True post-Close, 27k-px close post-fix).
    [Fact]
    public void Click_Reaches_IButtonControl_And_Settles()
    {
        // PDN PdnPushButton derives from GdiBufferedAnimationControl, not
        // Button: the modal click must activate it through IButtonControl
        // and settle a chosen OK result (no headless pump to observe it).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "PushDlg", Width = 360, Height = 240 };
        var push = new FakePushButton
        {
            Name = "okPush",
            Left = 100,
            Top = 100,
            Width = 80,
            Height = 26,
        };
        push.Click += (_, _) => dlg.DialogResult = FormsDialogResult.OK;
        dlg.Controls.Add(push);
        dlg.ShowDialog();
        try
        {
            host.Paint(new RecordingRenderer());
            host.OnClick(
                dlg.Location.X + push.Left + (push.Width / 2),
                dlg.Location.Y + 30 + push.Top + (push.Height / 2));
            Assert.True(push.Clicked);
            Assert.Equal(FormsDialogResult.OK, dlg.DialogResult);
            Assert.False(System.Windows.Forms.WasmDialogHost.TopModal(out _));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Closed_Dialog_Disappears_From_Paint_And_Modal()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var dlg = new FormsForm { Text = "GoneDlg", Width = 360, Height = 240 };
        dlg.ShowDialog();
        try
        {
            Assert.True(System.Windows.Forms.WasmDialogHost.TopModal(out _));
            dlg.Close();
            var recorder = new RecordingRenderer();
            host.Paint(recorder);
            Assert.DoesNotContain(recorder.Commands,
                c => c is DrawTextCommand dt && dt.Text.Contains("GoneDlg"));
            Assert.False(System.Windows.Forms.WasmDialogHost.TopModal(out _));
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
            dlg.Dispose();
        }
    }
}
