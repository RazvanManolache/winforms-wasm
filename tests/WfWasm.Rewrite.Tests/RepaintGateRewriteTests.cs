using WfWasm.Rewrite;
using Xunit;

public sealed class RepaintGateRewriteTests
{
    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    private static System.Collections.Generic.IEnumerable<Mono.Cecil.MethodDefinition> AllMethods(
        System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> types)
    {
        foreach (var t in types)
        {
            foreach (var m in t.Methods)
                yield return m;
            foreach (var m in AllMethods(t.NestedTypes))
                yield return m;
        }
    }

    [Fact]
    public void Rewrite_TelerikWinControls_Invalidate_Redirects_To_RepaintGate()
    {
        // Headless Control.Invalidate is a silent no-op (no handles), so
        // timer/animation UI freezes after the first frame. The full
        // rewrite retargets every Control.Invalidate call (all six
        // overloads) to the fork's WasmRepaintGate hook, which the canvas
        // host subscribes for coalesced repaints. Telerik's own
        // RadElement.Invalidate must NOT match (different declarer).
        // Regression: photo-album animations frozen at start positions.
        string input = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release\Telerik.WinControls.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"TelerikWinControls_Invalidate_{Guid.NewGuid()}.dll");
        try
        {
            string formsDll = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
            string drawingDll = ShimDll("WfWasm.Drawing", "WfWasm.Drawing");
            var map = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["System.Windows.Forms"] = formsDll,
                ["System.Windows.Forms.Primitives"] = formsDll,
                ["System.Drawing"] = drawingDll,
                ["System.Drawing.Common"] = drawingDll,
                ["System.Drawing.Primitives"] = drawingDll,
            };
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var calls = AllMethods(asm.MainModule.Types)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as Mono.Cecil.MethodReference)
                .Where(mr => mr is not null && (mr.Name == "Invalidate" || mr.Name == "RequestRepaint"))
                .ToList();
            Assert.DoesNotContain(calls, mr =>
                mr!.Name == "Invalidate"
                && mr.DeclaringType.FullName == "System.Windows.Forms.Control");
            Assert.Contains(calls, mr =>
                mr!.Name == "RequestRepaint"
                && mr.DeclaringType.FullName == "System.Windows.Forms.WasmRepaintGate");
            // AddInvalidatedRect's Invalidate(Rectangle, bool) keeps its
            // stack shape as (Control, Rectangle, bool).
            Assert.Contains(calls, mr =>
                mr!.Name == "RequestRepaint" && mr.Parameters.Count == 3);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
