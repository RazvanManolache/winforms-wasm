extern alias WasmDraw;

namespace WfWasm.WinForms.Tests;

// Legacy-menu overlay contract: construction works (upstream throws
// PNSE), items/endabled/click behave in memory, Show raises Popup and
// records the request for a future host popup bridge.
// Regression: PNSE Menu..ctor <- RadTitleBar..ctor <- BookShelf.MainForm
// create (every RadForm-based Telerik demo failed to construct).
public sealed class MenuOverlayTests
{
    [Fact]
    public void TitleBar_Shape_Builds_And_Pops_Up()
    {
        // The RadTitleBar..ctor shape: items with Click handlers in a
        // ContextMenu, Enabled toggled from Popup.
        var menu = new System.Windows.Forms.ContextMenu();
        bool clicked = false;
        bool popped = false;
        menu.Popup += (_, _) =>
        {
            popped = true;
            menu.MenuItems[0].Enabled = false;
        };
        var restore = new System.Windows.Forms.MenuItem("Restore");
        restore.Click += (_, _) => clicked = true;
        menu.MenuItems.Add(restore);
        menu.MenuItems.Add("Move");
        menu.MenuItems.Add("Close", (_, _) => { });

        Assert.Equal(3, menu.MenuItems.Count);
        Assert.Same(menu, restore.Parent);
        Assert.Equal(0, restore.Index);
        Assert.Equal(2, menu.MenuItems.IndexOf(menu.MenuItems[2]));

        using var host = new System.Windows.Forms.Control();
        menu.Show(host, new WasmDraw::System.Drawing.Point(10, 20));
        Assert.True(popped);
        Assert.Same(host, menu.SourceControl);
        Assert.False(menu.MenuItems[0].Enabled);
        Assert.NotNull(System.Windows.Forms.ContextMenu.LastShowRequest);

        menu.MenuItems[0].Enabled = true;
        restore.PerformClick();
        Assert.True(clicked);
    }

    [Fact]
    public void Collection_Ops_Track_Parent_And_Index()
    {
        var menu = new System.Windows.Forms.MainMenu();
        var a = menu.MenuItems.Add("A");
        var b = new System.Windows.Forms.MenuItem("B");
        menu.MenuItems.Add(1, b);
        Assert.Equal(0, a.Index);
        Assert.Equal(1, b.Index);
        b.Index = 0;
        Assert.Equal(0, b.Index);
        Assert.Equal(1, a.Index);
        Assert.True(menu.MenuItems.Contains(a));
        menu.MenuItems.Remove(a);
        Assert.Null(a.Parent);
        Assert.Single(menu.MenuItems);
        menu.MenuItems.Clear();
        Assert.Empty(menu.MenuItems);
        Assert.Null(b.Parent);
    }

    [Fact]
    public void CloneMenu_Deep_Copies_Submenu()
    {
        var file = new System.Windows.Forms.MenuItem("File");
        file.MenuItems.Add("Open");
        file.Checked = true;
        file.Shortcut = System.Windows.Forms.Shortcut.CtrlO;
        var clone = file.CloneMenu();
        Assert.Equal("File", clone.Text);
        Assert.True(clone.Checked);
        Assert.Equal(System.Windows.Forms.Shortcut.CtrlO, clone.Shortcut);
        Assert.Single(clone.MenuItems);
        Assert.Same(clone, clone.MenuItems[0].Parent);
        Assert.NotSame(file.MenuItems[0], clone.MenuItems[0]);
    }

    [Fact]
    public void Defaults_Match_Netfx()
    {
        var item = new System.Windows.Forms.MenuItem();
        Assert.True(item.Enabled);
        Assert.True(item.Visible);
        Assert.True(item.ShowShortcut);
        Assert.False(item.Checked);
        Assert.Equal(System.Windows.Forms.MenuMerge.Add, item.MergeType);
        Assert.Equal(System.Windows.Forms.Shortcut.None, item.Shortcut);
        Assert.Equal('\0', item.Mnemonic);
        Assert.Equal(-1, item.Index);
        var amp = new System.Windows.Forms.MenuItem("&File");
        Assert.Equal('F', amp.Mnemonic);
        var menu = new System.Windows.Forms.ContextMenu();
        Assert.Same(menu.MenuItems, menu.MenuItems);
        Assert.Equal(System.Windows.Forms.RightToLeft.No, menu.RightToLeft);
        Assert.False(menu.IsParent);
        var main = new System.Windows.Forms.MainMenu();
        Assert.Equal(System.Windows.Forms.RightToLeft.Inherit, main.RightToLeft);
    }
}
