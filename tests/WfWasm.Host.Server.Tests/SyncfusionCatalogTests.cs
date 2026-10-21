using WfWasm.CanvasHost;
using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class SyncfusionCatalogTests
{
    public static IEnumerable<object[]> Samples() => SyncfusionDemos.Entries.Select(d => new object[] { d.Slug });

    [Fact] public void Catalog_Routes_Are_Unique_And_Resolve_To_Actual_Scenes()
    {
        var state = new SceneState();
        Assert.Equal(42, SyncfusionDemos.Entries.Count);
        Assert.Equal(42, SyncfusionDemos.Entries.Select(d => d.Project).Distinct().Count());
        Assert.Equal(42, SyncfusionDemos.Entries.Select(d => d.Scene).Distinct().Count());
        Assert.Equal(state.Entries.Count, state.Entries.Select(e => SceneState.ToSlug(e.Scene)).Distinct().Count());
        foreach (var demo in SyncfusionDemos.Entries)
        {
            Assert.True(SceneState.TryParseSlug(demo.Slug, out var parsed)); Assert.Equal(demo.Scene, parsed);
            var entry = Assert.Single(state.Entries, e => e.Scene == demo.Scene);
            Assert.Equal(demo.Slug, entry.AppFolder); Assert.Equal(SyncfusionDemos.RepositoryUrl, entry.SourceUrl);
            Assert.Contains("experimental", entry.SourceLabel!);
        }
    }
    [Theory][MemberData(nameof(Samples))]
    public void Native_Entry_And_Declared_Form_Are_Present(string slug)
    {
        var demo = SyncfusionDemos.Entries.Single(d => d.Slug == slug);
        Assert.True(AppTransform.TryGetSpec(slug, out var spec));
        Assert.Equal(new[] { demo.Assembly + ".dll" }, spec!.Assemblies);
        Assert.True(AppTransform.TryResolveAssembly(slug, demo.Assembly, out string? file));
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(spec.Folder, file!));
        Assert.Equal(demo.Assembly, assembly.Name.Name);
        var form = assembly.MainModule.GetType(demo.FormType);
        Assert.NotNull(form);
        Assert.Contains(form.Methods, m => m.IsConstructor && m.IsPublic && !m.IsStatic && !m.HasParameters);
    }

    [Theory][MemberData(nameof(Samples))]
    public void Coverage_Metadata_Is_Present_And_References_Real_Native_Types(string slug)
    {
        var demo = SyncfusionDemos.Entries.Single(d => d.Slug == slug);
        Assert.False(string.IsNullOrWhiteSpace(demo.Family));
        Assert.False(string.IsNullOrWhiteSpace(demo.Purpose));
        Assert.NotEmpty(demo.Components);
        Assert.Equal(demo.Components.Length, demo.Components.Distinct().Count());
        Assert.True(AppTransform.TryGetSpec(slug, out var spec));
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(spec!.Folder, demo.Assembly + ".dll"));
        var references = assembly.MainModule.GetTypeReferences().Select(t => t.FullName).ToHashSet();
        foreach (string component in demo.Components)
        {
            Assert.StartsWith("Syncfusion.", component);
            Assert.Contains(component, references);
        }
    }

    [Fact] public void Selection_Preserves_Baseline_And_Covers_Missing_Component_Families()
    {
        string[] original = ["buttons", "combobox", "datagrid", "chart", "diagram", "calendar", "treeview", "numeric"];
        foreach (string suffix in original) Assert.Contains(SyncfusionDemos.Entries, d => d.Slug == "syncfusion-" + suffix);
        string[] required = ["SfButton", "SfComboBox", "SfDataGrid", "SfListView", "SfCalendar", "SfDateTimeEdit",
            "SfNumericTextBox", "ChartControl", "Diagram", "RibbonControlAdv", "RadialMenu", "DockingManager",
            "EditControl", "WizardControl", "DigitalGauge", "LinearGauge", "RadialGauge", "PivotGridControl",
            "GridGroupingControl", "GridControl", "Maps", "TreeMap", "SfBarcode", "BulletGraph", "SfSmithChart",
            "PivotChart", "HTMLUIControl", "NavigationDrawer", "MultiColumnTreeView", "RangeSlider", "RadialSlider",
            "RatingControl", "MaskedEditBox", "SfToolTip", "SfScrollFrame", "TabbedMDIManager", "HubTile",
            "SpellCheckerAdv", "ToggleButton", "SplitContainerAdv", "TileLayout", "StatusBarAdv", "FontComboBox",
            "AutoComplete", "Clock"];
        var covered = SyncfusionDemos.Entries.SelectMany(d => d.Components).Select(t => t.Split('.').Last()).ToHashSet();
        foreach (string component in required) Assert.Contains(component, covered);
        Assert.Equal(new[] { "Composite", "Data", "Documents", "Input", "Layout", "Navigation", "Notification", "Visualization", "Workspace" },
            SyncfusionDemos.Entries.Select(d => d.Family).Distinct().Order().ToArray());
    }

    [Fact] public void Map_Data_Is_Delivered_But_Configuration_And_License_Files_Are_Not()
    {
        Assert.True(AppTransform.TryGetSpec("syncfusion-maps", out var spec));
        Assert.Contains("world1.shp", spec!.ContentFiles.Keys);
        Assert.Contains("world1.dbf", spec.ContentFiles.Keys);
        Assert.DoesNotContain(spec.ContentFiles.Keys, p => p.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void Diagram_Nodes_Image_Is_Served_As_Vfs_Content()
    {
        // NodesDemo.Form1_Load builds a BitmapNode from image3.png via a
        // dev-layout relative path outside the build output; without this
        // entry BitmapNode..ctor throws FileNotFoundException inside OnLoad
        // and OOB-traps the session. VFS lookups match by tail.
        Assert.True(AppTransform.TryGetSpec("syncfusion-diagram", out var spec));
        Assert.True(spec!.ContentFiles.TryGetValue("image3.png", out string? path));
        Assert.True(File.Exists(path), "diagram image3.png source missing from checkout");
    }
    [Theory][MemberData(nameof(Samples))]
    public void Dependencies_Are_Available_On_Demand_And_Content_Excludes_Assemblies_And_Keys(string slug)
    {
        Assert.True(AppTransform.TryGetSpec(slug, out var spec));
        Assert.True(AppTransform.TryResolveAssembly(slug, "Syncfusion.Licensing", out _));
        Assert.DoesNotContain(spec!.ContentFiles.Keys, p => p.Contains("license", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.False(AppTransform.TryResolveAssembly(slug, "../Syncfusion.Licensing.dll", out _));
    }
    [Fact] public void Same_Named_Demo_Assemblies_Do_Not_Share_Content_Identity()
    {
        Assert.True(AppTransform.TryGetSpec("syncfusion-buttons", out var buttons));
        Assert.True(AppTransform.TryGetSpec("syncfusion-combobox", out var combo));
        Assert.Equal(buttons!.Entry, combo!.Entry); Assert.NotEqual(buttons.Folder, combo.Folder);
        Assert.NotEqual(AppTransform.ContentHash("syncfusion-buttons", "GettingStarted.dll"), AppTransform.ContentHash("syncfusion-combobox", "GettingStarted.dll"));
    }

    [Fact] public void Shared_Dependency_Rewrite_Can_Resolve_Injected_Compatibility_Types()
    {
        // Shared.Base uses isolated storage. Its rewrite injects WfWasm.IsolatedStorage
        // references which Cecil must resolve even though the vendor output lacks that DLL.
        byte[] bytes = AppTransform.TransformOrCached("syncfusion-buttons", "Syncfusion.Shared.Base.dll");
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
        Assert.Contains(assembly.MainModule.AssemblyReferences, r => r.Name == "WfWasm.IsolatedStorage");
    }
}
