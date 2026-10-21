using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectLastSaveConfigToken: FileType.GetLastSaveConfigToken is
// answered by the default-token factory (persisted per-filetype tokens need
// the user settings store, unavailable headless), preserving per-type
// defaults through the virtual OnCreateDefaultSaveConfigToken dispatch.
public sealed class SaveConfigTokenRedirectTests
{
    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("TokenSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "TokenSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var token = new TypeDefinition("PaintDotNet", "SaveConfigToken",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(token);

            var ft = new TypeDefinition("PaintDotNet", "FileType",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var factory = new MethodDefinition("CreateDefaultSaveConfigToken",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                token);
            factory.Body.GetILProcessor().Emit(OpCodes.Ret);
            ft.Methods.Add(factory);
            var target = new MethodDefinition("GetLastSaveConfigToken",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                | Mono.Cecil.MethodAttributes.Virtual,
                token);
            // Fake original shape: throws InvalidOperationException.
            var il = target.Body.GetILProcessor();
            il.Emit(OpCodes.Newobj, module.ImportReference(typeof(InvalidOperationException)
                .GetConstructor(Type.EmptyTypes)!));
            il.Emit(OpCodes.Throw);
            ft.Methods.Add(target);
            module.Types.Add(ft);
            asm.Write(tempOut);
        }
    }

    [Fact]
    public void Redirect_Synthetic_TokenGetter_Answers_DefaultFactory()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"TokenSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tempOut);
            ModuleDefinition module = read.MainModule;
            AssemblyRewriter.RedirectLastSaveConfigToken(module);

            var target = module.GetType("PaintDotNet.FileType").Methods
                .First(m => m.Name == "GetLastSaveConfigToken");
            var instrs = target.Body.Instructions;
            Assert.Equal(3, instrs.Count);
            Assert.Equal(OpCodes.Ldarg_0, instrs[0].OpCode);
            Assert.Equal(OpCodes.Call, instrs[1].OpCode);
            Assert.Equal("CreateDefaultSaveConfigToken",
                ((MethodReference)instrs[1].Operand).Name);
            Assert.Equal(OpCodes.Ret, instrs[2].OpCode);
            Assert.Empty(target.Body.ExceptionHandlers);
            Assert.Empty(target.Body.Variables);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
