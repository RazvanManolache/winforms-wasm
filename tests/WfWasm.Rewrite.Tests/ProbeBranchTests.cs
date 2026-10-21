using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins InstrumentProbes against its historic failure modes. The EXIT pair
// inserted before each ret grows the distance between existing short
// branches and their targets; a branch sitting at the +/-127 edge then
// overflows the short form on write (InvalidProgram, or worse a silent
// mis-jump). InstrumentProbes now expands short branches up front
// (SimplifyMacros); these tests execute probed bodies to prove it.
public sealed class ProbeBranchTests
{
    private static string BuildShortBranch(string tempOut)
    {
        // int Work(int x): brtrue.s at exactly offset 127 over the first ret.
        // Layout after brtrue.s: ldc.i4.1(1) ret(1) 125x nop, TARGET ldc.i4.2.
        // The EXIT pair (ldstr+call = 10 bytes) before ret#1 pushes TARGET to
        // 137: without expansion the write overflows the short form.
        var asmName = new AssemblyNameDefinition("ProbeBranch", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "ProbeBranch", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var box = new TypeDefinition("ProbeBranch", "Box",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var target = new MethodDefinition("Work",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Int32);
            target.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            var il = target.Body.GetILProcessor();
            var onTrue = il.Create(OpCodes.Ldc_I4_2);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brtrue_S, onTrue));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));
            for (int i = 0; i < 125; i++)
                il.Append(il.Create(OpCodes.Nop));
            il.Append(onTrue);
            il.Append(il.Create(OpCodes.Ret));
            box.Methods.Add(target);
            module.Types.Add(box);
            asm.Write(tempOut);
        }
        return tempOut;
    }

    private static string BuildGuarded(string tempOut)
    {
        // int Guarded(int x): try/catch with leaves and a single ret.
        // Negative x throws inside the try and lands 7 via the catch.
        var asmName = new AssemblyNameDefinition("ProbeGuarded", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "ProbeGuarded", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var box = new TypeDefinition("ProbeGuarded", "Box",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var target = new MethodDefinition("Guarded",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Int32);
            target.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            target.Body.InitLocals = true;
            target.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
            var il = target.Body.GetILProcessor();
            var noThrow = il.Create(OpCodes.Ldarg_0);
            var end = il.Create(OpCodes.Ldloc_0);
            var tryStart = il.Create(OpCodes.Ldarg_0);
            il.Append(tryStart);
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Bge_S, noThrow));
            il.Append(il.Create(OpCodes.Newobj, module.ImportReference(
                typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!)));
            il.Append(il.Create(OpCodes.Throw));
            il.Append(noThrow);
            il.Append(il.Create(OpCodes.Stloc_0));
            var leave1 = il.Create(OpCodes.Leave_S, end);
            il.Append(leave1);
            var handlerStart = il.Create(OpCodes.Pop);
            il.Append(handlerStart);
            il.Append(il.Create(OpCodes.Ldc_I4_7));
            il.Append(il.Create(OpCodes.Stloc_0));
            var leave2 = il.Create(OpCodes.Leave_S, end);
            il.Append(leave2);
            var handlerEnd = il.Create(OpCodes.Nop);
            il.Append(handlerEnd);
            il.Append(end);
            il.Append(il.Create(OpCodes.Ret));
            target.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                CatchType = module.ImportReference(typeof(InvalidOperationException)),
                TryStart = tryStart,
                TryEnd = handlerStart,
                HandlerStart = handlerStart,
                HandlerEnd = handlerEnd,
            });
            box.Methods.Add(target);
            module.Types.Add(box);
            asm.Write(tempOut);
        }
        return tempOut;
    }

    private static object? InvokeProbed(string src, string typeName, string methodName, params object?[] args)
    {
        string dst = Path.Combine(Path.GetTempPath(), $"ProbeOut_{Guid.NewGuid()}.dll");
        try
        {
            int probed = AssemblyRewriter.InstrumentProbes(src, dst, [$"{typeName}::{methodName}"]);
            Assert.Equal(1, probed);
            var alc = new AssemblyLoadContext($"Probe_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(dst);
                var type = loaded.GetType(typeName);
                Assert.NotNull(type);
                var target = type.GetMethod(methodName);
                Assert.NotNull(target);
                RuntimeHelpers.PrepareMethod(target.MethodHandle);
                return target.Invoke(null, args);
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

    [Fact]
    public void Probe_ShortBranch_OverRet_Stays_Correct()
    {
        string src = Path.Combine(Path.GetTempPath(), $"ProbeBranch_{Guid.NewGuid()}.dll");
        BuildShortBranch(src);
        // A silently truncated branch would return 2 for x == 0; an
        // unverifiable body would die in PrepareMethod instead.
        Assert.Equal(1, InvokeProbed(src, "ProbeBranch.Box", "Work", 0));
        string src2 = Path.Combine(Path.GetTempPath(), $"ProbeBranch_{Guid.NewGuid()}.dll");
        BuildShortBranch(src2);
        Assert.Equal(2, InvokeProbed(src2, "ProbeBranch.Box", "Work", 7));
    }

    [Fact]
    public void Probe_HandlerMethod_Stays_Correct()
    {
        string src = Path.Combine(Path.GetTempPath(), $"ProbeGuarded_{Guid.NewGuid()}.dll");
        BuildGuarded(src);
        Assert.Equal(5, InvokeProbed(src, "ProbeGuarded.Box", "Guarded", 5));
        string src2 = Path.Combine(Path.GetTempPath(), $"ProbeGuarded_{Guid.NewGuid()}.dll");
        BuildGuarded(src2);
        Assert.Equal(7, InvokeProbed(src2, "ProbeGuarded.Box", "Guarded", -3));
    }
}
