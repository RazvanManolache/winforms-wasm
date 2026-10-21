using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;

public sealed class DropdownKeyNavTests
{
    [Fact]
    public void ArrowDown_Then_Enter_Activates_Menu_Item()
    {
        // An open menu captures navigation keys (desktop menu loop):
        // Down selects, Enter activates. The fork runs no modal menu
        // loop headless, so the host drives vendor selection + click
        // (Paint.NET Adjustments keyboard path: 9x ArrowDown + Enter did
        // nothing before this emulation).
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var first = new System.Windows.Forms.ToolStripMenuItem("First");
        var second = new System.Windows.Forms.ToolStripMenuItem("Second");
        bool clicked = false;
        string? clickedName = null;
        first.Click += (_, _) => { clicked = true; clickedName = "First"; };
        second.Click += (_, _) => { clicked = true; clickedName = "Second"; };
        mi.DropDownItems.Add(first);
        mi.DropDownItems.Add(second);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        strip.PerformLayout();
        WaitForLayout(() => mi.Bounds.Width, "menu item");
        try
        {
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            Assert.True(mi.DropDown.Visible, "drop opened");
            host.OnKeyDown("ArrowDown");
            Assert.True(first.Selected, "first item selected after Down");
            host.OnKeyDown("ArrowDown");
            Assert.True(second.Selected, "second item selected after Down x2");
            host.OnKeyDown("Enter");
            Assert.True(clicked, "Enter activated the selected item");
            Assert.Equal("Second", clickedName);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void ArrowUp_Wraps_To_Last_Item()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var first = new System.Windows.Forms.ToolStripMenuItem("First");
        var second = new System.Windows.Forms.ToolStripMenuItem("Second");
        mi.DropDownItems.Add(first);
        mi.DropDownItems.Add(second);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        strip.PerformLayout();
        WaitForLayout(() => mi.Bounds.Width, "menu item");
        try
        {
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            Assert.True(mi.DropDown.Visible, "drop opened");
            host.OnKeyDown("ArrowUp");
            Assert.True(second.Selected, "Up with no selection wraps to last");
            host.OnKeyDown("ArrowUp");
            Assert.True(first.Selected, "Up moves to previous");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    [Fact]
    public void Enter_With_No_Selection_Activates_First_Item()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var first = new System.Windows.Forms.ToolStripMenuItem("First");
        bool clicked = false;
        first.Click += (_, _) => clicked = true;
        mi.DropDownItems.Add(first);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        strip.PerformLayout();
        WaitForLayout(() => mi.Bounds.Width, "menu item");
        try
        {
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            Assert.True(mi.DropDown.Visible, "drop opened");
            host.OnKeyDown("Enter");
            Assert.True(clicked, "Enter with no selection activates first item");
            Assert.False(mi.DropDown.Visible, "menu dismisses before executing (desktop order)");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }

    private static void WaitForLayout(System.Func<int> width, string what)
    {
        for (int i = 0; i < 100 && width() <= 0; i++)
        {
            try { System.Windows.Forms.Application.DoEvents(); }
            catch { }
            System.Threading.Thread.Sleep(20);
        }
        Assert.True(width() > 0, what + " never laid out");
    }
}
