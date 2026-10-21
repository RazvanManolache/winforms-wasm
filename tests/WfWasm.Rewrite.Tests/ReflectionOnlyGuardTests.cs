using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class ReflectionOnlyGuardTests
{
    [Fact]
    public void NeutralizeModule_Rewrites_GetterCall_To_PopFalse()
    {
        // Stack-neutral: callvirt instance bool get_ReflectionOnly()
        // pops the assembly and pushes a bool; the rewrite answers
        // false (exact Core behavior: no reflection-only context) as
        // pop + ldc.i4.0.
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("RefOnlyProbe", new Version(1, 0)), "RefOnlyProbe", ModuleKind.Dll);
        var module = asm.MainModule;
        var t = new TypeDefinition("", "P", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(t);
        var m = new MethodDefinition("Probe", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
        m.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(System.Reflection.Assembly))));
        t.Methods.Add(m);
        var il = m.Body.GetILProcessor();
        var getter = new MethodReference("get_ReflectionOnly", module.TypeSystem.Boolean, module.ImportReference(typeof(System.Reflection.Assembly))) { HasThis = true };
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getter);
        il.Emit(OpCodes.Ret);

        int sites = AssemblyRewriter.NeutralizeModuleReflectionOnlyGuards(module);

        Assert.Equal(1, sites);
        var ops = m.Body.Instructions.Select(i => i.OpCode.Name).ToList();
        Assert.Equal(new[] { "ldarg.0", "pop", "ldc.i4.0", "ret" }, ops);
    }

    [Fact]
    public void NeutralizeFile_WithoutSites_LeavesBytesIdentical()
    {
        // Fix 3 scans the whole host _framework: clean files must not
        // churn (Cecil round-trips change MVIDs/timestamps and would
        // invalidate every boot-manifest hash for no reason).
        string input = typeof(ReflectionOnlyGuardTests).Assembly.Location;
        string tempOut = Path.Combine(Path.GetTempPath(), $"RO_Clean_{Guid.NewGuid()}.dll");
        try
        {
            int sites = AssemblyRewriter.NeutralizeReflectionOnlyGuards(input, tempOut);
            Assert.Equal(0, sites);
            Assert.False(File.Exists(tempOut));
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void NeutralizeFile_SyntheticProbe_RoundTrips()
    {
        // File-API coverage without a pristine WindowsBase: no managed
        // copy with a live guard survives in-tree (bin + staged are
        // neutralized by the Fix3 stage; the 2.2MB GAC file is mixed-mode
        // and Cecil cannot write those). A synthetic probe exercises the
        // read/scan/write path end to end.
        string tempIn = Path.Combine(Path.GetTempPath(), $"RO_Probe_{Guid.NewGuid()}.dll");
        string tempOut = Path.Combine(Path.GetTempPath(), $"RO_Probe_{Guid.NewGuid()}.dll");
        try
        {
            using (var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("RefOnlyProbe", new Version(1, 0)), "RefOnlyProbe", ModuleKind.Dll))
            {
                var module = asm.MainModule;
                var t = new TypeDefinition("", "P", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
                module.Types.Add(t);
                var m = new MethodDefinition("Probe", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Boolean);
                m.Parameters.Add(new ParameterDefinition(module.ImportReference(typeof(System.Reflection.Assembly))));
                t.Methods.Add(m);
                var il = m.Body.GetILProcessor();
                var getter = new MethodReference("get_ReflectionOnly", module.TypeSystem.Boolean, module.ImportReference(typeof(System.Reflection.Assembly))) { HasThis = true };
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, getter);
                il.Emit(OpCodes.Ret);
                asm.Write(tempIn);
            }
            int sites = AssemblyRewriter.NeutralizeReflectionOnlyGuards(tempIn, tempOut);
            Assert.Equal(1, sites);
            using var outAsm = AssemblyDefinition.ReadAssembly(tempOut);
            var probe = outAsm.MainModule.GetTypes().SelectMany(t => t.Methods).First(m => m.Name == "Probe");
            var ops = probe.Body.Instructions.Select(i => i.OpCode.Name).ToList();
            Assert.Equal(new[] { "ldarg.0", "pop", "ldc.i4.0", "ret" }, ops);
        }
        finally
        {
            try { if (File.Exists(tempIn)) File.Delete(tempIn); } catch { }
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void StagedWindowsBase_OnLoaded_Neutralized_Keeps_Loop()
    {
        // Fix3 pipeline contract: the staged full WindowsBase (which the
        // WebCIL bundle fingerprints) carries no live ReflectionOnly guard
        // while the GAC-record loop (get_FullName match) stays intact.
        // Fails if the --refonly stage is removed or goes stale.
        string staged = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "WfWasm.CanvasHost", "obj", "Debug", "net9.0", "full-windowsbase", "WindowsBase.dll"));
        if (!File.Exists(staged)) return;
        using var asm = AssemblyDefinition.ReadAssembly(staged);
        var onLoaded = asm.MainModule.GetTypes()
            .Where(t => t.FullName == "MS.Internal.AssemblyHelper")
            .SelectMany(t => t.Methods)
            .First(m => m.Name == "OnLoaded");
        var calls = onLoaded.Body.Instructions
            .Select(i => i.Operand as MethodReference)
            .Where(mr => mr is not null)
            .ToList();
        Assert.DoesNotContain(calls, mr => mr!.Name == "get_ReflectionOnly");
        Assert.Contains(calls, mr => mr!.Name == "get_FullName");
    }
}
