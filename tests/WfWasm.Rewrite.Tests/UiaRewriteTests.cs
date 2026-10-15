using WfWasm.Rewrite;
using Xunit;

public sealed class UiaRewriteTests
{
    private static System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> WalkTypes(
        System.Collections.Generic.IEnumerable<Mono.Cecil.TypeDefinition> types)
    {
        foreach (var t in types)
        {
            yield return t;
            foreach (var n in WalkTypes(t.NestedTypes))
                yield return n;
        }
    }

    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_TelerikUI_ClientsAreListening_Redirects_To_Stub()
    {
        // Telerik.WinControls.UI.dll calls the static UIA getter 58 times
        // (RootUIAutomationManager.CanHandleUIAutomationRequests gates
        // every list add on it): UIAutomationCore.dll is absent headless,
        // so the full rewrite must retarget every call to the fork stub.
        // Regression: DllNotFoundException UIAutomationCore <- Add <-
        // CategoryListLoaded <- DeserializeData left BookShelf shelves
        // empty (categories loaded, UI update aborted).
        string input = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release\Telerik.WinControls.UI.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"TelerikUI_Uia_{Guid.NewGuid()}.dll");
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
            using (var pristine = Mono.Cecil.AssemblyDefinition.ReadAssembly(input))
            {
                int before = WalkTypes(pristine.MainModule.Types)
                    .SelectMany(t => t.Methods)
                    .Where(m => m.HasBody)
                    .SelectMany(m => m.Body.Instructions)
                    .Select(i => i.Operand as Mono.Cecil.MethodReference)
                    .Count(mr => mr is not null && mr.Name == "get_ClientsAreListening"
                        && mr.DeclaringType.FullName == "System.Windows.Automation.Provider.AutomationInteropProvider");
                Assert.True(before > 0, "fixture drift: pristine Telerik.WinControls.UI.dll has no UIA getter calls");
            }
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var calls = WalkTypes(asm.MainModule.Types)
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as Mono.Cecil.MethodReference)
                .Where(mr => mr is not null)
                .ToList();
            Assert.DoesNotContain(calls, mr =>
                mr!.Name == "get_ClientsAreListening"
                && mr.DeclaringType.FullName == "System.Windows.Automation.Provider.AutomationInteropProvider");
            Assert.Contains(calls, mr =>
                mr!.Name == "NoClientsAreListening"
                && mr.DeclaringType.FullName == "System.Windows.Forms.WasmAutomation");
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
