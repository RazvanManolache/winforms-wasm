/// <summary>
/// Right-rail contract (eval 2026-10-17 pass 3): the title bar moved to a
/// right-side vertical icon rail; the dashboard link is a rail icon.
/// These tests lock the SOURCE side (markup order + CSS rules). They do
/// not cover deploy sync: pass 3 caught the server serving a stale
/// app.css (rail rendered bottom-left), which needs a wwwroot sync gate.
/// </summary>
public sealed class EvalRailTests
{
    private static string RepoFile(params string[] parts)
    {
        string? dir = Path.GetDirectoryName(SourceFile());
        return Path.GetFullPath(Path.Combine(new[] { "../../" }.Concat(parts).ToArray()), dir!);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void Rail_Renders_Right_Of_Main_With_Dashboard_Icon()
    {
        string text = File.ReadAllText(RepoFile("src", "WfWasm.CanvasHost", "Layout", "MainLayout.razor"));
        int mainEnd = text.IndexOf("</main>", StringComparison.Ordinal);
        int rail = text.IndexOf("app-rail", StringComparison.Ordinal);
        Assert.True(mainEnd >= 0 && rail > mainEnd, "rail aside must follow </main> (right column)");
        Assert.Contains("href=\"/dashboard\"", text);
        Assert.Contains("Evaluation dashboard", text);
        foreach (string action in new[] { "App Studio home", "Toggle app library", "Compare desktop", "Reload" })
            Assert.Contains(action, text);
    }

    [Fact]
    public void Rail_Actions_All_Have_Accessible_Names()
    {
        // The rail is icons-only (pass 4 serves the synced CSS: 60px right
        // column, glyphs only). Every action must carry a tooltip + screen-
        // reader name so the icon is not cryptic.
        string text = File.ReadAllText(RepoFile("src", "WfWasm.CanvasHost", "Layout", "MainLayout.razor"));
        int aside = text.IndexOf("<aside class=\"app-rail\"", StringComparison.Ordinal);
        Assert.True(aside >= 0, "rail aside block must exist");
        int end = text.IndexOf("</aside>", aside, StringComparison.Ordinal);
        Assert.True(end > aside, "rail aside block must close");
        string rail = text.Substring(aside, end - aside);
        Assert.Contains("aria-label=\"Workspace actions\"", rail);
        foreach (string action in new[] { "App Studio home", "Toggle app menu", "Compare desktop", "Reload", "Evaluation dashboard" })
            Assert.Contains($"aria-label=\"{action}\"", rail);
        Assert.Equal(6, rail.Split("aria-label=\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(5, rail.Split("title=\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Rail_Css_Defines_Vertical_Right_Strip()
    {
        string css = File.ReadAllText(RepoFile("src", "WfWasm.CanvasHost", "wwwroot", "css", "app.css"));
        Assert.Contains("grid-template-columns:var(--sidebar) minmax(0,1fr) 60px", css);
        Assert.Contains(".app-rail{", css);
        Assert.Contains("flex-direction:column", css);
        Assert.Contains("border-left:1px solid", css);
    }
}
