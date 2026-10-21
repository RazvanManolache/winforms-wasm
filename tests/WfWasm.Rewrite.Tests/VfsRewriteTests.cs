using WfWasm.Rewrite;
using Xunit;

public sealed class VfsRewriteTests
{
    // The GetCreationTime call hides in a compiler-generated lambda
    // (nested type); a flat Types walk misses it (vacuous pass + fail).
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
    public void Rewrite_PhotoAlbum_GetFiles_Redirects_To_Vfs()
    {
        // Form1_Load enumerates Directory.GetFiles(@"Photos\", "*.jpg"):
        // the browser has no disk, so the full rewrite must retarget
        // every such call to the AppFileSystem registry (same shapes).
        // Regression: FileNotFoundException '/background.jpg' + empty
        // photo strip on telerik-photo (GetFiles hit the real disk).
        string input = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\PhotoAlbum\bin\PhotoAlbum.exe");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"PhotoAlbum_GetFiles_{Guid.NewGuid()}.exe");
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
                .Where(mr => mr is not null && mr.Name == "GetFiles")
                .ToList();
            Assert.NotEmpty(calls);
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.Directory");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem"
                && mr.Parameters.Count == 2);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Rewrite_BookShelf_FileOps_Redirect_To_Vfs()
    {
        // BookShelfApp falls back to BackUp.xml next to the exe (Exists +
        // FileStream open + Copy + GetCreationTime): the full rewrite must
        // retarget all four to the AppFileSystem registry, which answers
        // served content and falls through to MEMFS otherwise.
        // Regression: offline data load silently empty (Exists hit the
        // real disk) after the ExecutablePath null crash was fixed.
        string input = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\BookShelf\bin\BookShelf.exe");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"BookShelf_FileOps_{Guid.NewGuid()}.exe");
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
            var calls = WalkTypes(asm.MainModule.Types)
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as Mono.Cecil.MethodReference)
                .Where(mr => mr is not null)
                .ToList();
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.File" && mr.Name == "Exists");
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.FileStream" && mr.Name == ".ctor"
                && mr.Parameters.Count == 2);
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.File" && mr.Name == "Copy");
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.File" && mr.Name == "GetCreationTime");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem" && mr.Name == "Exists");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem" && mr.Name == "OpenFileStream");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem" && mr.Name == "CopyFile");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem" && mr.Name == "GetCreationTime");
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    private static string FixtureDll()
    {
        string dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows"));
        string dll = Path.Combine(dir, "WfWasm.Rewrite.Fixture.dll");
        Assert.True(File.Exists(dll), $"fixture not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_BaseDirectory_Getters_Redirect_To_Identity()
    {
        // Both BaseDirectory shapes (AppContext static, AppDomain
        // instance) funnel to WasmAppIdentity, which passes the real
        // value through and falls back to "/app" in the browser.
        // The instance callvirt keeps its `this` as the shim's object
        // arg (same stack shape, opcode flips to Call).
        string input = FixtureDll();
        string tempOut = Path.Combine(Path.GetTempPath(), $"BaseDir_{Guid.NewGuid()}.dll");
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
            var sites = WalkTypes(asm.MainModule.Types)
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Where(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name is "get_BaseDirectory" or "BaseDirectory")
                .ToList();
            Assert.NotEmpty(sites);
            Assert.DoesNotContain(sites, i =>
                ((Mono.Cecil.MethodReference)i.Operand).DeclaringType.FullName is "System.AppContext" or "System.AppDomain");
            Assert.Contains(sites, i =>
            {
                var mr = (Mono.Cecil.MethodReference)i.Operand;
                return mr.DeclaringType.FullName == "System.Windows.Forms.WasmAppIdentity"
                    && mr.Parameters.Count == 0 && i.OpCode == Mono.Cecil.Cil.OpCodes.Call;
            });
            Assert.Contains(sites, i =>
            {
                var mr = (Mono.Cecil.MethodReference)i.Operand;
                return mr.DeclaringType.FullName == "System.Windows.Forms.WasmAppIdentity"
                    && mr.Parameters.Count == 1 && i.OpCode == Mono.Cecil.Cil.OpCodes.Call;
            });
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void WasmAppIdentity_BaseDirectory_Never_Calls_Getter()
    {
        // The small-app migration self-rewrites the fork: if either
        // BaseDirectory overload called AppContext/AppDomain
        // get_BaseDirectory, the rule above would retarget it at the
        // shim itself (infinite recursion -> test-host stack overflow
        // in every migrated-fork consumer). The real call must live in
        // Drawing (never self-rewritten); the fork only delegates.
        string fork = ShimDll("WfWasm.WinForms", "WfWasm.WinForms");
        using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(fork);
        var methods = WalkTypes(asm.MainModule.Types)
            .Where(t => t.FullName == "System.Windows.Forms.WasmAppIdentity")
            .SelectMany(t => t.Methods)
            .Where(m => m.Name == "BaseDirectory" && m.HasBody)
            .ToList();
        Assert.Equal(2, methods.Count);
        foreach (var m in methods)
            Assert.DoesNotContain(m.Body.Instructions,
                i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "get_BaseDirectory");
    }

    [Fact]
    public void Rewrite_ErpRepository_FileOps_Redirect_To_Vfs()
    {
        // MainRepository.InitializeData builds GetFullPath(Combine(
        // BaseDirectory, "..", "Data")) and LoadData Exists-checks then
        // ReadAllTexts dir + "\X.json" into JsonConvert. The data-only
        // DLL never references the Drawing fork, so a scope-gated VFS
        // left Exists on the real disk (backslash path misses MEMFS)
        // and the caches stayed null -> Count TIE in every grid ctor.
        // Regression: ERP Purchases grid silently empty despite 14/14
        // content fetches landing (served transform had zero
        // AppFileSystem refs).
        string input = TestPaths.Repo(@"telerik\Telerik UI for WinForms demos\ExamplesProjects\ERP\ERP.Client\bin\ERP.Repository.dll");
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"ErpRepo_FileOps_{Guid.NewGuid()}.dll");
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
            Assert.Contains(asm.MainModule.AssemblyReferences,
                r => r.Name == "WfWasm.Drawing");
            var calls = WalkTypes(asm.MainModule.Types)
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand as Mono.Cecil.MethodReference)
                .Where(mr => mr is not null)
                .ToList();
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.File" && mr.Name == "Exists");
            Assert.DoesNotContain(calls, mr =>
                mr!.DeclaringType.FullName == "System.IO.File" && mr.Name == "ReadAllText");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem" && mr.Name == "Exists");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "WfWasm.Drawing.AppFileSystem" && mr.Name == "ReadAllText"
                && mr.Parameters.Count == 1);
            // InitializeData root cause (above the VFS pair): the browser
            // answers null for AppDomain.BaseDirectory, so Combine(null,
            // "..", "Data") threw path1 before any read was attempted
            // (caught -> zero rows -> Count TIE). The instance getter
            // keeps its `this` as the shim's object arg.
            Assert.DoesNotContain(calls, mr =>
                mr!.Name == "get_BaseDirectory" && mr.DeclaringType.FullName == "System.AppDomain");
            Assert.Contains(calls, mr =>
                mr!.DeclaringType.FullName == "System.Windows.Forms.WasmAppIdentity" && mr.Name == "BaseDirectory"
                && mr.Parameters.Count == 1);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
