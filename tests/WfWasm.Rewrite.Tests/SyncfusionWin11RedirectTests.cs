using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectSyncfusionWin11Check: SfForm.CheckIsWindows11 is answered
// false (no DWM/Mica headless; the registry build-number probe throws on
// int.Parse(null) before any Syncfusion demo form exists).
public sealed class SyncfusionWin11RedirectTests
{
    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("SfSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "SfSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var form = new TypeDefinition("Syncfusion.WinForms.Controls", "SfForm",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var check = new MethodDefinition("CheckIsWindows11",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Boolean);
            // Fake original shape: registry probe + Parse.
            var il = check.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, module.ImportReference(
                typeof(int).GetMethod("Parse", new[] { typeof(string) })!));
            il.Emit(OpCodes.Ldc_I4, 22000);
            il.Emit(OpCodes.Clt);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ret);
            form.Methods.Add(check);
            module.Types.Add(form);
            asm.Write(tempOut);
        }
    }

    [Fact]
    public void Redirect_Synthetic_Win11Check_Answers_False()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"SfSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tempOut);
            ModuleDefinition module = read.MainModule;
            AssemblyRewriter.RedirectSyncfusionWin11Check(module);

            var target = module.GetType("Syncfusion.WinForms.Controls.SfForm").Methods
                .First(m => m.Name == "CheckIsWindows11");
            var instrs = target.Body.Instructions;
            Assert.Equal(2, instrs.Count);
            Assert.Equal(OpCodes.Ldc_I4_0, instrs[0].OpCode);
            Assert.Equal(OpCodes.Ret, instrs[1].OpCode);
            Assert.Empty(target.Body.ExceptionHandlers);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void Redirect_Real_Win11Check_Answers_False()
    {
        string src = TestPaths.Repo(@"external-apps\syncfusion\syncfusion-calendar\Syncfusion.Shared.Base.dll");
        string tmp = Path.Combine(Path.GetTempPath(), $"SfReal_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            AssemblyRewriter.RedirectSyncfusionWin11Check(read.MainModule);

            var target = read.MainModule.GetType("Syncfusion.WinForms.Controls.SfForm").Methods
                .First(m => m.Name == "CheckIsWindows11");
            var instrs = target.Body.Instructions;
            Assert.Equal(2, instrs.Count);
            Assert.Equal(OpCodes.Ldc_I4_0, instrs[0].OpCode);
            Assert.Equal(OpCodes.Ret, instrs[1].OpCode);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
