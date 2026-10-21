using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// The headless SendMessage shim has no native list view behind a handle, so
// LVM_SETITEMSTATE / LVM_GETITEMSTATE / LVM_GETSELECTEDCOUNT /
// LVM_GETNEXTITEM / LVM_SETSELECTIONMARK are answered from managed state.
// The shim used to fall through to default, which kept no selection on
// handle-created list views: row clicks toggled ListViewItem.Selected into
// the void, SelectedItems read back empty, and Done-style handlers iterated
// nothing (silent, zero errors).
public class ListViewSelectionShimTests
{
    private static ListView ThreeRows()
    {
        var list = new ListView();
        list.Items.Add("a");
        list.Items.Add("b");
        list.Items.Add("c");
        _ = list.Handle; // force handle creation: selection reads LVM_GETNEXTITEM
        return list;
    }

    [Fact]
    public void RowSelectRoundTripsThroughShim()
    {
        using ListView list = ThreeRows();
        list.Items[1].Selected = true;
        Assert.True(list.Items[1].Selected);
        Assert.Equal(1, list.SelectedItems.Count);
        Assert.Equal("b", list.SelectedItems[0].Text);
        Assert.Equal(new[] { 1 }, list.SelectedIndices.Cast<int>().ToArray());
    }

    [Fact]
    public void RowToggleOffClearsSelection()
    {
        using ListView list = ThreeRows();
        list.Items[1].Selected = true;
        list.Items[1].Selected = false;
        Assert.False(list.Items[1].Selected);
        Assert.Equal(0, list.SelectedItems.Count);
        Assert.Equal(0, list.SelectedIndices.Count);
    }

    [Fact]
    public void MultiSelectRoundTripsThroughShim()
    {
        using ListView list = ThreeRows();
        list.Items[0].Selected = true;
        list.Items[2].Selected = true;
        Assert.Equal(2, list.SelectedItems.Count);
        Assert.Equal(new[] { 0, 2 }, list.SelectedIndices.Cast<int>().ToArray());
    }

    [Fact]
    public void ItemIndexResolvesThroughShim()
    {
        using ListView list = ThreeRows();
        Assert.Equal(0, list.Items[0].Index);
        Assert.Equal(1, list.Items[1].Index);
        Assert.Equal(2, list.Items[2].Index);
    }

    [Fact]
    public void UnselectedRowsReadBackEmpty()
    {
        using ListView list = ThreeRows();
        Assert.Equal(0, list.SelectedItems.Count);
        Assert.False(list.Items[0].Selected);
    }
}
