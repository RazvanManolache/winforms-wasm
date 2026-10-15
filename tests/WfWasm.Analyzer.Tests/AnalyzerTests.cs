using WfWasm.Analyzer;

public class AnalyzerTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string Baseline(params string[] parts) =>
        Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());

    private static string RequireBaseline(params string[] parts)
    {
        string dll = Baseline(parts);
        if (!File.Exists(dll))
            throw Xunit.Sdk.SkipException.ForSkip($"Baseline not built: {dll} (see BASELINES.md)");
        return dll;
    }

    [Fact]
    public void DatagridviewBaseline_ReferencesWinForms()
    {
        string dll = RequireBaseline("baselines", "dotnet-samples", "windowsforms",
            "datagridview", "CSWinFormDataGridView", "bin", "Debug", "net6.0-windows",
            "CSWinFormDataGridView.dll");

        ScanResult result = AssemblyScanner.Scan(dll);

        Assert.Contains(result.AssemblyReferences,
            r => r.StartsWith("System.Windows.Forms", StringComparison.Ordinal));
        Assert.Contains(result.TypeReferences,
            t => t.FullName.StartsWith("System.Windows.Forms", StringComparison.Ordinal));
        Assert.NotEmpty(result.ManifestResources);
    }

    [Fact]
    public void GraphicsBaseline_UsesDrawingApi()
    {
        string dll = RequireBaseline("baselines", "dotnet-samples", "windowsforms",
            "graphics", "WinFormGraphics", "bin", "Debug", "net7.0-windows",
            "WinFormGraphics.dll");

        ScanResult result = AssemblyScanner.Scan(dll);

        Assert.Contains(result.TypeReferences,
            t => t.FullName.StartsWith("System.Drawing", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            AssemblyScanner.Scan(Baseline("does-not-exist.dll")));
    }
}
