using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

public sealed class CasRedirectTests
{
    private static readonly string TelerikPool = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\Bin\Release");

    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    [Fact]
    public void Redirect_Synthetic_UIPermission_Field_Moves_To_Fork()
    {
        // Hermetic: a fabricated field of mscorlib-scoped UIPermission
        // (the RadLabel:allWindows shape) must move to WfWasm.WinForms.
        string tempOut = Path.Combine(Path.GetTempPath(), $"CasSynth_{Guid.NewGuid()}.dll");
        try
        {
            // Cecil only materializes the TypeRef table on Write, so the
            // fixture is built, written, then re-read before the pass.
            var asmName = new AssemblyNameDefinition("CasSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "CasSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                var mscorlib = new AssemblyNameReference("mscorlib", new Version(4, 0, 0, 0));
                module.AssemblyReferences.Add(mscorlib);
                var uiPerm = new TypeReference("System.Security.Permissions", "UIPermission", module, mscorlib);
                var host = new TypeDefinition("CasSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                host.Fields.Add(new FieldDefinition("allWindows", FieldAttributes.Public, uiPerm));
                module.Types.Add(host);
                // A non-CAS control: must keep its scope untouched.
                var crypto = new TypeReference("System.Security.Cryptography", "HashAlgorithm", module, mscorlib);
                host.Fields.Add(new FieldDefinition("hash", FieldAttributes.Public, crypto));
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition module = read.MainModule;
                AssemblyRewriter.RedirectCasTypeReferences(module);

                TypeReference uiPerm = module.GetTypeReferences().First(
                    t => t.FullName == "System.Security.Permissions.UIPermission");
                Assert.Equal("WfWasm.WinForms", ((AssemblyNameReference)uiPerm.Scope).Name);
                Assert.Equal("System.Security.Permissions", uiPerm.Namespace);
                Assert.Equal("UIPermission", uiPerm.Name);
                TypeReference hash = module.GetTypeReferences().First(
                    t => t.FullName == "System.Security.Cryptography.HashAlgorithm");
                Assert.Equal("mscorlib", ((AssemblyNameReference)hash.Scope).Name);
                Assert.Contains(module.AssemblyReferences, r => r.Name == "WfWasm.WinForms");
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void FullRewrite_TelerikUI_No_Mscorlib_CAS_Typerefs_Remain()
    {
        // Integration: the real Telerik.WinControls.UI.dll carries CAS
        // field/member refs (RadLabel:allWindows). After the full rewrite
        // every CAS reference must point at the fork stubs; any survivor
        // is fatal at load in the browser.
        // Regression: TypeLoadException UIPermission <- BookShelf.MainForm
        // create (every Telerik demo blank, zero console errors).
        string input = Path.Combine(TelerikPool, "Telerik.WinControls.UI.dll");
        if (!File.Exists(input))
            return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"TelerikUI_CAS_{Guid.NewGuid()}.dll");
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
            using var asm = AssemblyDefinition.ReadAssembly(tempOut);
            var survivors = asm.MainModule.GetTypeReferences()
                .Where(t => t.Namespace == "System.Security.Permissions"
                    && t.Scope is AssemblyNameReference ar && ar.Name != "WfWasm.WinForms")
                .Select(t => t.FullName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            Assert.True(survivors.Count == 0,
                "CAS typerefs still mscorlib-scoped: " + string.Join(", ", survivors.Take(10)));
            Assert.Contains(asm.MainModule.GetTypeReferences(), t =>
                t.FullName == "System.Security.Permissions.UIPermission"
                && t.Scope is AssemblyNameReference ar && ar.Name == "WfWasm.WinForms");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
