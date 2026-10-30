using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;

// Pins the real Syncfusion-combobox route (proven in vivo 2026-10-28:
// Edit-mode list opens as a modeless WasmDialogHost-tracked PopupHost,
// the item press commits on MouseDown via DownTrackedDock/DialogOnDown,
// the popup closes before Click so Click-time routing is not involved).
public sealed class PopupFormClickTests
{
    [Fact]
    public void TrackedDialog_Button_Press_Delivers_To_Dialog_Not_Main()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var main = new FormsForm { Text = "Main", Width = 800, Height = 600 };
        bool mainDown = false;
        var mainBtn = new System.Windows.Forms.Button { Name = "mainBtn", Text = "M", Left = 50, Top = 50, Width = 200, Height = 60 };
        mainBtn.MouseDown += (_, _) => mainDown = true;
        main.Controls.Add(mainBtn);
        main.StartPosition = System.Windows.Forms.FormStartPosition.Manual; main.Left = 0; main.Top = 0; main.Show();
        // Borderless => ChromeHeight 0, so client math needs no magic offset
        // (matches real popup hosts, which are borderless).
        using var pop = new FormsForm { Text = "Pop", Width = 255, Height = 125, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None };
        bool popDown = false;
        var popBtn = new System.Windows.Forms.Button { Name = "popBtn", Text = "P", Left = 100, Top = 40, Width = 80, Height = 30 };
        popBtn.MouseDown += (_, _) => popDown = true;
        pop.Controls.Add(popBtn);
        pop.StartPosition = System.Windows.Forms.FormStartPosition.Manual; pop.Left = 500; pop.Top = 300; pop.Show();
        System.Windows.Forms.WasmDialogHost.Track(pop, modal: false);
        try
        {
            int cx = pop.Left + popBtn.Left + popBtn.Width / 2;
            int cy = pop.Top + popBtn.Top + popBtn.Height / 2;
            host.OnMouseDown(cx, cy);
            Assert.True(popDown, "tracked dialog button never got the press");
            Assert.False(mainDown, "press leaked to the main form");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(pop);
            System.Windows.Forms.WasmDialogHost.Untrack(main);
        }
    }

    [Fact]
    public void UntrackedDialog_Button_Press_Does_Not_Reach_Dialog()
    {
        // Negative control: without tracking the same press must NOT reach
        // the dialog button (guards against silent rerouting through the
        // main tree that would make the positive test pass for the wrong
        // reason).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var pop = new FormsForm { Text = "Pop", Width = 255, Height = 125, FormBorderStyle = System.Windows.Forms.FormBorderStyle.None };
        bool popDown = false;
        var popBtn = new System.Windows.Forms.Button { Name = "popBtn", Text = "P", Left = 100, Top = 40, Width = 80, Height = 30 };
        popBtn.MouseDown += (_, _) => popDown = true;
        pop.Controls.Add(popBtn);
        pop.StartPosition = System.Windows.Forms.FormStartPosition.Manual; pop.Left = 500; pop.Top = 300; pop.Show();
        System.Windows.Forms.WasmDialogHost.Untrack(pop);
        try
        {
            int cx = pop.Left + popBtn.Left + popBtn.Width / 2;
            int cy = pop.Top + popBtn.Top + popBtn.Height / 2;
            host.OnMouseDown(cx, cy);
            Assert.False(popDown, "untracked dialog button got the press");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(pop);
        }
    }
}
