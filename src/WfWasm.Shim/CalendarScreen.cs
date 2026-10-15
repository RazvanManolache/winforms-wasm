using System.Globalization;

namespace WfWasm.Shim;

/// <summary>
/// Interactive calendar screen: month grid plus navigation row plus an
/// inline event editor (a day click opens it focused; Save persists to the
/// grid's event store, Cancel closes). All state is plain models; painting
/// is canvas commands via the control tree.
/// </summary>
public sealed class CalendarScreen
{
    private readonly Form _window;
    private readonly Label _monthLabel;
    private readonly TextBox _txtMonth;
    private readonly TextBox _txtYear;
    private readonly Label _prompt;
    private readonly TextBox _editor;
    private readonly Button _save;
    private readonly Button _cancel;
    private readonly Label _status;

    private DateTime? _editingDate;

    public CalendarScreen(int x, int y)
    {
        Grid = new MonthCalendar("grid");
        Grid.GoTo("10", "2026");
        Grid.Today = new DateTime(2026, 10, 5);
        Grid.Events.Set(new DateTime(2026, 10, 6), "Dentist");
        Grid.Events.Set(new DateTime(2026, 10, 31), "Halloween");
        Grid.DayClicked += (_, date) => OpenEditor(date);

        _window = new Form("window") { X = x, Y = y, Width = 760, Height = 660, Text = "Calendar" };
        _monthLabel = new Label("lbdate") { X = 8, Y = 8, Width = 300, Height = 26, Text = "" };
        var prev = new Button("prev") { X = 320, Y = 6, Width = 60, Height = 26, Text = "<" };
        prev.Click += (_, _) => { Grid.MovePrevious(); Refresh(); };
        var next = new Button("next") { X = 386, Y = 6, Width = 60, Height = 26, Text = ">" };
        next.Click += (_, _) => { Grid.MoveNext(); Refresh(); };
        _txtMonth = new TextBox("txtMonth") { X = 452, Y = 6, Width = 60, Height = 26, Text = "10" };
        _txtYear = new TextBox("txtYear") { X = 518, Y = 6, Width = 70, Height = 26, Text = "2026" };
        var go = new Button("go") { X = 594, Y = 6, Width = 60, Height = 26, Text = "Go", IsDefault = true };
        go.Click += (_, _) =>
        {
            if (Grid.GoTo(_txtMonth.Text, _txtYear.Text))
                Refresh();
            else
                SetStatus("Enter a valid month (1-12 or name) and year.");
        };

        _prompt = new Label("prompt") { X = 8, Y = 556, Width = 400, Height = 24, Text = "" };
        _editor = new TextBox("editor") { X = 8, Y = 582, Width = 400, Height = 26, Text = "" };
        _save = new Button("save") { X = 420, Y = 582, Width = 90, Height = 26, Text = "Save", IsDefault = true };
        _save.Click += (_, _) => SaveEditor();
        _cancel = new Button("cancel") { X = 516, Y = 582, Width = 90, Height = 26, Text = "Cancel" };
        _cancel.Click += (_, _) => CloseEditor();
        _status = new Label("status") { X = 8, Y = 614, Width = 600, Height = 24, Text = "Ready." };

        Grid.X = 8;
        Grid.Y = 44;
        _window.AddChild(_monthLabel);
        _window.AddChild(prev);
        _window.AddChild(next);
        _window.AddChild(_txtMonth);
        _window.AddChild(_txtYear);
        _window.AddChild(go);
        _window.AddChild(Grid);
        _window.AddChild(_prompt);
        _window.AddChild(_editor);
        _window.AddChild(_save);
        _window.AddChild(_cancel);
        _window.AddChild(_status);
        SetEditorVisible(false);
        Refresh();
    }

    public Form Window => _window;
    public MonthCalendar Grid { get; }
    public TextBox Editor => _editor;
    public Label Status => _status;
    public DateTime? EditingDate => _editingDate;

    private void OpenEditor(DateTime date)
    {
        _editingDate = date;
        _prompt.Text = $"Event for {date.ToString("d", CultureInfo.InvariantCulture)}:";
        _editor.Text = Grid.Events.TryGet(date, out string? existing) ? existing ?? "" : "";
        _editor.MoveCaretTo(_editor.Text.Length);
        _editor.Focused = true;
        SetEditorVisible(true);
        SetStatus($"Editing {date.ToString("d", CultureInfo.InvariantCulture)}.");
    }

    private void SaveEditor()
    {
        if (_editingDate is null)
            return;
        if (string.IsNullOrWhiteSpace(_editor.Text))
        {
            SetStatus("Enter an event description.");
            return;
        }

        Grid.Events.Set(_editingDate.Value, _editor.Text.Trim());
        SetStatus($"Saved event for {_editingDate.Value.ToString("d", CultureInfo.InvariantCulture)}.");
        CloseEditor();
    }

    private void CloseEditor()
    {
        _editingDate = null;
        _editor.Focused = false;
        SetEditorVisible(false);
    }

    private void SetEditorVisible(bool visible)
    {
        _prompt.Visible = visible;
        _editor.Visible = visible;
        _save.Visible = visible;
        _cancel.Visible = visible;
    }

    private void Refresh()
    {
        string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Grid.Month);
        _monthLabel.Text = $"{monthName} {Grid.Year}";
    }

    private void SetStatus(string text) => _status.Text = text;
}
