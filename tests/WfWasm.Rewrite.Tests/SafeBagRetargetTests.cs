using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// P1 finalizer slice: SafeProducerConsumerCollection.Finalize probes IsEmpty
// on a bag whose framework ThreadLocal is already disposed -> unhandled
// ObjectDisposedException on the finalizer thread kills the host. The rule
// retargets that single probe to the fail-closed CpuPaint.SafeBagIsEmpty
// helper. Hermetic: synthetic Collections-shaped module, no vendor binaries;
// the real CpuPaint helper is imported from the built PdnCpu dll.
public sealed class SafeBagRetargetTests
{
    private static string CpuPaintDll()
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.PdnCpu", "bin", "Debug", "net9.0", "WfWasm.PdnCpu.dll"));
        Assert.True(File.Exists(dll), "dependency not built: " + dll);
        return dll;
    }

    private static MethodReference IsEmptyRef(
        ModuleDefinition module, string ns, string name, string arity)
    {
        var dt = new TypeReference(ns, name + arity, module, module.TypeSystem.CoreLibrary);
        var mr = new MethodReference("get_IsEmpty", module.TypeSystem.Boolean, dt) { HasThis = true };
        return mr;
    }

    private static ModuleDefinition SynthModule(
        string asmName, bool withBag, bool withDecoys, string tempOut)
    {
        using (var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(asmName, new Version(1, 0, 0, 0)), asmName, ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var iface = new TypeDefinition(
                "PaintDotNet.Collections", "IProducerConsumerCollection2`1",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
                module.TypeSystem.Object);
            iface.GenericParameters.Add(new GenericParameter("T", iface));
            var getter = new MethodDefinition("get_IsEmpty",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Abstract
                    | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig
                    | Mono.Cecil.MethodAttributes.NewSlot,
                module.TypeSystem.Boolean);
            iface.Methods.Add(getter);
            module.Types.Add(iface);

            var other = new TypeDefinition("OtherNS", "IOther",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
                module.TypeSystem.Object);
            var otherGetter = new MethodDefinition("get_IsEmpty",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Abstract
                    | Mono.Cecil.MethodAttributes.Virtual | Mono.Cecil.MethodAttributes.HideBySig
                    | Mono.Cecil.MethodAttributes.NewSlot,
                module.TypeSystem.Boolean);
            other.Methods.Add(otherGetter);
            module.Types.Add(other);

            if (withBag)
            {
                var bag = new TypeDefinition("PaintDotNet.Collections", "SafeProducerConsumerCollection`2",
                    TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
                bag.GenericParameters.Add(new GenericParameter("T", bag));
                bag.GenericParameters.Add(new GenericParameter("TQueue", bag));
                var fin = new MethodDefinition("Finalize",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Virtual,
                    module.TypeSystem.Void);
                bag.Methods.Add(fin);
                module.Types.Add(bag);
                var il = fin.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, IsEmptyRef(module,
                    "PaintDotNet.Collections", "IProducerConsumerCollection2", "`1"));
                il.Emit(OpCodes.Pop);
                if (withDecoys)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Callvirt, IsEmptyRef(module, "OtherNS", "IOther", ""));
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldarg_0);
                    var toStr = new MethodReference("ToString", module.TypeSystem.String, module.TypeSystem.Object)
                    {
                        HasThis = true
                    };
                    il.Emit(OpCodes.Callvirt, toStr);
                    il.Emit(OpCodes.Pop);
                }
                il.Emit(OpCodes.Ret);
            }
            asm.Write(tempOut);
        }
        return AssemblyDefinition.ReadAssembly(tempOut).MainModule;
    }

    [Fact]
    public void SafeBag_Finalize_Probe_Retargeted_To_FailClosed_Helper()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"SafeBagSynth_{Guid.NewGuid()}.dll");
        try
        {
            using (var module = SynthModule("PaintDotNet.Collections", true, true, tempOut))
            {
                AssemblyRewriter.EnsureSafeBagFinalizeTolerant(module, CpuPaintDll());

                var calls = module.Types.SelectMany(t => t.Methods).Where(x => x.HasBody)
                    .SelectMany(x => x.Body.Instructions)
                    .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                        && i.Operand is MethodReference)
                    .Select(i => (Op: i.OpCode, Mr: (MethodReference)i.Operand))
                    .ToList();
                var swapped = calls.Where(c => c.Mr.Name == "SafeBagIsEmpty").ToList();
                Assert.Single(swapped);
                Assert.Equal(OpCodes.Call, swapped[0].Op);
                Assert.Equal("WfWasm.PdnCpu.CpuPaint", swapped[0].Mr.DeclaringType.FullName);
                // The torn-bag probe is gone; unrelated probes stay put.
                Assert.DoesNotContain(calls, c => c.Mr.Name == "get_IsEmpty"
                    && c.Mr.DeclaringType.FullName.StartsWith("PaintDotNet.Collections.IProducerConsumerCollection2",
                        StringComparison.Ordinal));
                Assert.Contains(calls, c => c.Mr.Name == "get_IsEmpty"
                    && c.Mr.DeclaringType.FullName == "OtherNS.IOther");
                Assert.Contains(calls, c => c.Mr.Name == "ToString");
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void SafeBag_Missing_Bag_Type_Fails_Loud()
    {
        // The rule refuses foreign assemblies loudly instead of silently
        // no-op'ing (the driver gates by assembly name; a direct call on a
        // module without the bag shape must still fail closed).
        string tempOut = Path.Combine(Path.GetTempPath(), $"SafeBagSynth_{Guid.NewGuid()}.dll");
        try
        {
            using (var module = SynthModule("SomethingElse", false, false, tempOut))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    AssemblyRewriter.EnsureSafeBagFinalizeTolerant(module, CpuPaintDll()));
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
