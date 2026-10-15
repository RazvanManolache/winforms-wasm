using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsForm = System.Windows.Forms.Form;

public sealed class DockWindowTests
{
    [Fact]
    public void Click_Reaches_Modeless_Dock_Button()
    {
        // Modeless tracked windows (PDN docks) paint above the scene, so
        // clicks route into their controls even though they are not scene
        // children and no modal is open.
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
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
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 500; dlg.Top = 500; dlg.Show();
        try
        {
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
    public void Title_Drag_Moves_Modeless_Dock()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 500; dlg.Top = 500; dlg.Show();
        try
        {
            host.OnMouseDown(680, 510);
            host.OnMouseMove(710, 525);
            host.OnMouseUp(710, 525);
            Assert.Equal(530, dlg.Location.X);
            Assert.Equal(515, dlg.Location.Y);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Close_X_Hides_Modeless_Dock()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 360, Height = 240 };
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 500; dlg.Top = 500; dlg.Show();
        try
        {
            Assert.True(dlg.Visible);
            host.OnClick(dlg.Location.X + dlg.Width - 19, dlg.Location.Y + 15);
            Assert.True(dlg.IsDisposed || !dlg.Visible);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Placement_Grows_Dock_To_Fit_Visible_Content()
    {
        // PDN tool windows are born smaller than their contents headless
        // (Colors 209x217 vs ~376x250 of sliders); placement grows the
        // frame to the visible-children union and pulls it into view so
        // no drag is needed to reveal the clipped rows.
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        host.ViewWidth = 500;
        host.ViewHeight = 400;
        using var dlg = new FormsForm { Text = "Dock", Width = 209, Height = 217 };
        var tall = new FormsButton { Left = 300, Top = 230, Width = 76, Height = 20, Visible = true };
        var hidden = new FormsButton { Left = 0, Top = 0, Width = 900, Height = 900, Visible = false };
        dlg.Controls.Add(tall);
        dlg.Controls.Add(hidden);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 10; dlg.Top = 300; dlg.Show();
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.Equal(376, dlg.Width);
            Assert.Equal(250, dlg.Height);
            Assert.Equal(10, dlg.Left);
            Assert.Equal(120, dlg.Top);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Placement_Never_Shrinks_Dock()
    {
        var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        host.ViewWidth = 500;
        host.ViewHeight = 400;
        using var dlg = new FormsForm { Text = "Dock", Width = 300, Height = 200 };
        var small = new FormsButton { Left = 10, Top = 10, Width = 50, Height = 50, Visible = true };
        dlg.Controls.Add(small);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 10; dlg.Top = 10; dlg.Show();
        try
        {
            host.Paint(new RecordingRenderer());
            Assert.Equal(300, dlg.Width);
            Assert.Equal(200, dlg.Height);
            Assert.Equal(10, dlg.Left);
            Assert.Equal(10, dlg.Top);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}



