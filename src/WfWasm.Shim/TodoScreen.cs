namespace WfWasm.Shim;

/// <summary>
/// Todo list mirror (cf. kasra10/simple-csharp-todolist
/// ToDoListDashBoard_FR): task entry plus New/Done lists with Add, Remove,
/// Clear, Done and Not-Done moves, and the original's validation dialog.
/// Deviations: two single-select list boxes stand in for the ListViews
/// (no multi-select in the shim); the GitHub/Website picture links are
/// omitted (no process launch on canvas); the "Pleas" typo is the app's own.
/// </summary>
public sealed class TodoScreen
{
    private readonly Action<string, string> _showMessage;

    public Form Window { get; }

    public TextBox TaskBox { get; }

    public ListBox NewTasks { get; }

    public ListBox DoneTasks { get; }

    public TodoScreen(int x, int y, Action<string, string>? showMessage = null)
    {
        _showMessage = showMessage ?? ((_, _) => { });
        Window = new Form("todo") { X = x, Y = y, Width = 640, Height = 560, Text = "DashBoard" };

        Window.AddChild(new Label("title1") { X = 12, Y = 8, Width = 120, Height = 30, Text = "To Do" });
        Window.AddChild(new Label("title2") { X = 132, Y = 8, Width = 200, Height = 30, Text = "List (Daily)" });
        Window.AddChild(new Label("taskLbl")
        {
            X = 12, Y = 44, Width = 300, Height = 22, Text = "Write Your Task Today:",
        });
        TaskBox = new TextBox("taskBox")
        {
            X = 12, Y = 68, Width = 400, Height = 26, Text = "1-Write A Code....",
        };
        Window.AddChild(TaskBox);

        AddButton("add", "Add Task", 420, 68, AddTask);
        AddButton("remove", "Remove Task", 516, 68, RemoveTask);
        AddButton("clear", "Clear List", 420, 102, ClearList);
        AddButton("done", "Done", 420, 136, MoveDone);
        AddButton("notdone", "Not Done!", 516, 136, MoveNotDone);

        Window.AddChild(new Label("newLbl") { X = 12, Y = 104, Width = 200, Height = 22, Text = "New Tasks:" });
        NewTasks = new ListBox("newTasks") { X = 12, Y = 128, Width = 390, Height = 190 };
        Window.AddChild(NewTasks);

        Window.AddChild(new Label("doneLbl") { X = 12, Y = 326, Width = 200, Height = 22, Text = "Done Tasks:" });
        DoneTasks = new ListBox("doneTasks") { X = 12, Y = 350, Width = 390, Height = 170 };
        Window.AddChild(DoneTasks);
    }

    private void AddButton(string name, string text, int x, int y, Action handler)
    {
        var button = new Button(name) { X = x, Y = y, Width = 100, Height = 28, Text = text };
        button.Click += (_, _) => handler();
        Window.AddChild(button);
    }

    private void AddTask()
    {
        if (!string.IsNullOrWhiteSpace(TaskBox.Text))
        {
            NewTasks.Items.Add(TaskBox.Text);
            TaskBox.Text = "";
        }
        else
        {
            _showMessage("Pleas Write A Task. Error: ", "Error");
        }
    }

    private void ClearList()
    {
        NewTasks.Items.Clear();
        TaskBox.Text = "";
    }

    private void MoveDone()
    {
        if (NewTasks.SelectedItem is string item)
        {
            DoneTasks.Items.Add(item);
            NewTasks.Items.Remove(item);
        }
    }

    private void MoveNotDone()
    {
        if (DoneTasks.SelectedItem is string item)
        {
            NewTasks.Items.Add(item);
            DoneTasks.Items.Remove(item);
        }
    }

    private void RemoveTask()
    {
        if (NewTasks.SelectedItem is string newItem)
            NewTasks.Items.Remove(newItem);
        if (DoneTasks.SelectedItem is string doneItem)
            DoneTasks.Items.Remove(doneItem);
    }
}
