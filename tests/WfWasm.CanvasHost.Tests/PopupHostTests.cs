using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;

public sealed class PopupHostTests
{
    // Stand-in for a real top-level popup (bookshelf dropdown carries
    // WS_POPUP): plain controls seed WS_CHILD and the tracking funnels
    // reject (and untrack) them since the eval8 phantom fix.
    private sealed class PopupControl : FormsControl
    {
        protected override System.Windows.Forms.CreateParams CreateParams
        {
            get
            {
                System.Windows.Forms.CreateParams cp = base.CreateParams;
                cp.Style |= unchecked((int)0x80000000);
                cp.Style &= ~0x40000000;
                return cp;
            }
        }
    }

    [Fact]
    public void Tracked_Popup_Paints_Above_Form()
    {
        // Top-level popups (Telerik context menus) are parentless non-Form
        // controls: the host paints tracked ones above dialogs via the
        // self-painted path, with an unconditional drop shadow.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var popup = new PopupControl
        {
            Name = "menu",
            Left = 400,
            Top = 300,
            Width = 160,
            Height = 120,
        };
        popup.Visible = true;
        System.Windows.Forms.WasmPopupHost.Track(popup);
        try
        {
            _ = popup.Handle; // live handle, as a ShowWindow-tracked popup has
            var recorder = new RecordingRenderer();
            host.Paint(recorder);
            Assert.Contains(recorder.Commands,
                c => c is FillRectCommand f && f.X == 403 && f.Y == 303);
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }

    [Fact]
    public void Click_Reaches_Popup_Child_Control()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var popup = new PopupControl
        {
            Name = "menu",
            Left = 400,
            Top = 300,
            Width = 160,
            Height = 120,
        };
        bool childClicked = false;
        var child = new FormsControl
        {
            Name = "item",
            Left = 10,
            Top = 10,
            Width = 100,
            Height = 24,
        };
        child.MouseClick += (_, _) => childClicked = true;
        popup.Controls.Add(child);
        popup.Visible = true;
        System.Windows.Forms.WasmPopupHost.Track(popup);
        try
        {
            _ = popup.Handle; // live handle, as a ShowWindow-tracked popup has
            host.Paint(new RecordingRenderer());
            host.OnClick(400 + 10 + 50, 300 + 10 + 12);
            Assert.True(childClicked);
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }

    [Fact]
    public void Outside_Down_With_Undismissable_Popup_Falls_Through()
    {
        // Outside presses dismiss through Telerik's manager and are
        // swallowed only if something actually closed. Without Telerik
        // loaded the reflection dismiss is a no-op, so the press falls
        // through to normal dispatch (eval8 ERP/hotel/RTE: boot phantoms
        // must never eat input). The popup stays tracked.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var popup = new PopupControl
        {
            Name = "menu",
            Left = 400,
            Top = 300,
            Width = 160,
            Height = 120,
        };
        popup.Visible = true;
        System.Windows.Forms.WasmPopupHost.Track(popup);
        try
        {
            _ = popup.Handle; // live handle, as a ShowWindow-tracked popup has
            host.Paint(new RecordingRenderer());
            var ex = Record.Exception(() => host.OnMouseDown(50, 500));
            Assert.Null(ex);
            Assert.Contains(System.Windows.Forms.WasmPopupHost.Snapshot(),
                c => ReferenceEquals(c, popup));
        }
        finally
        {
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
        }
    }

    [Fact]
    public void Popup_Miss_Falls_Through_To_Modal_Button()
    {
        // eval8 ERP/hotel/RTE: boot phantoms ate every press. An outside
        // click that dismisses nothing must reach the modal button.
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
        using var popup = new PopupControl
        {
            Name = "phantom",
            Left = 0,
            Top = 0,
            Width = 40,
            Height = 40,
        };
        popup.Visible = true;
        _ = popup.Handle;
        System.Windows.Forms.WasmPopupHost.Track(popup);
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
            System.Windows.Forms.WasmPopupHost.Untrack(popup);
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
