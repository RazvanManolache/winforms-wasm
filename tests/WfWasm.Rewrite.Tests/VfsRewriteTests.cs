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
}
