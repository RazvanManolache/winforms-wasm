using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// The headless SendMessage shim has no native combo box behind a handle, so
// CB_GETCURSEL / CB_SETCURSEL / CB_RESETCONTENT are answered from managed
// state. Desktop returns CB_ERR (-1) when nothing is selected; the shim used
// to fall through to 0, which made SelectedIndex lie (0) on handle-created
// but unselected combos and threw (Items[0] of an empty list) out of
// Control.OnHandleDestroyed's text stash on every destroy.
public class ComboBoxSelectionShimTests
{
    [Fact]
    public void EmptyComboWithHandleReportsNoSelection()
    {
        using var combo = new ComboBox();
        _ = combo.Handle; // force handle creation: SelectedIndex reads CB_GETCURSEL
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
    }

    [Fact]
    public void EmptyComboTextReadDoesNotThrow()
    {
        using var combo = new ComboBox();
        _ = combo.Handle;
        // This is what Control.OnHandleDestroyed stashes (_text = Text);
        // it used to throw ArgumentOutOfRangeException here.
        string text = combo.Text;
        Assert.NotNull(text);
    }

    [Fact]
    public void SelectionRoundTripsThroughShim()
    {
        using var combo = new ComboBox();
        combo.Items.Add("a");
        combo.Items.Add("b");
        _ = combo.Handle;
        combo.SelectedIndex = 1;
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("b", combo.SelectedItem);
        combo.SelectedIndex = -1;
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
    }

    [Fact]
    public void ClearResetsSelection()
    {
        using var combo = new ComboBox();
        combo.Items.Add("a");
        combo.Items.Add("b");
        _ = combo.Handle;
        combo.SelectedIndex = 0;
        combo.Items.Clear();
        Assert.Equal(-1, combo.SelectedIndex);
        Assert.Null(combo.SelectedItem);
    }
}
