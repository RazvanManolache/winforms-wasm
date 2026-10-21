using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Renderer;
using WfWasm.Shim;
using FormsButton = System.Windows.Forms.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsForm = System.Windows.Forms.Form;
using FormsLabel = System.Windows.Forms.Label;
using FormsTextBox = System.Windows.Forms.TextBox;

/// <summary>
/// Browser-verified interaction parity (eval 2026-10-17): each test mirrors
/// a scripted browser run against the live app (/app/{slug}) that passed
/// functionally, locking the same behavior in at the host level.
/// </summary>
public sealed class EvalInteractionTests
{
    private static readonly string SmallDir = TestPaths.Repo(@".artifacts\runtime");

    private static Assembly LoadSmallApp(string file, string expectName)
    {
        string path = Path.Combine(SmallDir, file);
        Assert.True(File.Exists(path), $"regenerate via E:/tmp/rewrite-small.ps1: {path}");
        var app = Assembly.LoadFrom(path);
        Assert.Equal(expectName, app.GetName().Name);
        return app;
    }

    private static ResolveEventHandler SmallResolver(Assembly app) => (_, args) =>
    {
        string want = args.Name.Split(',')[0].Trim();
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == want);
        if (loaded is not null) return loaded;
        string stem = Path.GetFileNameWithoutExtension(app.Location);
        if (string.Equals(want, stem, StringComparison.OrdinalIgnoreCase)) return app;
        string dll = Path.Combine(SmallDir, want + ".dll");
        return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
    };

    private static IEnumerable<FormsControl> Descendants(FormsControl root)
    {
        foreach (FormsControl c in root.Controls)
        {
            yield return c;
            foreach (var n in Descendants(c)) yield return n;
        }
    }

    private static (int X, int Y) CenterOn(FormsForm form, FormsControl control)
    {
        int x = form.Location.X, y = form.Location.Y + 30; // host chrome, cf. TictactoePaintTests
        for (FormsControl? c = control; c is not null && c is not FormsForm; c = c.Parent)
        {
            x += c.Left;
            y += c.Top;
        }
        return (x + (control.Width / 2), y + (control.Height / 2));
    }

    private static void Click(WinFormsAppHost host, FormsForm form, FormsControl control)
    {
        var (x, y) = CenterOn(form, control);
        host.OnClick(x, y);
    }

    private static bool PaintContains(WinFormsAppHost host, string text)
    {
        var recorder = new RecordingRenderer();
        host.Paint(recorder);
        return recorder.Commands.OfType<DrawTextCommand>().Any(c => c.Text.Contains(text));
    }

    [Fact]
    public void Smoke_Click_Increments_Counter()
    {
        // Browser: click -> label "Clicks: 1 (Success!)".
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke)!;
        Assert.NotNull(form);
        FormsButton button = Descendants(form).OfType<FormsButton>()
            .First(b => b.Text == "Click Me");
        Click(host, form, button);
        FormsLabel label = Descendants(form).OfType<FormsLabel>()
            .First(l => l.Text.StartsWith("Clicks:"));
        Assert.Contains("Clicks: 1", label.Text);
        Assert.True(PaintContains(host, "Clicks: 1"));
    }

    [Fact]
    public void Calculator_Seven_Plus_Eight_Shows_15()
    {
        // Browser: 7 + 8 = -> display "15".
        var app = LoadSmallApp("Calculator.dll", "Calculator");
        ResolveEventHandler? resolver = SmallResolver(app);
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.Calculator);
            FormsForm form = host.GetOrCreateForm(AppScene.Calculator)!;
            Assert.NotNull(form);
            foreach (string key in new[] { "7", "+", "8", "=" })
            {
                FormsButton button = Descendants(form).OfType<FormsButton>()
                    .First(b => b.Text == key);
                Click(host, form, button);
            }
            Assert.True(PaintContains(host, "15"));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    [Fact]
    public void Todo_Add_Task_Appends_Row()
    {
        // Browser: type "eval task" + Add Task -> row appears in New Tasks.
        var app = LoadSmallApp("SimpleDailyToDoList.dll", "SimpleDailyToDoList");
        ResolveEventHandler? resolver = SmallResolver(app);
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.Todo);
            FormsForm form = host.GetOrCreateForm(AppScene.Todo)!;
            Assert.NotNull(form);
            FormsTextBox box = Descendants(form).OfType<FormsTextBox>()
                .First(t => t.Name == "Task_TB");
            box.Text = "eval task";
            FormsButton add = Descendants(form).OfType<FormsButton>()
                .First(b => b.Name == "Add_BTN");
            Click(host, form, add);
            Assert.True(PaintContains(host, "eval task"));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    [Fact]
    public void HubTile_Handler_Error_Is_Contained_And_Host_Stays_Alive()
    {
        // Browser: DemoAppsHub tile click ran bugTrackerTile_Click ->
        // RunProcess -> RadMessageBox.Show, which threw TypeLoadException
        // (System.Media.SystemSounds); the page survived (2 repaint frames,
        // no pageerror) and logged the mouse-event error. Stock buttons
        // contain the same failure under the "Button click error" tag.
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        FormsForm form = host.GetOrCreateForm(AppScene.Smoke)!;
        Assert.NotNull(form);
        FormsButton button = Descendants(form).OfType<FormsButton>()
            .First(b => b.Text == "Click Me");
        button.Click += (_, _) => throw new InvalidOperationException("Could not locate executable!");
        var log = new System.IO.StringWriter();
        TextWriter saved = Console.Out;
        Console.SetOut(log);
        try
        {
            var (x, y) = CenterOn(form, button);
            var ex = Record.Exception(() => { host.OnMouseDown(x, y); host.OnMouseUp(x, y); });
            Assert.Null(ex);
        }
        finally
        {
            Console.SetOut(saved);
        }
        Assert.Contains("Button click error", log.ToString());
        Assert.Contains("Could not locate executable", log.ToString());
        // Host still routes input after the contained failure.
        FormsLabel label = Descendants(form).OfType<FormsLabel>()
            .First(l => l.Text.StartsWith("Clicks:"));
        Assert.Contains("Clicks: 1", label.Text);
    }

    [Fact]
    public void Calendar_Day_Click_Opens_Add_Event_Dialog()
    {
        // Browser: day-cell click -> modal "Add Event" dialog for that date.
        var app = LoadSmallApp("Calendar.dll", "grp2Calendar");
        ResolveEventHandler? resolver = SmallResolver(app);
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(AppScene.Calendar);
            FormsForm form = host.GetOrCreateForm(AppScene.Calendar)!;
            Assert.NotNull(form);
            FormsControl container = Descendants(form).Append(form)
                .First(c => c.Name == "daycontainer");
            Assert.True(container.Controls.Count > 20);
            FormsControl day = container.Controls
                .Cast<FormsControl>()
                .First(c => c.Width > 50 && c.Height > 30
                    && c.GetType().Name != "UserControlBlank");
            // Click the panel corner, not the center: a centered child
            // label would swallow the click (WinForms routes to the
            // deepest child; the day handler sits on the panel).
            var (cx, cy) = CenterOn(form, day);
            host.OnClick(cx - (day.Width / 2) + 8, cy - (day.Height / 2) + 8);
            Assert.True(PaintContains(host, "Add Event"));
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }
}
