using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins InstrumentProbes verifiability: a probed method must still JIT and
// execute (ENTER/EXIT print, balanced stack). The verb bumps MaxStackSize
// for its inserted ldstr/call pair; PrepareMethod+Invoke here prove the
// breadcrumbs observe without breaking the method.
public sealed class ProbeMaxStackTests
{
    private static string BuildTight(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("ProbeTight", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "ProbeTight", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var box = new TypeDefinition("ProbeTight", "Box",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var target = new MethodDefinition("Work",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            var il = target.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            box.Methods.Add(target);
            module.Types.Add(box);
            asm.Write(tempOut);
        }
        return tempOut;
    }

    [Fact]
    public void Probe_Tight_Method_Stays_Jittable()
    {
        string src = Path.Combine(Path.GetTempPath(), $"ProbeTight_{Guid.NewGuid()}.dll");
        string dst = Path.Combine(Path.GetTempPath(), $"ProbeTightOut_{Guid.NewGuid()}.dll");
        BuildTight(src);
        try
        {
            int probed = AssemblyRewriter.InstrumentProbes(src, dst,
                ["ProbeTight.Box::Work"]);
            Assert.Equal(1, probed);
            var alc = new AssemblyLoadContext($"ProbeTight_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(dst);
                var box = loaded.GetType("ProbeTight.Box");
                Assert.NotNull(box);
                var target = box.GetMethod("Work");
                Assert.NotNull(target);
                // InvalidProgramException here means the probe broke the
                // method it was meant to observe. Invoking further proves
                // the breadcrumbs execute (ENTER/EXIT print, no stack damage).
                RuntimeHelpers.PrepareMethod(target.MethodHandle);
                target.Invoke(null, null);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(dst); } catch { }
        }
    }
}
