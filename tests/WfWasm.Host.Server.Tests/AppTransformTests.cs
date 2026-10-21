using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

public sealed class AppTransformTests
{
    [Fact]
    public void Pristine_Assemblies_Serve_Byte_Identical()
    {
        // Assemblies the rewriter must not touch (Mono rejects
        // Cecil-processed metadata-only reference assemblies with
        // BadImageFormatException at Assembly.Load) serve pristine.
        // Regression: telerik-photo form-create failed loading
        // UIAutomationProvider even though its bytes were fetched.
        const string app = "telerik-photo";
        const string name = "UIAutomationProvider.dll";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        AppTransform.AppSpec spec = maybe!;
        string pristinePath = spec.SourceOverride.TryGetValue(name, out string? alt)
            ? alt
            : Path.Combine(spec.Folder, name);
        Assert.True(File.Exists(pristinePath), $"pristine missing: {pristinePath}");
        // Drop any cached transform of this file (an older revision may
        // have rewritten it): the cache is regenerable, the server and
        // later test runs rebuild it on demand.
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        byte[] served = AppTransform.TransformOrCached(app, name);
        Assert.Equal(File.ReadAllBytes(pristinePath), served);
    }

    [Theory]
    [InlineData("todo", "Microsoft Sans Serif", 8.25f)] // netfx 4.6 entry
    [InlineData("tictactoe", "Microsoft Sans Serif", 8.25f)] // netfx 4.8 entry
    [InlineData("calculator", "Segoe UI", 9f)] // net10 entry
    [InlineData("calendar", "Segoe UI", 9f)] // net8 entry
    [InlineData("no-such-app", "Segoe UI", 9f)] // unreadable -> modern fallback
    public void DefaultFontFor_Matches_Entry_TFM(string app, string name, float size)
    {
        // Autoscale divides the designer dims by the runtime default font's
        // metrics, so the browser must start with the same default the
        // desktop runtime would pick for the app's framework.
        (string actualName, float actualSize) = AppTransform.DefaultFontFor(app);
        Assert.Equal(name, actualName);
        Assert.Equal(size, actualSize);
    }

    [Fact]
    public void Erp_Content_Map_Covers_Data_Dir()
    {
        // ERP.Repository.InitializeData reads Data/*.json via a
        // BaseDirectory-relative walk that breaks in the browser; the VFS
        // content bridge must ship every file the sources hold.
        Assert.True(AppTransform.TryGetSpec("telerik-erp", out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        var files = maybe!.ContentFiles;
        string data = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\ERP\ERP.Client\Data");
        string[] onDisk = Directory.GetFiles(data, "*.json");
        Assert.Equal(onDisk.Length, files.Count);
        foreach (string disk in onDisk)
        {
            string key = "Data/" + Path.GetFileName(disk);
            Assert.True(files.TryGetValue(key, out string? mapped), key);
            Assert.True(File.Exists(mapped), mapped);
        }
    }

    [Fact]
    public void Paintnet_DirectWriteForwarder_Keeps_Self_Scope()
    {
        // The DWF fake is hand-built managed (the mixed-mode pristine
        // aborts WASM interp) with self-named typeref scopes; it needs
        // pinvoke-stubbing at most. Full rewrite dropped its
        // self-named assemblyref and Cecil re-emitted the orphaned
        // typerefs as System.Private.CoreLib (TypeLoad at bind time).
        const string app = "paintnet";
        const string name = "DirectWriteForwarder.dll";
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        Assert.True(maybe is not null);
        try
        {
            string dir = Path.Combine(AppTransform.CacheRoot, app);
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir, "*" + Path.GetFileNameWithoutExtension(name) + "*"))
                    File.Delete(f);
            }
        }
        catch { }
        byte[] served = AppTransform.TransformOrCached(app, name);
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(served));
        Assert.Contains(asm.MainModule.AssemblyReferences, r => r.Name == "DirectWriteForwarder");
        var face = asm.MainModule.GetTypeReferences()
            .First(t => t.FullName == "MS.Internal.Text.TextInterface.FontFace");
        Assert.Equal("DirectWriteForwarder", face.Scope.Name);
    }
}
