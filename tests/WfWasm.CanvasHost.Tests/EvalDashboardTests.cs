using System.Text.Json;

/// <summary>
/// Evaluation dashboard contract (eval 2026-10-17): the published run file
/// must load and cover every app, and the dashboard route must exist.
/// </summary>
public sealed class EvalDashboardTests
{
    private static string RepoFile(params string[] parts)
    {
        string? dir = Path.GetDirectoryName(SourceFile());
        return Path.GetFullPath(Path.Combine(new[] { "../../" }.Concat(parts).ToArray()), dir!);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void Latest_Results_Load_And_Cover_Every_App()
    {
        string file = RepoFile("src", "WfWasm.CanvasHost", "wwwroot", "eval", "latest.json");
        Assert.True(File.Exists(file), file);
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("runAt", out _));
        Assert.Equal(10, root.GetProperty("budgets").GetProperty("frameMs").GetInt32());
        var apps = root.GetProperty("apps").EnumerateArray().ToList();
        Assert.Equal(20, apps.Count);
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in apps)
        {
            string slug = a.GetProperty("slug").GetString()!;
            Assert.True(slugs.Add(slug), $"duplicate {slug}");
            Assert.Equal("/app/" + slug, a.GetProperty("url").GetString());
            Assert.True(a.TryGetProperty("bootMs", out _));
            Assert.True(a.TryGetProperty("frameMs", out _));
            Assert.Contains(a.GetProperty("frameVerdict").GetString(), new[] { "pass", "fail" });
            Assert.True(a.GetProperty("featuresTested").GetInt32() >= 1);
            Assert.True(a.GetProperty("featuresPass").GetInt32() <= a.GetProperty("featuresTested").GetInt32());
            Assert.True(a.TryGetProperty("parity", out _));
            Assert.True(a.TryGetProperty("errors", out _));
        }
    }

    [Fact]
    public void Latest_Results_Record_Generation_Provenance()
    {
        // Every eval run must record which bundle generation it measured
        // (native fingerprints + served layout), so mixed-generation runs
        // (pass 3 redeploy, pass 4 rev54 restart) stay attributable.
        string file = RepoFile("src", "WfWasm.CanvasHost", "wwwroot", "eval", "latest.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("framework", out var fw)
            && fw.GetString()!.Length > 10);
        Assert.True(root.TryGetProperty("layout", out var layout)
            && layout.GetString()!.Length > 10);
    }

    [Fact]
    public void Dashboard_Route_And_Nav_Link_Exist()
    {
        string razor = RepoFile("src", "WfWasm.CanvasHost", "Pages", "Dashboard.razor");
        Assert.True(File.Exists(razor), razor);
        string text = File.ReadAllText(razor);
        Assert.Contains("@page \"/dashboard\"", text);
        Assert.Contains("eval/latest.json", text);
        string layout = RepoFile("src", "WfWasm.CanvasHost", "Layout", "MainLayout.razor");
        Assert.Contains("href=\"/dashboard\"", File.ReadAllText(layout));
    }
}
