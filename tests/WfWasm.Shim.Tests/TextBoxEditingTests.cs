using WfWasm.Shim;

public class TextBoxEditingTests
{
    [Fact]
    public void Typing_InsertsAtCaretWhenFocused()
    {
        var box = new TextBox("txt") { Focused = true };
        box.HandleTextInput("202");
        box.HandleTextInput("6");
        Assert.Equal("2026", box.Text);
        Assert.Equal(4, box.CaretIndex);
    }

    [Fact]
    public void Typing_IgnoredWhenNotFocused()
    {
        var box = new TextBox("txt") { Text = "ab" };
        box.HandleTextInput("XY");
        Assert.Equal("ab", box.Text);
        Assert.Equal(0, box.CaretIndex);
    }

    [Fact]
    public void Typing_InsertsMidStringAndDropsNewlines()
    {
        var box = new TextBox("txt") { Text = "ac", Focused = true };
        box.MoveCaretTo(1);
        box.HandleTextInput("b\ncd\ref");
        Assert.Equal("abcdefc", box.Text);
        Assert.Equal(6, box.CaretIndex);
    }

    [Fact]
    public void Backspace_DeletesBeforeCaret()
    {
        var box = new TextBox("txt") { Text = "abc", Focused = true };
        box.MoveCaretTo(3);
        box.HandleBackspace();
        Assert.Equal("ab", box.Text);
        Assert.Equal(2, box.CaretIndex);
    }

    [Fact]
    public void Backspace_NoOpAtStartOrUnfocused()
    {
        var box = new TextBox("txt") { Text = "ab", Focused = true };
        box.MoveCaretTo(0);
        box.HandleBackspace();
        Assert.Equal("ab", box.Text);

        box.Focused = false;
        box.MoveCaretTo(2);
        box.HandleBackspace();
        Assert.Equal("ab", box.Text);
    }

    [Fact]
    public void Caret_ClampsToText()
    {
        var box = new TextBox("txt") { Text = "ab" };
        box.MoveCaretTo(99);
        Assert.Equal(2, box.CaretIndex);
        box.MoveCaretTo(-5);
        Assert.Equal(0, box.CaretIndex);
        box.Text = "a";
        Assert.Equal(0, box.CaretIndex);
    }
}
