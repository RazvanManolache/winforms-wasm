using WfWasm.Shim;

/// <summary>Real mirrored apps driven through CanvasApp like a user.</summary>
public class RealAppsTests
{
    private static Control Find(Control root, string name)
    {
        foreach (Control child in root.Children)
        {
            if (child.Name == name)
                return child;
            try
            {
                return Find(child, name);
            }
            catch (KeyNotFoundException)
            {
            }
        }

        throw new KeyNotFoundException(name);
    }

    private static (int X, int Y) AbsCenter(Control root, string name)
    {
        Control target = Find(root, name);
        int x = target.X;
        int y = target.Y;
        for (Control? p = target.Parent; p is not null; p = p.Parent)
        {
            x += p.X;
            y += p.Y;
            if (p is Form form)
            {
                x += Form.Border;
                y += form.CaptionHeight + Form.Border;
            }
        }

        return (x + (target.Width / 2), y + (target.Height / 2));
    }

    private static void ClickNamed(CanvasApp app, Control root, string name)
    {
        var (x, y) = AbsCenter(root, name);
        app.ClickAt(x, y);
    }

    private static void ClickModalOk(CanvasApp app) =>
        ClickNamed(app, app.ModalRoot!, "mbOk");

    private static void ClickFirstItem(CanvasApp app, Control root, string name)
    {
        Control target = Find(root, name);
        int x = target.X;
        int y = target.Y;
        for (Control? p = target.Parent; p is not null; p = p.Parent)
        {
            x += p.X;
            y += p.Y;
            if (p is Form form)
            {
                x += Form.Border;
                y += form.CaptionHeight + Form.Border;
            }
        }

        // First row center (below the 2px sunken inset, 15px rows).
        app.ClickAt(x + 10, y + 2 + 7);
    }

    [Fact]
    public void Modal_Captures_Input_Until_Dismissed()
    {
        var app = new CanvasApp();
        app.ShowMessage("Boom", "Error");
        Assert.True(app.HasModal);

        // Clicks outside the dialog do not dismiss or leak through.
        app.ClickAt(50, 50);
        Assert.True(app.HasModal);
        Assert.Equal(AppScene.Smoke, app.Current);

        // Scene digits are swallowed while modal.
        app.KeyChar('2');
        Assert.Equal(AppScene.Smoke, app.Current);
        Assert.True(app.HasModal);

        ClickModalOk(app);
        Assert.False(app.HasModal);
    }

    [Fact]
    public void Modal_Dismisses_On_Enter_And_Escape()
    {
        var app = new CanvasApp();
        app.ShowMessage("Boom", "Error");
        app.KeyChar('\r');
        Assert.False(app.HasModal);

        app.ShowMessage("Boom", "Error");
        app.KeyEscape();
        Assert.False(app.HasModal);
    }

    private static void CalcPress(CanvasApp app, string key) =>
        ClickNamed(app, app.Calculator.Window, "btn" + key);

    [Fact]
    public void Calculator_Adds_Divides_And_Handles_Zero()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.Calculator);
        Assert.Equal("0", app.Calculator.Display.Text);

        foreach (string k in new[] { "2", "+", "3", "=" })
            CalcPress(app, k);
        Assert.Equal("5", app.Calculator.Display.Text);

        CalcPress(app, "C");
        Assert.Equal("0", app.Calculator.Display.Text);

        foreach (string k in new[] { "8", "/", "0", "=" })
            CalcPress(app, k);
        Assert.True(app.HasModal);
        Assert.Contains("Cannot divide by zero", app.ModalRoot!.Children
            .OfType<Label>().First().Text);
        Assert.Equal("0", app.Calculator.Display.Text);
        ClickModalOk(app);
        Assert.False(app.HasModal);
    }

    [Fact]
    public void Calculator_Percent_Delete_Dot_Rules()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.Calculator);

        // 200 + 10 % => 20 (plus-style percent).
        foreach (string k in new[] { "2", "0", "0", "+", "1", "0", "%" })
            CalcPress(app, k);
        Assert.Equal("20", app.Calculator.Display.Text);

        CalcPress(app, "C");
        foreach (string k in new[] { "1", "2", "DEL" })
            CalcPress(app, k);
        Assert.Equal("1", app.Calculator.Display.Text);

        CalcPress(app, "C");
        foreach (string k in new[] { ".", "5" })
            CalcPress(app, k);
        Assert.Equal("0.5", app.Calculator.Display.Text);
    }

    [Fact]
    public void Todo_Add_Move_Remove_Clear_Flow()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.Todo);

        // Start clean, type a task, add it.
        ClickNamed(app, app.Todo.Window, "clear");
        ClickNamed(app, app.Todo.Window, "taskBox");
        foreach (char c in "Buy milk")
            app.KeyChar(c);
        ClickNamed(app, app.Todo.Window, "add");
        Assert.Equal(new[] { "Buy milk" }, app.Todo.NewTasks.Items.Cast<object>().Select(i => i?.ToString()));
        Assert.Empty(app.Todo.DoneTasks.Items);

        // Select it in the list, move to done and back.
        ClickFirstItem(app, app.Todo.Window, "newTasks");
        Assert.Equal("Buy milk", app.Todo.NewTasks.SelectedItem);
        ClickNamed(app, app.Todo.Window, "done");
        Assert.Empty(app.Todo.NewTasks.Items);
        Assert.Equal(new[] { "Buy milk" }, app.Todo.DoneTasks.Items.Cast<object>().Select(i => i?.ToString()));
        ClickFirstItem(app, app.Todo.Window, "doneTasks");
        ClickNamed(app, app.Todo.Window, "notdone");
        Assert.Equal(new[] { "Buy milk" }, app.Todo.NewTasks.Items.Cast<object>().Select(i => i?.ToString()));
        Assert.Empty(app.Todo.DoneTasks.Items);

        // Remove selected, then clear.
        ClickFirstItem(app, app.Todo.Window, "newTasks");
        ClickNamed(app, app.Todo.Window, "remove");
        Assert.Empty(app.Todo.NewTasks.Items);
    }

    [Fact]
    public void Todo_Empty_Add_Shows_Original_Error()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.Todo);
        ClickNamed(app, app.Todo.Window, "clear");
        ClickNamed(app, app.Todo.Window, "taskBox");
        ClickNamed(app, app.Todo.Window, "add");
        Assert.True(app.HasModal);
        Assert.Contains("Pleas Write A Task. Error: ", app.ModalRoot!.Children
            .OfType<Label>().First().Text);
        ClickModalOk(app);
        Assert.False(app.HasModal);
    }

    private static void TicCell(CanvasApp app, int index) =>
        ClickNamed(app, app.TicTacToe.Window, "cell" + index);

    [Fact]
    public void TicTacToe_X_Wins_Top_Row()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.TicTacToe);
        Assert.Equal("Player 1", app.TicTacToe.TurnLabel.Text);

        TicCell(app, 0); // X
        Assert.Equal("Player 2", app.TicTacToe.TurnLabel.Text);
        TicCell(app, 3); // O
        TicCell(app, 1); // X
        TicCell(app, 4); // O
        TicCell(app, 2); // X wins

        Assert.Equal("Game Over", app.TicTacToe.TurnLabel.Text);
        Assert.Equal("Player 1", app.TicTacToe.WinnerLabel.Text);
        Assert.True(app.HasModal);
        ClickModalOk(app);
        Assert.Equal("1 time(s)", app.TicTacToe.Player1Stats.Text);
        Assert.Equal("0 time(s)", app.TicTacToe.Player2Stats.Text);
    }

    [Fact]
    public void TicTacToe_Occupied_Cell_Warns()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.TicTacToe);
        TicCell(app, 0);
        TicCell(app, 0);
        Assert.True(app.HasModal);
        Assert.Contains("Wrong Choice", app.ModalRoot!.Children
            .OfType<Label>().First().Text);
        ClickModalOk(app);
        Assert.Equal("Player 2", app.TicTacToe.TurnLabel.Text);
    }

    [Fact]
    public void TicTacToe_Full_Board_Draws()
    {
        var app = new CanvasApp();
        app.ShowScene(AppScene.TicTacToe);
        // X:0,2,3,7,8 O:1,4,5,6 — no three in a row.
        foreach (int i in new[] { 0, 1, 2, 4, 3, 5, 7, 6, 8 })
        {
            TicCell(app, i);
            if (app.HasModal)
                ClickModalOk(app);
        }

        Assert.Equal("Game Over", app.TicTacToe.TurnLabel.Text);
        Assert.Equal("Draw", app.TicTacToe.WinnerLabel.Text);
    }
}
