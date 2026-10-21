using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// The headless SendMessage shim has no native edit control behind a handle,
// so EM_SETSEL / EM_GETSEL / EM_REPLACESEL are answered from managed state.
// The shim used to fall through to default, which lost every selection on
// handle-created text boxes: SelectionStart never stuck (typed chars
// inserted at 0), SelectAll was invisible to handlers, and SelectedText
// assignment silently did nothing.
public class TextBoxSelectionShimTests
{
    [Fact]
    public void CaretPositionRoundTripsThroughShim()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        _ = box.Handle; // force handle creation: selection reads EM_GETSEL
        box.SelectionStart = 2;
        Assert.Equal(2, box.SelectionStart);
        Assert.Equal(0, box.SelectionLength);
    }

    [Fact]
    public void SelectRangeRoundTripsThroughShim()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        _ = box.Handle;
        box.Select(1, 2);
        Assert.Equal(1, box.SelectionStart);
        Assert.Equal(2, box.SelectionLength);
        Assert.Equal("bc", box.SelectedText);
    }

    [Fact]
    public void SelectAllReportsFullRange()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        _ = box.Handle;
        box.SelectAll();
        Assert.Equal(0, box.SelectionStart);
        Assert.Equal(4, box.SelectionLength);
    }

    [Fact]
    public void SelectedTextReplacementEditsText()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        _ = box.Handle;
        box.Select(1, 2);
        box.SelectedText = "X";
        Assert.Equal("aXd", box.Text);
        Assert.Equal(2, box.SelectionStart);
        Assert.Equal(0, box.SelectionLength);
    }

    [Fact]
    public void SelectionClampsToTextLength()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        _ = box.Handle;
        box.Select(100, 5);
        Assert.Equal(4, box.SelectionStart);
        Assert.Equal(0, box.SelectionLength);
    }

    [Fact]
    public void ShrunkTextClampsSelectionOnRead()
    {
        using var box = new TextBox();
        box.Text = "abcd";
        _ = box.Handle;
        box.Select(1, 3);
        box.Text = "ab";
        Assert.Equal(1, box.SelectionStart);
        Assert.Equal(1, box.SelectionLength);
    }
}
