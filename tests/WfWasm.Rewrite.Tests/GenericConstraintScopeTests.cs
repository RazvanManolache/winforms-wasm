using Mono.Cecil;
using WfWasm.Rewrite;

public sealed class GenericConstraintScopeTests
{
    private static string ShimBinDll(string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", name, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim bin not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_Imports_Synthesized_Generic_Constraint_Scopes()
    {
        // The synthesized CpuDrawingContext.TryGetResourceCache<T> copies its
        // T constraint from the IResourceCacheHolder contract: sharing that
        // foreign TypeReference re-scoped IObjectRef to System.Numerics.Vectors
        // at write time (live TypeLoad token 010002E7, paint brush strokes and
        // GPU-effect dialogs dead). The constraint must stay ComponentModel.
        string corePath = TestPaths.Repo(@"targets\paint.net\PaintDotNet.Windows.Core.dll");
        if (!File.Exists(corePath)) return;
        string cpuDll = ShimBinDll("WfWasm.PdnCpu");
        string tempOut = Path.Combine(Path.GetTempPath(), $"WindowsCore_GCon_{Guid.NewGuid()}.dll");
        try
        {
            AssemblyRewriter.Rewrite(
                corePath, tempOut,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, (string Assembly, string Type)>(StringComparer.Ordinal),
                cpuDll);

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(corePath)!);
            using var asm = AssemblyDefinition.ReadAssembly(tempOut,
                new ReaderParameters { AssemblyResolver = resolver });
            var ctx = asm.MainModule.GetType("PaintDotNet.Direct2D1.CpuDrawingContext");
            Assert.NotNull(ctx);
            var tryGet = ctx.Methods.First(m => m.Name == "TryGetResourceCache" && m.HasGenericParameters);
            var constraint = Assert.Single(tryGet.GenericParameters[0].Constraints).ConstraintType;
            Assert.Equal("PaintDotNet.ComponentModel.IObjectRef", constraint.FullName);
            var scope = Assert.IsType<AssemblyNameReference>(constraint.Scope);
            Assert.Equal("PaintDotNet.ComponentModel", scope.Name);
            // The constraint must resolve inside the closure: an orphaned
            // scope is what the loader rejects with TypeLoad.
            Assert.NotNull(constraint.Resolve());
        }
        finally { if (File.Exists(tempOut)) File.Delete(tempOut); }
    }
}
