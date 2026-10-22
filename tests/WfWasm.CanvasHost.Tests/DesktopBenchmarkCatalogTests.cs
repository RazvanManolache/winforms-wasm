using WfWasm.CanvasHost;
using WfWasm.Shim;

public sealed class DesktopBenchmarkCatalogTests
{
    [Fact]
    public void Requested_Benchmark_Families_Are_Present_Without_Replacing_Current_Paint()
    {
        Assert.Equal(27, DesktopBenchmarks.Entries.Count);
        Assert.Equal(27, DesktopBenchmarks.Entries.Count(d => !d.RequiresManualInput));
        Assert.Equal(0, DesktopBenchmarks.Entries.Count(d => d.RequiresManualInput));
        Assert.Equal(9, DesktopBenchmarks.Entries.Count(d => d.Family == "Heavy desktop"));
        Assert.Equal(2, DesktopBenchmarks.Entries.Count(d => d.Family == "Layout and docking"));
        Assert.Equal(3, DesktopBenchmarks.Entries.Count(d => d.Family == "Quick start"));
        var state = new SceneState();
        Assert.Equal("paintnet", state.Entries.Single(e => e.Scene == AppScene.Paint).AppFolder);
        Assert.NotEqual(AppScene.Paint, DesktopBenchmarks.Entries.Single(d => d.Slug == "benchmark-openpdn").Scene);
    }

    [Fact]
    public void Every_Benchmark_Has_A_Unique_Deep_Link_And_Its_Own_App_Folder()
    {
        var state = new SceneState();
        Assert.Equal(DesktopBenchmarks.Entries.Count, DesktopBenchmarks.Entries.Select(d => d.Scene).Distinct().Count());
        Assert.Equal(DesktopBenchmarks.Entries.Count, DesktopBenchmarks.Entries.Select(d => d.Slug).Distinct().Count());
        foreach (var demo in DesktopBenchmarks.Entries)
        {
            var entry = Assert.Single(state.Entries, e => e.Scene == demo.Scene);
            Assert.Equal(demo.Slug, entry.AppFolder);
            Assert.Equal(demo.Slug, SceneState.ToSlug(demo.Scene));
            Assert.True(SceneState.TryParseSlug(demo.Slug.ToUpperInvariant(), out var parsed));
            Assert.Equal(demo.Scene, parsed);
            Assert.Null(entry.DesktopReferenceFile);
            Assert.Contains("experimental", entry.SourceLabel!);
            Assert.Equal(demo.SourceUrl, entry.SourceUrl);
        }
    }

    [Fact]
    public void Preparation_Metadata_Is_Pinned_And_Does_Not_Describe_A_Build_As_Browser_Support()
    {
        foreach (var demo in DesktopBenchmarks.Entries)
        {
            Assert.StartsWith("benchmark-", demo.Slug);
            Assert.Matches("^benchmark-[a-z0-9-]+$", demo.Slug);
            Assert.NotEmpty(demo.License);
            Assert.NotEmpty(demo.Notes);
            Assert.NotEmpty(demo.Components);
            Assert.Equal("https", new Uri(demo.SourceUrl).Scheme);
            if (demo.RequiresManualInput)
            {
                Assert.Empty(demo.EntryFile);
                Assert.Empty(demo.Assembly);
                Assert.Empty(demo.FormType);
                Assert.Empty(demo.FormAssembly);
                Assert.Null(demo.DownloadUrl);
                continue;
            }
            Assert.Equal(demo.EntryFile, Path.GetFileName(demo.EntryFile));
            Assert.True(demo.EntryFile.EndsWith(".dll") || demo.EntryFile.EndsWith(".exe"));
            Assert.NotEmpty(demo.FormType);
            Assert.NotEmpty(demo.FormAssembly);
            Assert.NotEmpty(demo.License);
            Assert.NotEmpty(demo.Notes);
            Assert.NotEmpty(demo.Components);
            Assert.Equal("https", new Uri(demo.SourceUrl).Scheme);
            if (demo.Build is "archive" or "installer-archive")
            {
                Assert.Equal("https", new Uri(demo.DownloadUrl!).Scheme);
                Assert.Matches("^[a-f0-9]{64}$", demo.Sha256!);
            }
            else Assert.Matches("^[a-f0-9]{40}$", demo.Revision);
        }
    }

    [Fact]
    public void Vendor_Candidates_Cannot_Attempt_Form_Construction_With_Unverified_Metadata()
    {
        var pending = DesktopBenchmarks.Entries[0] with { Build = "manual" };
        Assert.Throws<NotSupportedException>(() => DesktopBenchmarkForms.Create(pending));
    }

    [Theory]
    [InlineData("reflector", "Reflector", "Reflector.exe", "Reflector.Application.UserInterface.ApplicationWindow")]
    [InlineData("fiddler-classic", "Fiddler", "Fiddler.exe", "Fiddler.frmViewer")]
    [InlineData("devexpress", "EditorsMainDemo", "EditorsMainDemo.dll", "DevExpress.XtraEditors.Demos.frmMain")]
    [InlineData("componentone", "Showcase", "Showcase.dll", "Showcase.MainForm")]
    [InlineData("infragistics", "IGPaint", "IGPaint.dll", "IGPaint.frmImageEditor")]
    public void Vendor_Entries_Have_Concrete_Preparation_Recipes_And_Real_Startup_Metadata(
        string slug, string assembly, string entry, string form)
    {
        var demo = Assert.Single(DesktopBenchmarks.Entries, d => d.Slug == "benchmark-" + slug);
        Assert.False(demo.RequiresManualInput);
        Assert.Equal(assembly, demo.Assembly);
        Assert.Equal(entry, demo.EntryFile);
        Assert.Equal(form, demo.FormType);
        Assert.Equal(assembly, demo.FormAssembly);
        Assert.Contains(demo.Build, new[] { "dotnet", "installer-archive" });
        Assert.True(demo.DownloadUrl is not null || !string.IsNullOrEmpty(demo.Project));
    }

    [Fact]
    public void New_Source_Targets_Are_Exact_Forms_And_Historical_Framework_Choices_Are_Explicit()
    {
        string[] requested = ["subtitleedit", "sharex", "naps2", "mremoteng", "realtaiizor", "cyotek",
            "advanceddatagridview", "fastcoloredtextbox", "external-calculator", "vgapainter", "hexbox"];
        foreach (var slug in requested)
        {
            var demo = Assert.Single(DesktopBenchmarks.Entries, d => d.Slug == "benchmark-" + slug);
            Assert.False(demo.RequiresManualInput);
            Assert.NotEmpty(demo.Project);
            Assert.Contains(demo.Build, new[] { "dotnet", "msbuild" });
            Assert.Contains(".", demo.FormType);
        }
        foreach (var slug in new[] { "subtitleedit", "sharex", "naps2" })
            Assert.Contains("historical", DesktopBenchmarks.Entries.Single(d => d.Slug == "benchmark-" + slug).Notes.ToLowerInvariant());
        Assert.Equal(62, (int)AppScene.BenchmarkOpenPdn);
        Assert.Equal(71, (int)AppScene.BenchmarkChip8);
    }

    [Fact]
    public void Missing_And_Unsupported_Startup_Types_Fail_Instead_Of_Opening_Another_App()
    {
        var demo = DesktopBenchmarks.Entries[0];
        Assert.Throws<TypeLoadException>(() => DesktopBenchmarkForms.Create(demo with {
            FormType = "MissingBenchmarkForm", FormAssembly = "WfWasm.CanvasHost" }));
        Assert.Throws<TypeLoadException>(() => DesktopBenchmarkForms.Create(demo with {
            FormType = typeof(DesktopBenchmarks).FullName!, FormAssembly = "WfWasm.CanvasHost" }));
    }
}
