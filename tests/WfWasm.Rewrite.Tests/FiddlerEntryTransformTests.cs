using Mono.Cecil;
using WfWasm.Rewrite;

public sealed class FiddlerEntryTransformTests
{
    private static string RepoRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    // Focused reproducer for the benchmark-fiddler-classic manifest 500:
    // transforming the entry assembly threw NullReferenceException in
    // Cecil's own writer (Mixin.HasImplicitThis <- ComputeStackDelta),
    // i.e. the rewrite emitted IL Cecil cannot even serialize. Mirrors
    // the server recipe (AppTransform.TransformOrCached for benchmarks:
    // fork/drawing assembly map, AutoMap, WinRT token remaps, folder
    // closure). RAM-only: no temp files, no cross-checkout collisions.
    [Fact]
    public void Rewrite_Fiddler_Entry_Serializes()
    {
        string folder = Path.Combine(RepoRoot(), ".artifacts", "benchmark-apps", "benchmark-fiddler-classic");
        string input = Path.Combine(folder, "Fiddler.exe");
        Assert.True(File.Exists(input),
            "benchmark input not prepared (run prepare-desktop-benchmarks.ps1): " + input);
        string fork = TestPaths.Repo(@".artifacts\runtime\WfWasm.WinForms.dll");
        Assert.True(File.Exists(fork), "migrated fork not built (run rewrite-small.ps1): " + fork);
        string drawing = Path.Combine(RepoRoot(), "src", "WfWasm.Drawing", "bin", "Debug", "net9.0", "WfWasm.Drawing.dll");
        Assert.True(File.Exists(drawing), "drawing not built: " + drawing);

        var assemblyMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Windows.Forms"] = fork,
            ["System.Windows.Forms.Primitives"] = fork,
            ["System.Drawing"] = drawing,
            ["System.Drawing.Common"] = drawing,
            ["System.Drawing.Primitives"] = drawing,
        };
        var typeMap = GapAnalyzer.AutoMap(input, assemblyMap);
        typeMap["System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken"] =
            ("Windows.Foundation.FoundationContract", "System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken");
        typeMap["System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal"] =
            ("Windows.Foundation.FoundationContract", "System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal");

        byte[] bytes = AssemblyRewriter.RewriteToBytes(input, assemblyMap, typeMap, null, null,
            new[] { folder, AppContext.BaseDirectory });
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0, "rewrite produced empty bytes");

        // The bytes must round-trip through Cecil (this is where the
        // server died: ModuleDefinition.Write inside RewriteToBytes).
        // RewriteToBytes already wrote once; a second parse proves the
        // output is structurally sound, not just non-empty.
        using var stream = new MemoryStream(bytes, writable: false);
        using var asm = AssemblyDefinition.ReadAssembly(stream);
        Assert.NotNull(asm.EntryPoint);
    }
}
