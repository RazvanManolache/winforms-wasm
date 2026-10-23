using WfWasm.Rewrite;
using Xunit;

public sealed class FileVersionRedirectTests
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
    public void Rewrite_CyotekDemo_GetVersionInfo_Redirects_To_WasmFileVersion()
    {
        // AboutDialog.OnLoad calls FileVersionInfo.GetVersionInfo. The
        // browser framework ships that type as a field-less throwing stub
        // (every member PNSEs), so the throw aborts the whole OnLoad chain:
        // MainForm never builds its Demonstrations tab and the demo boots to
        // a blank panel. The rewrite must retarget the call to the fork's
        // metadata-backed WasmFileVersion shim.
        string tempOut;
        if (!TryRewriteCyotek(out tempOut)) return;
        try
        {
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var calls = CollectCalls(asm);
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.Diagnostics.FileVersionInfo"
                && mr.Name == "GetVersionInfo");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "System.Windows.Forms.WasmFileVersion"
                && mr.Name == "GetVersionInfo");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Rewrite_CyotekDemo_Getters_And_ToString_Redirect_To_Readers()
    {
        // The stub has no backing fields, so redirecting the factory alone
        // still PNSEs on the first property read (AboutDialog reads
        // ProductName next). Every instance getter and ToString on
        // FileVersionInfo must become a WasmFileVersion.Read*/Describe call.
        string tempOut;
        if (!TryRewriteCyotek(out tempOut)) return;
        try
        {
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var calls = CollectCalls(asm);
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.Diagnostics.FileVersionInfo"
                && (mr.Name == "ToString" || mr.Name.StartsWith("get_")));
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "System.Windows.Forms.WasmFileVersion"
                && mr.Name == "ReadProductName");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    private static bool TryRewriteCyotek(out string tempOut)
    {
        string input = TestPaths.Repo(@".artifacts\benchmark-apps\benchmark-cyotek\Cyotek.Windows.Forms.ColorPicker.Demo.exe");
        tempOut = Path.Combine(Path.GetTempPath(), $"CyotekDemo_FileVersion_{Guid.NewGuid()}.dll");
        if (!File.Exists(input)) return false;
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
        return true;
    }

    private static List<Mono.Cecil.MethodReference?> CollectCalls(Mono.Cecil.AssemblyDefinition asm)
    {
        return asm.MainModule.Types
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand as Mono.Cecil.MethodReference)
            .Where(mr => mr is not null)
            .ToList();
    }
}
