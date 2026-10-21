using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsForm = System.Windows.Forms.Form;

public sealed class DropdownEscapeTests
{
    [Fact]
    public void Close_Tolerates_Reentrant_Close_During_Hide()
    {
        // Paint Adjustments host-fatal (stack-proven, browser-reproduced):
        // Escape on an open dropdown threw "Collection was modified" out
        // of CloseOpenDropdowns (outer enumerator over the live tracked
        // list) and killed the Blazor session. Vendor menu teardown
        // re-enters the close path while the outer iteration is in flight
        // (the final Clear is the mid-loop mutator); both close loops must
        // therefore run on snapshots. The nested close below models the
        // vendor re-entrancy deterministically: hiding the dropdown nests
        // a second close, whose Clear must not break the outer iteration.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        using var dlg = new FormsForm { Text = "Dock", Width = 500, Height = 400 };
        var strip = new System.Windows.Forms.MenuStrip();
        var mi = new System.Windows.Forms.ToolStripMenuItem("M");
        var leaf = new System.Windows.Forms.ToolStripMenuItem("Leaf");
        mi.DropDownItems.Add(leaf);
        strip.Items.Add(mi);
        dlg.Controls.Add(strip);
        dlg.StartPosition = System.Windows.Forms.FormStartPosition.Manual; dlg.Left = 100; dlg.Top = 100; dlg.Show();
        try
        {
            int mx = dlg.Left + strip.Left + mi.Bounds.X + (mi.Bounds.Width / 2);
            int my = dlg.Top + 30 + strip.Top + mi.Bounds.Y + (mi.Bounds.Height / 2);
            host.OnClick(mx, my);
            var drop = mi.DropDown;
            Assert.True(drop.Visible, "drop opened");
            // Guarantee tracking (mirrors the open path).
            System.Reflection.MethodInfo track = typeof(WinFormsAppHost).GetMethod("TrackDropdown",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            track.Invoke(host, new object[] { drop, 0, 0 });
            System.Reflection.MethodInfo close = typeof(WinFormsAppHost).GetMethod("CloseOpenDropdowns",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            bool nested = false;
            drop.VisibleChanged += (_, _) =>
            {
                // Vendor-style teardown: closing nests another close once.
                if (!drop.Visible && !nested)
                {
                    nested = true;
                    close.Invoke(host, null);
                }
            };
            var ex = Record.Exception(() => close.Invoke(host, null));
            Assert.Null(ex);
            Assert.True(nested, "nested close fired");
            Assert.False(drop.Visible, "drop closed");
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(dlg);
        }
    }
}
