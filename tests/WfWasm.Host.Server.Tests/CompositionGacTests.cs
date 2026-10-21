using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using WfWasm.Host.Server;

namespace WfWasm.Host.Server.Tests;

// MEF1 ships pristine from the GAC for Telerik apps: RichTextEditor and
// SpellCheck compose parts at startup and died with FileNotFoundException
// for 'System.ComponentModel.Composition, Version=4.0.0.0'. Composition
// carries the ECMA public key (b77a5c561934e089), not the framework key
// the UIAutomation* precedents use, so the GAC path must be key-aware.
public sealed class CompositionGacTests
{
    [Theory]
    [InlineData("telerik-richtexteditor")]
    [InlineData("telerik-spellcheckasyoutype")]
    public void Composition_Resolves_From_EcmaKey_Gac_Folder(string app)
    {
        Assert.True(AppTransform.TryGetSpec(app, out AppTransform.AppSpec? maybe));
        AppTransform.AppSpec spec = maybe!;
        Assert.True(
            AppTransform.TryResolveAssembly(app, "System.ComponentModel.Composition.dll", out string? file),
            $"Composition unresolved for {app}");
        Assert.Equal("System.ComponentModel.Composition.dll", file);
        Assert.True(spec.SourceOverride.TryGetValue("System.ComponentModel.Composition.dll", out string? gacPath));
        Assert.Contains("b77a5c561934e089", gacPath);
        Assert.True(File.Exists(gacPath), $"GAC copy missing: {gacPath}");
    }

    [Fact]
    public void Composition_Serves_With_Exact_Mef_Identity()
    {
        byte[] served = AppTransform.TransformOrCached(
            "telerik-richtexteditor", "System.ComponentModel.Composition.dll");
        Assert.NotEmpty(served);
        using var pe = new PEReader(new MemoryStream(served, writable: false));
        MetadataReader md = pe.GetMetadataReader();
        AssemblyDefinition def = md.GetAssemblyDefinition();
        Assert.Equal("System.ComponentModel.Composition", md.GetString(def.Name));
        Assert.Equal(new Version(4, 0, 0, 0), def.Version);
    }
}
