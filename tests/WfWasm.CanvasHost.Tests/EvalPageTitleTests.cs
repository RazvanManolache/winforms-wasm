/// <summary>
/// Page-title contract (eval 2026-10-17 pass 5): every /app page served
/// a mojibake title ("Smoke form Ã‚Â· App Studio") because Home.razor
/// carries a double-UTF8-encoded middle dot. Source-side lock in the
/// EvalRailTests style (markup bytes, not the served deploy).
/// </summary>
public sealed class EvalPageTitleTests
{
    private static string RepoFile(params string[] parts)
    {
        string? dir = Path.GetDirectoryName(SourceFile());
        return Path.GetFullPath(Path.Combine(new[] { "../../" }.Concat(parts).ToArray()), dir!);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void Home_PageTitle_Uses_Middle_Dot_Separator()
    {
        string text = File.ReadAllText(RepoFile("src", "WfWasm.CanvasHost", "Pages", "Home.razor"));
        Assert.Contains("· App Studio", text);
        Assert.DoesNotContain("Ã", text);
    }

    [Fact]
    public void Dashboard_PageTitle_Uses_Middle_Dot_Separator()
    {
        // Control: the dashboard title already carries the correct
        // separator (proves the expectation, not the harness).
        string text = File.ReadAllText(RepoFile("src", "WfWasm.CanvasHost", "Pages", "Dashboard.razor"));
        Assert.Contains("· App Studio", text);
        Assert.DoesNotContain("Ã", text);
    }
}
