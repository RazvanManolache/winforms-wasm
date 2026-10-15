using System.Globalization;
using WfWasm.Renderer;
using WfWasm.Shim;
using WfWasm.Skia;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: WfWasm.CanvasDemo <out-dir>");
    return 2;
}

string outDir = args[0];
Directory.CreateDirectory(outDir);

RenderSmokeForm(Path.Combine(outDir, "smoke-form.svg"));
RenderTaskDialog(Path.Combine(outDir, "taskdialog.svg"));
RenderCalendar(Path.Combine(outDir, "calendar.svg"));

RenderSmokeFormPng(Path.Combine(outDir, "smoke-form.png"));
RenderCalendarPng(Path.Combine(outDir, "calendar.png"));

RenderAppScene(Path.Combine(outDir, "paint.png"), AppScene.Paint, _ => { });
RenderAppScene(Path.Combine(outDir, "calculator.png"), AppScene.Calculator, app =>
{
    // 12 + 7 = 19 on screen.
    foreach (string key in new[] { "1", "2", "+", "7", "=" })
        ClickCalcKey(app, key);
});
RenderAppScene(Path.Combine(outDir, "todo.png"), AppScene.Todo, app =>
{
    app.Todo.TaskBox.Text = "1-Write A Code....";
    app.Todo.NewTasks.Items.Add("1-Write A Code....");
    app.Todo.NewTasks.Items.Add("2-Review pull request");
    app.Todo.DoneTasks.Items.Add("0-Read inbox");
});
RenderAppScene(Path.Combine(outDir, "tictactoe.png"), AppScene.TicTacToe, app =>
{
    // X takes 0 and 1, O takes 4: mid-game board.
    ClickTicCell(app, 0);
    ClickTicCell(app, 4);
    ClickTicCell(app, 1);
});
Console.WriteLine($"wrote 3 scenes + 6 PNGs to {outDir}");
return 0;

static void RenderAppScene(string path, AppScene scene, Action<CanvasApp> drive)
{
    var app = new CanvasApp();
    app.ShowScene(scene);
    drive(app);
    using var skia = new SkiaCanvasRenderer();
    skia.BeginFrame(CanvasApp.ViewWidth, CanvasApp.ViewHeight);
    app.Paint(skia);
    skia.EndFrame();
    File.WriteAllBytes(path, skia.ToPng());
}

static void ClickCalcKey(CanvasApp app, string key)
{
    string[,] keys =
    {
        { "%", "DEL", "C", "/" },
        { "7", "8", "9", "*" },
        { "4", "5", "6", "-" },
        { "1", "2", "3", "+" },
        { "00", "0", ".", "=" },
    };
    for (int row = 0; row < 5; row++)
        for (int col = 0; col < 4; col++)
            if (keys[row, col] == key)
                app.ClickAt(120 + 1 + 12 + (col * 68) + 31, 45 + 29 + 56 + (row * 52) + 23);
}

static void ClickTicCell(CanvasApp app, int index)
{
    int row = index / 3;
    int col = index % 3;
    app.ClickAt(60 + 1 + 100 + (col * 100) + 46, 30 + 29 + 72 + (row * 100) + 46);
}

static void RenderSmokeForm(string path)
{
    var form = new Form("form") { Width = 800, Height = 450, Text = "Form1" };
    form.AddChild(new Label("label1")
    {
        X = 180, Y = 169, Width = 300, Height = 50,
        Text = "Hello .NET", FontCss = "36px sans-serif",
    });
    form.AddChild(new Button("ok") { X = 690, Y = 380, Width = 90, Height = 30, Text = "OK", IsDefault = true });

    var svg = new SvgRenderer();
    svg.BeginFrame(800, 450);
    form.Render(svg);
    svg.EndFrame();
    File.WriteAllText(path, svg.GetSvg());
}

static void RenderTaskDialog(string path)
{
    var page = new DialogPageModel
    {
        Heading = "Save changes?",
        Text = "Do you want to save changes before closing?",
    };
    page.Buttons.Add(new DialogButtonModel("save", "Save") { IsDefault = true });
    page.Buttons.Add(new DialogButtonModel("dontsave", "Don't Save") { AllowClose = true });
    page.Buttons.Add(new DialogButtonModel("cancel", "Cancel"));

    var svg = new SvgRenderer();
    DialogOverlayPainter.Paint(svg, page, "Task dialog");
    File.WriteAllText(path, svg.GetSvg());
}

static void RenderSmokeFormPng(string path)
{
    var form = new Form("form") { Width = 800, Height = 450, Text = "Form1" };
    form.AddChild(new Label("label1")
    {
        X = 180, Y = 169, Width = 300, Height = 50,
        Text = "Hello .NET", FontCss = "36px sans-serif",
    });
    form.AddChild(new Button("ok") { X = 690, Y = 380, Width = 90, Height = 30, Text = "OK", IsDefault = true });

    using var skia = new SkiaCanvasRenderer();
    skia.BeginFrame(800, 450);
    form.Render(skia);
    skia.EndFrame();
    File.WriteAllBytes(path, skia.ToPng());
}

static void RenderCalendarPng(string path)
{
    var grid = BuildCalendarGrid();
    var form = new Form("form") { Width = 760, Height = 660, Text = "Calendar" };
    AddCalendarNav(form, grid);

    using var skia = new SkiaCanvasRenderer();
    skia.BeginFrame(760, 660);
    form.Render(skia);
    skia.EndFrame();
    File.WriteAllBytes(path, skia.ToPng());
}

static void RenderCalendar(string path)
{
    var grid = BuildCalendarGrid();
    var form = new Form("form") { Width = 760, Height = 660, Text = "Calendar" };
    AddCalendarNav(form, grid);

    var svg = new SvgRenderer();
    svg.BeginFrame(760, 660);
    form.Render(svg);
    svg.EndFrame();
    File.WriteAllText(path, svg.GetSvg());
}

static MonthCalendar BuildCalendarGrid()
{
    var grid = new MonthCalendar("grid");
    grid.GoTo("10", "2026");
    grid.Today = new DateTime(2026, 10, 5);
    grid.Events.Set(new DateTime(2026, 10, 6), "Dentist");
    grid.Events.Set(new DateTime(2026, 10, 31), "Halloween");
    return grid;
}

static void AddCalendarNav(Form form, MonthCalendar grid)
{
    string monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(grid.Month);
    form.AddChild(new Label("lbdate") { X = 8, Y = 8, Width = 300, Height = 26, Text = $"{monthName} {grid.Year}" });
    form.AddChild(new Button("prev") { X = 320, Y = 6, Width = 60, Height = 26, Text = "<" });
    form.AddChild(new Button("next") { X = 386, Y = 6, Width = 60, Height = 26, Text = ">" });
    form.AddChild(new TextBox("txtMonth") { X = 452, Y = 6, Width = 60, Height = 26, Text = "10" });
    form.AddChild(new TextBox("txtYear") { X = 518, Y = 6, Width = 70, Height = 26, Text = "2026" });
    form.AddChild(new Button("go") { X = 594, Y = 6, Width = 60, Height = 26, Text = "Go", IsDefault = true });
    grid.X = 8;
    grid.Y = 44;
    form.AddChild(grid);
}
