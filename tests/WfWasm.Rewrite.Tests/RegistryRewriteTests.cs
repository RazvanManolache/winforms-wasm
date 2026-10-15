using WfWasm.Rewrite;
using Xunit;

public sealed class RegistryRewriteTests
{
    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_PdnSystemLayer_Registry_Redirects_To_WasmRegistry()
    {
        // UIHelpers.GetWindowColorizationInfo reads HKCU\SOFTWARE\Microsoft
        // \Windows\DWM; the browser has no registry and the runtime NREs
        // (CurrentUser is null), so the full rewrite must retarget every
        // Registry/RegistryKey call to the WasmRegistry missing-key stubs.
        // Regression: NRE in GetWindowColorizationInfo <- PdnToolBar paint
        // on every PDN frame.
        string input = TestPaths.Repo(@"targets\paint.net\PaintDotNet.SystemLayer.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PdnSysLayer_Registry_{Guid.NewGuid()}.dll");
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
            var calls = asm.MainModule.Types
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as Mono.Cecil.MethodReference)
                .Where(mr => mr is not null)
                .ToList();
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "Microsoft.Win32.RegistryKey"
                && (mr.Name == "OpenSubKey" || mr.Name == "GetValue"));
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "Microsoft.Win32.Registry"
                && mr.Name == "GetValue");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "System.Windows.Forms.WasmRegistry"
                && mr.Name == "OpenSubKey");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "System.Windows.Forms.WasmRegistry"
                && mr.Name == "GetValue");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
