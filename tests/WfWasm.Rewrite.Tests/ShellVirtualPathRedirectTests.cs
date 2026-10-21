using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectShellVirtualPaths: ShellUtil.GetVirtualPath is answered
// from the /vfs layout (shell folders are "" headless), honoring the
// create flag with a guarded CreateDirectory; only mscorlib callees.
public sealed class ShellVirtualPathRedirectTests
{
    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("ShellSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "ShellSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var util = new TypeDefinition("PaintDotNet.SystemLayer", "ShellUtil",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var target = new MethodDefinition("GetVirtualPath",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.String);
            target.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            target.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            // Fake original shape: answers "".
            var il = target.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, "");
            il.Emit(OpCodes.Ret);
            util.Methods.Add(target);
            module.Types.Add(util);
            asm.Write(tempOut);
        }
    }

    [Fact]
    public void Redirect_Synthetic_VirtualPath_Answers_VfsLayout()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"ShellSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tempOut);
            ModuleDefinition module = read.MainModule;
            AssemblyRewriter.RedirectShellVirtualPaths(module);

            var target = module.GetType("PaintDotNet.SystemLayer.ShellUtil").Methods
                .First(m => m.Name == "GetVirtualPath");
            var strings = target.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Ldstr)
                .Select(i => (string)i.Operand)
                .ToList();
            Assert.Contains("/vfs/pictures", strings);
            Assert.Contains("/vfs/documents", strings);
            Assert.Contains("/vfs/local-app-data", strings);
            Assert.DoesNotContain("", strings);
            var calls = target.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Call)
                .Select(i => ((MethodReference)i.Operand).Name)
                .ToList();
            Assert.Contains("CreateDirectory", calls);
            Assert.DoesNotContain("GetCSIDLPath", calls);
            Assert.Equal(OpCodes.Ret, target.Body.Instructions.Last().OpCode);
            Assert.Single(target.Body.ExceptionHandlers);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
