using WfWasm.CanvasHost;
using FormsControl = System.Windows.Forms.Control;

namespace WfWasm.CanvasHost.Tests;

public sealed class GridStateTests
{
    // Shape stub: DumpGridStateCore reflects over app-loaded grid types,
    // so any control with the member shape exercises the branch.
    private sealed class FakeDataGridView : FormsControl
    {
        public List<object> Rows { get; } = new() { new(), new(), new() };
        public List<object> Columns { get; } = new() { new() };
        public object? DataSource { get; set; } = new object();
    }

    [Fact]
    public void DumpGridState_Reports_Rows_Cols_And_Source()
    {
        var grid = new FakeDataGridView { Name = "g1" };
        string output = Capture(() => WinFormsAppHost.DumpGridState(grid));
        Assert.Contains("[GRID-STATE]", output);
        Assert.Contains("rows=3", output);
        Assert.Contains("cols=1", output);
        Assert.Contains("datasource=Object", output);
    }

    [Fact]
    public void DumpGridState_Never_Throws_On_Plain_Tree()
    {
        using var form = new System.Windows.Forms.Form();
        form.Controls.Add(new FormsControl());
        string output = Capture(() => WinFormsAppHost.DumpGridState(form));
        Assert.DoesNotContain("[GRID-STATE]", output);
    }

    // Shape stubs for the first-frame visual pass (DumpGridVisualState):
    // cells/rows/columns are app-loaded Telerik types, so any member
    // shape exercises the reflection branches.
    private sealed class FakeElement
    {
        public List<object> Children { get; } = new();
        public string Bounds { get; set; } = "{X=0,Y=0,Width=10,Height=10}";
        public string Visibility { get; set; } = "Visible";
    }

    private sealed class FakeColumn
    {
        public string Name { get; set; } = "C0";
        public int Width { get; set; } = 42;
        public int MinWidth { get; set; } = 5;
        public int MaxWidth { get; set; } = 100;
        public bool IsVisible { get; set; } = true;
    }

    private sealed class FakeTemplate
    {
        public string AutoSizeColumnsMode { get; set; } = "Fill";
    }

    private sealed class FakeRadGridView : FormsControl
    {
        public FakeElement TableElement { get; } = new();
        public List<object> Columns { get; } = new() { new FakeColumn() };
        public FakeTemplate MasterTemplate { get; } = new();
    }

    [Fact]
    public void DumpGridVisualState_Reports_All_Diagnostic_Tags()
    {
        var grid = new FakeRadGridView { Name = "gv" };
        grid.TableElement.Children.Add(new FakeElement());
        string output = Capture(() => WinFormsAppHost.DumpGridVisualState(grid));
        Assert.Contains("[GRID-VIS]", output);
        Assert.Contains("[GRID-COLS]", output);
        Assert.Contains("[GRID-TREE]", output);
        Assert.Contains("[GRID-GATE]", output);
        Assert.Contains("[GRID-PAINT]", output);
    }

    [Fact]
    public void DumpGridVisualState_Never_Throws_On_Null_Shapes()
    {
        var grid = new FakeRadGridView { Name = "gv" };
        string output = Capture(() => WinFormsAppHost.DumpGridVisualState(grid));
        Assert.Contains("[GRID-VIS]", output);
    }

    private static string Capture(Action action)
    {
        var sb = new System.Text.StringBuilder();
        using var writer = new StringWriter(sb);
        TextWriter saved = Console.Out;
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(saved);
        }

        return sb.ToString();
    }
}
