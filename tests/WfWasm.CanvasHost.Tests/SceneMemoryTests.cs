using System.Reflection;
using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class SceneMemoryTests
{
    private static void LoadApp(string file, string simpleName)
    {
        // Same regenerable-data coupling as the other host tests: the
        // rewritten app must exist in E:\tmp; bridge the assembly name
        // like the browser loader does by file.
        string app = @$"E:\tmp\rewritten-small\{file}";
        Assert.True(File.Exists(app), $"regenerate via E:/tmp/rewrite-small.ps1: {app}");
        var asm = Assembly.LoadFrom(app);
        ResolveEventHandler? resolver = (_, args) =>
            args.Name.Split(',')[0].Trim() == simpleName ? asm : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
    }

    [Fact]
    public void Selecting_New_Scene_Disposes_Previous_Form()
    {
        // Only the selected app stays alive: switching must dispose the
        // old form so control trees cannot accumulate in memory.
        LoadApp("Calculator.dll", "Calculator");
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var smoke = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(smoke);
        Assert.False(smoke.IsDisposed);

        host.SelectScene(AppScene.Calculator);

        Assert.True(smoke.IsDisposed);
        var calculator = host.GetOrCreateForm(AppScene.Calculator);
        Assert.NotNull(calculator);
        Assert.False(calculator.IsDisposed);
    }

    [Fact]
    public void Reselecting_Same_Scene_Keeps_Form_Alive()
    {
        using var host = new WinFormsAppHost();
        host.SelectScene(AppScene.Smoke);
        var first = host.GetOrCreateForm(AppScene.Smoke);
        Assert.NotNull(first);

        host.SelectScene(AppScene.Smoke);

        Assert.False(first.IsDisposed);
        Assert.Same(first, host.GetOrCreateForm(AppScene.Smoke));
    }
}
