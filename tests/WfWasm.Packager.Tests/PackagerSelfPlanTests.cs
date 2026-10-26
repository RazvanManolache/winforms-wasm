using System.Text.Json;
using WfWasm.Packager;

public class PackagerSelfPlanTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private static string SelfDll() =>
        System.Reflection.Assembly.GetExecutingAssembly().Location;

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    [Fact]
    public void CreatePlan_Self_Has_Entry_And_References()
    {
        string dll = SelfDll();
        PackageManifest manifest = Packager.CreatePlan(dll);
        Assert.Equal(Path.GetFileName(dll), manifest.EntryAssembly);
        Assert.Equal([Path.GetFileName(dll)], manifest.StagedFiles);
        Assert.NotEmpty(manifest.AssemblyReferences);
    }

    [Fact]
    public void CreatePlan_Self_Warns_Only_When_Binary_Resources_Without_Extensions()
    {
        // Self assembly ships no legacy .resources, so no MSB3822 warning.
        PackageManifest manifest = Packager.CreatePlan(SelfDll());
        Assert.DoesNotContain(manifest.Warnings, w => w.Contains("MSB3822"));
    }

    [Fact]
    public void Stage_Copies_Self_And_Manifest_Round_Trips()
    {
        string dll = SelfDll();
        PackageManifest manifest = Packager.CreatePlan(dll);
        Packager.Stage(manifest, dll, _workDir);
        Assert.True(File.Exists(Path.Combine(_workDir, manifest.EntryAssembly)));
        string json = File.ReadAllText(Path.Combine(_workDir, Packager.ManifestFileName));
        PackageManifest? back = JsonSerializer.Deserialize<PackageManifest>(json);
        Assert.NotNull(back);
        Assert.Equal(manifest.EntryAssembly, back.EntryAssembly);
        Assert.Equal(manifest.StagedFiles, back.StagedFiles);
        Assert.Equal(manifest.Warnings, back.Warnings);
    }

    [Fact]
    public void Stage_Null_Manifest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Packager.Stage(null!, SelfDll(), _workDir));
    }
}
