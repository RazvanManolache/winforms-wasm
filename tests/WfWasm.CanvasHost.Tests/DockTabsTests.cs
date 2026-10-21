using WfWasm.CanvasHost;
using FormsControl = System.Windows.Forms.Control;

namespace WfWasm.CanvasHost.Tests;

public sealed class DockTabsTests
{
    // Shape stub: WalkDockTabs reflects over app-loaded dock types, so any
    // control with the member shape exercises the branch.
    private sealed class FakeToolWindow : FormsControl
    {
    }

    [Fact]
    public void DumpDockTabText_Reports_Window_Text()
    {
        using var form = new System.Windows.Forms.Form();
        var win = new FakeToolWindow { Name = "projectsToolWindow", Text = "Projects" };
        form.Controls.Add(win);
        string output = Capture(() => WinFormsAppHost.DumpDockTabText(form));
        Assert.Contains("[DOCK-TABS]", output);
        Assert.Contains("Projects", output);
    }

    [Fact]
    public void DumpDockTabText_Never_Throws_On_Plain_Tree()
    {
        using var form = new System.Windows.Forms.Form();
        form.Controls.Add(new FormsControl());
        string output = Capture(() => WinFormsAppHost.DumpDockTabText(form));
        Assert.Contains("[DOCK-TABS]", output);
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
