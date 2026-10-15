using WfWasm.Renderer;
using WfWasm.Shim;

public class CanvasAppTests
{
    private static Label FindLabel(Control root, string name) =>
        root.Children.OfType<Label>().First(l => l.Name == name);

    [Fact]
    public void SmokeOkClick_UpdatesStatus()
    {
        var app = new CanvasApp();
        app.ClickAt(800, 570);
        Assert.Equal("OK clicked!", FindLabel(app.SmokeForm, "status").Text);
    }

    [Fact]
    public void SceneSwitch_Digits()
    {
        var app = new CanvasApp();
        Assert.Equal(AppScene.Smoke, app.Current);
        app.KeyChar('2');
        Assert.Equal(AppScene.Calendar, app.Current);
        app.KeyChar('1');
        Assert.Equal(AppScene.Smoke, app.Current);
    }

    [Fact]
    public void CalendarNav_PrevNextChangesMonth()
    {
        var app = new CanvasApp();
        app.KeyChar('2');
        var label = FindLabel(app.Calendar.Window, "lbdate");
        Assert.Equal("October 2026", label.Text);

        app.ClickAt(470, 90);
        Assert.Equal("September 2026", label.Text);
        app.ClickAt(530, 90);
        Assert.Equal("October 2026", label.Text);
    }

    [Fact]
    public void CalendarGo_InvalidInput_ShowsStatus()
    {
        var app = new CanvasApp();
        app.KeyChar('2');

        app.ClickAt(580, 90);
        app.KeyChar('x');
        app.ClickAt(720, 90);

        Assert.Equal("Enter a valid month (1-12 or name) and year.",
            FindLabel(app.Calendar.Window, "status").Text);
    }

    [Fact]
    public void CalendarGo_ClearAndType_ReachesFebruary()
    {
        var app = new CanvasApp();
        app.KeyChar('2');

        app.ClickAt(580, 90);
        var month = FindTextBox(app.Calendar.Window, "txtMonth");
        Assert.True(month.Focused);
        month.MoveCaretTo(0);
        month.HandleDelete();
        month.HandleDelete();
        app.KeyChar('2');
        app.ClickAt(720, 90);

        Assert.Equal("February 2026", FindLabel(app.Calendar.Window, "lbdate").Text);
    }

    [Fact]
    public void DayClick_OpensEditorPrefilledAndFocused()
    {
        var app = new CanvasApp();
        app.KeyChar('2');

        app.ClickAt(350, 250);

        Assert.Equal(new DateTime(2026, 10, 6), app.Calendar.EditingDate);
        Assert.Equal("Dentist", app.Calendar.Editor.Text);
        Assert.True(app.Calendar.Editor.Focused);
    }

    [Fact]
    public void DayClick_SaveFlow_PersistsAndColors()
    {
        var app = new CanvasApp();
        app.KeyChar('2');

        app.ClickAt(350, 250);
        Assert.Equal(new DateTime(2026, 10, 6), app.Calendar.EditingDate);
        Assert.Equal("Dentist", app.Calendar.Editor.Text);

        app.KeyChar('!');
        app.ClickAt(550, 665);

        Assert.Null(app.Calendar.EditingDate);
        Assert.Contains("Saved event for",
            FindLabel(app.Calendar.Window, "status").Text);

        var renderer = new RecordingRenderer();
        app.Paint(renderer);
        Assert.Contains(renderer.Commands.OfType<DrawTextCommand>(),
            c => c.Text == "6" && c.Rgba == 0x006400FF);
    }

    [Fact]
    public void Digit_GoesToFocusedBox_NotSceneSwitch()
    {
        var app = new CanvasApp();
        app.KeyChar('2');

        app.ClickAt(580, 90);
        app.KeyChar('2');

        Assert.Equal(AppScene.Calendar, app.Current);
        Assert.Equal("210", FindTextBox(app.Calendar.Window, "txtMonth").Text);
    }

    [Fact]
    public void Paint_CoversViewWithScene()
    {
        var app = new CanvasApp();
        var renderer = new RecordingRenderer();
        app.Paint(renderer);

        Assert.Contains(renderer.Commands.OfType<FillRectCommand>(),
            c => c.Width == 1000 && c.Height == 750 && c.Rgba == 0xF0F0F0FF);
        Assert.Contains(renderer.Commands.OfType<DrawTextCommand>(), c => c.Text == "Form1");
    }

    private static TextBox FindTextBox(Control root, string name)
    {
        foreach (Control child in root.Children)
        {
            if (child is TextBox box && box.Name == name)
                return box;
            TextBox? nested = FindTextBoxOrNull(child, name);
            if (nested is not null)
                return nested;
        }

        throw new InvalidOperationException($"No textbox {name}.");
    }

    private static TextBox? FindTextBoxOrNull(Control root, string name)
    {
        foreach (Control child in root.Children)
        {
            if (child is TextBox box && box.Name == name)
                return box;
            TextBox? nested = FindTextBoxOrNull(child, name);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
