using WfWasm.CanvasHost;
using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class DesktopBenchmarkInputTests
{
    [Fact]
    public void Entry_Only_Manifests_Do_Not_Preload_The_Dependency_Folder()
    {
        string root = Path.Combine(Path.GetTempPath(), "absent-benchmark-inputs-" + Guid.NewGuid().ToString("N"));
        foreach (var demo in DesktopBenchmarks.Entries.Where(d => !d.RequiresManualInput))
        {
            var spec = DesktopBenchmarkInputs.CreateSpec(demo, root);
            Assert.Equal(new[] { demo.EntryFile }, spec.Assemblies);
            Assert.Equal(demo.Assembly, spec.Entry);
            Assert.Equal(Path.Combine(root, demo.Slug), spec.Folder);
            Assert.Equal(new[] { Path.Combine(root, demo.Slug, "dll") }, spec.AssemblySearchFolders);
            Assert.Empty(spec.ContentFiles);
            Assert.False(DesktopBenchmarkInputs.IsPrepared(demo, root));
        }
    }

    [Fact]
    public void Registered_Server_Specs_Match_The_Same_Catalog_As_The_Menu()
    {
        foreach (var demo in DesktopBenchmarks.Entries.Where(d => !d.RequiresManualInput))
        {
            Assert.True(AppTransform.TryGetSpec(demo.Slug, out var spec));
            Assert.Equal(demo.EntryFile, Assert.Single(spec!.Assemblies));
            Assert.Equal(demo.Assembly, spec.Entry);
            Assert.EndsWith(demo.Slug, spec.Folder);
        }
    }

    [Fact]
    public void Manual_Vendor_Entries_Have_No_Server_Payload_Or_Prepared_State()
    {
        string root = Path.Combine(Path.GetTempPath(), "wf-manual-bench-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var demo in new[] { DesktopBenchmarks.Entries[0] with { Build = "manual", Slug = "benchmark-pending-test" } })
            {
                string folder = Path.Combine(root, demo.Slug);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "preparation.json"), "{\"prepared\":true}");
                File.WriteAllBytes(Path.Combine(folder, "unverified.exe"), [1, 2, 3]);
                Assert.False(DesktopBenchmarkInputs.IsPrepared(demo, root));
                Assert.Throws<InvalidOperationException>(() => DesktopBenchmarkInputs.CreateSpec(demo, root));
                Assert.False(AppTransform.TryGetSpec(demo.Slug, out _));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Failed_Or_Interrupted_Preparation_Cannot_Advertise_A_Stale_Entry_As_Prepared()
    {
        var demo = DesktopBenchmarks.Entries[0];
        string root = Path.Combine(Path.GetTempPath(), "wf-benchmark-test-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, demo.Slug);
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllBytes(Path.Combine(folder, demo.EntryFile), [1, 2, 3]);
            Assert.True(DesktopBenchmarkInputs.IsPrepared(demo, root)); // manually supplied, compatibility unverified
            string receipt = Path.Combine(folder, "preparation.json");
            File.WriteAllText(receipt, "{\"prepared\":false}");
            Assert.False(DesktopBenchmarkInputs.IsPrepared(demo, root));
            File.WriteAllText(receipt, "{");
            Assert.False(DesktopBenchmarkInputs.IsPrepared(demo, root));
            File.WriteAllText(receipt, "{\"prepared\":true}");
            Assert.True(DesktopBenchmarkInputs.IsPrepared(demo, root));
            File.Delete(Path.Combine(folder, demo.EntryFile));
            Assert.False(DesktopBenchmarkInputs.IsPrepared(demo, root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
