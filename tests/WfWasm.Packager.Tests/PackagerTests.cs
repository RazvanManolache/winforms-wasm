using System.Text.Json;
using WfWasm.Packager;

public class PackagerTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    private static string BaselineDll()
    {
        string dll = Path.Combine(RepoRoot, "baselines", "dotnet-samples", "windowsforms",
            "datagridview", "CSWinFormDataGridView", "bin", "Debug", "net6.0-windows",
            "CSWinFormDataGridView.dll");
        if (!File.Exists(dll))
            throw Xunit.Sdk.SkipException.ForSkip($"Baseline not built: {dll} (see BASELINES.md)");
        return dll;
    }

    [Fact]
    public void CreatePlan_FlagsLegacyBinaryResources()
    {
        PackageManifest manifest = Packager.CreatePlan(BaselineDll());

        Assert.Equal("CSWinFormDataGridView.dll", manifest.EntryAssembly);
        Assert.Equal(new[] { "CSWinFormDataGridView.dll" }, manifest.StagedFiles);
        Assert.NotEmpty(manifest.AssemblyReferences);
        Assert.Contains(manifest.Warnings, w => w.Contains("System.Resources.Extensions"));
    }

    [Fact]
    public void Stage_CopiesAssemblyAndWritesManifest()
    {
        string dll = BaselineDll();
        PackageManifest manifest = Packager.CreatePlan(dll);

        Packager.Stage(manifest, dll, _workDir);

        Assert.True(File.Exists(Path.Combine(_workDir, "CSWinFormDataGridView.dll")));
        string jsonPath = Path.Combine(_workDir, Packager.ManifestFileName);
        PackageManifest? roundTripped = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(jsonPath));
        Assert.NotNull(roundTripped);
        Assert.Equal(manifest.EntryAssembly, roundTripped.EntryAssembly);
        Assert.Equal(manifest.Warnings, roundTripped.Warnings);
    }

    [Fact]
    public void CreatePlan_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            Packager.CreatePlan(Path.Combine(_workDir, "nope.dll")));
    }

    [Fact]
    public void CreatePlan_UnmanagedFile_Throws()
    {
        Directory.CreateDirectory(_workDir);
        string junk = Path.Combine(_workDir, "junk.dll");
        File.WriteAllBytes(junk, new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
        Assert.Throws<InvalidDataException>(() => Packager.CreatePlan(junk));
    }
}
