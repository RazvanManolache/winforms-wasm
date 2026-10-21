using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;
using FormsContainerControl = System.Windows.Forms.ContainerControl;
using FormsForm = System.Windows.Forms.Form;

/// <summary>
/// [Scene] Scale console-line contract (eval 2026-10-17 pass 4): every
/// scene selection logs its autoscale mode, design/current dimensions and
/// resolved font, e.g.
///   [Scene] Scale: mode=Font dims={Width=7, Height=15}
///     current={Width=7, Height=15} font=Segoe UI 9.00pt
/// The browser sweep reads this line to verify per-app default fonts
/// (TFM-aware: netfx MSS 8.25, modern Segoe 9) and autoscale behavior.
/// </summary>
public sealed class EvalScaleTests
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

    private static string SelectSceneLogged(AppScene scene)
    {
        var log = new System.IO.StringWriter();
        TextWriter saved = Console.Out;
        Console.SetOut(log);
        try
        {
            using var host = new WinFormsAppHost();
            host.SelectScene(scene);
        }
        finally
        {
            Console.SetOut(saved);
        }
        return log.ToString();
    }

    private static string ScaleLine(string log)
    {
        string? line = log.Split('\n')
            .FirstOrDefault(l => l.Contains("[Scene] Scale:"));
        Assert.False(string.IsNullOrEmpty(line), "SelectScene must log [Scene] Scale:");
        return line!;
    }

    [Fact]
    public void Smoke_Scale_Line_Reports_Inherit_With_Zero_Dims()
    {
        // Browser: mode=Inherit dims={Width=0, Height=0} (plain form, no autoscale).
        string line = ScaleLine(SelectSceneLogged(AppScene.Smoke));
        Assert.Contains("mode=Inherit", line);
        Assert.Contains("dims={Width=0, Height=0}", line);
        Assert.Contains("current={Width=0, Height=0}", line);
        Assert.Contains("font=", line);
        Assert.Contains("pt", line);
    }

    [Fact]
    public void Calculator_Scale_Line_Reports_Font_Autoscale()
    {
        // Browser: mode=Font dims={Width=7, Height=15} font=Segoe UI 9.00pt.
        // Headless the fork default font may differ, so pin the mode and
        // non-zero dims rather than exact pixels.
        var app = LoadSmallApp("Calculator.dll", "Calculator");
        ResolveEventHandler? resolver = SmallResolver(app);
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            string line = ScaleLine(SelectSceneLogged(AppScene.Calculator));
            Assert.Contains("mode=Font", line);
            Assert.Contains("dims={Width=", line);
            Assert.DoesNotContain("dims={Width=0, Height=0}", line);
            Assert.Contains("font=", line);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    [Fact]
    public void Scale_Line_Dims_Match_Form_AutoscaleDimensions()
    {
        // The logged dims/current must equal the live form state (no drift
        // between the diagnostic line and the object it describes).
        var app = LoadSmallApp("Calculator.dll", "Calculator");
        ResolveEventHandler? resolver = SmallResolver(app);
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            var log = new System.IO.StringWriter();
            TextWriter saved = Console.Out;
            FormsForm? form;
            Console.SetOut(log);
            try
            {
                using var host = new WinFormsAppHost();
                host.SelectScene(AppScene.Calculator);
                form = host.GetOrCreateForm(AppScene.Calculator);
            }
            finally
            {
                Console.SetOut(saved);
            }
            Assert.NotNull(form);
            var cc = Assert.IsAssignableFrom<FormsContainerControl>(form);
            string line = ScaleLine(log.ToString());
            Assert.Contains($"mode={cc.AutoScaleMode}", line);
            Assert.Contains($"dims={cc.AutoScaleDimensions}", line);
            Assert.Contains($"current={cc.CurrentAutoScaleDimensions}", line);
            Assert.Contains($"font={cc.Font?.Name}", line);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }
}
