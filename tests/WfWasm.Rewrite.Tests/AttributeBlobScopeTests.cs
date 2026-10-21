using Mono.Cecil;
using WfWasm.Rewrite;

public sealed class AttributeBlobScopeTests
{
    private static string FixtureDll()
    {
        string dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows"));
        string dll = Path.Combine(dir, "WfWasm.Rewrite.Fixture.dll");
        Assert.True(File.Exists(dll), $"fixture not built: {dll}");
        return dll;
    }

    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    private static Dictionary<string, string> ShimMap() => new(StringComparer.Ordinal)
    {
        ["System.Windows.Forms"] = ShimDll("WfWasm.Shim", "WfWasm.Shim"),
        ["System.Windows.Forms.Primitives"] = ShimDll("WfWasm.Shim", "WfWasm.Shim"),
        ["System.Drawing"] = ShimDll("WfWasm.Drawing", "WfWasm.Drawing"),
        ["System.Drawing.Common"] = ShimDll("WfWasm.Drawing", "WfWasm.Drawing"),
        ["System.Drawing.Primitives"] = ShimDll("WfWasm.Drawing", "WfWasm.Drawing"),
    };

    private static Dictionary<string, (string Assembly, string Type)> BootTypeMap() => new(StringComparer.Ordinal)
    {
        ["System.Drawing.SizeConverter"] = ("WfWasm.Drawing", "System.Drawing.SizeConverter"),
    };

    private static TypeReference BlobConverterArg(string dll)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(dll);
        TypeDefinition? fixture = assembly.MainModule.GetType("WfWasm.Rewrite.Fixture.ConverterFixture");
        Assert.NotNull(fixture);
        CustomAttribute? attr = fixture.CustomAttributes.FirstOrDefault(a =>
            a.AttributeType.FullName == "System.ComponentModel.TypeConverterAttribute");
        Assert.NotNull(attr);
        Assert.NotEmpty(attr.ConstructorArguments);
        var arg = Assert.IsType<TypeReference>(attr.ConstructorArguments[0].Value);
        return arg;
    }

    [Fact]
    public void Rewrite_Remaps_TypeValued_Attribute_Args()
    {
        // Negative control: the desktop fixture points at a framework scope.
        TypeReference before = BlobConverterArg(FixtureDll());
        Assert.NotEqual("WfWasm.Drawing", before.Scope.Name);

        string output = Path.Combine(Path.GetTempPath(), "greeter.attrblob.dll");
        AssemblyRewriter.Rewrite(FixtureDll(), output, ShimMap(), BootTypeMap());

        TypeReference after = BlobConverterArg(output);
        Assert.Equal("WfWasm.Drawing", after.Scope.Name);
        Assert.Equal("System.Drawing.SizeConverter", after.FullName);
    }
}
