using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;

public sealed class DropdownClickTests
{
    [Fact]
    public void Dropdown_Item_Click_Fires_Over_Document()
    {
        // An open dropdown paints above everything: its items must fire
        // even when the point also lies inside the document page bounds
        // (Adjustments/Levels over the canvas was swallowed as a brush
        // press). Leaf clicks also dismiss the menu, desktop parity.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var leaf = new System.Windows.Forms.ToolStripMenuItem("Leaf");
        bool clicked = false;
        leaf.Click += (_, _) => clicked = true;
        mi.DropDownItems.Add(leaf);
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
            var drop = mi.DropDown;
            Assert.True(drop.Visible, "drop opened");
            drop.Width = 200;
            drop.Height = 100;
            drop.PerformLayout();
            WaitForLayout(() => leaf.Bounds.Width, "leaf item");
            Assert.Same(leaf, drop.GetItemAt(leaf.Bounds.X + 5, leaf.Bounds.Y + 5));
            host.SetDocumentPageBounds(0, 0, 1200, 950);
            string Sig() => drop.Location.X + "," + drop.Location.Y + "," + leaf.Bounds.X + "," + leaf.Bounds.Y + "," + leaf.Bounds.Width + "," + leaf.Bounds.Height;
            string s1 = Sig();
            for (int i = 0; i < 50; i++)
            {
                System.Threading.Thread.Sleep(20);
                try { System.Windows.Forms.Application.DoEvents(); }
                catch { }
                string s2 = Sig();
                if (s2 == s1) break;
                s1 = s2;
            }
            int lx = dlg.Left + strip.Left + drop.Location.X + leaf.Bounds.X + 5;
            int ly = dlg.Top + 30 + strip.Top + drop.Location.Y + leaf.Bounds.Y + 5;
            host.OnMouseDown(lx, ly);
            host.OnClick(lx, ly);
            Assert.True(clicked);
            Assert.False(drop.Visible);
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
