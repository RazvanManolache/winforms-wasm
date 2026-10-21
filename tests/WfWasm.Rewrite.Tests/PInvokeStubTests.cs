using Mono.Cecil;
using WfWasm.Rewrite;
using Xunit;

public sealed class PInvokeStubTests
{
    private static MethodDefinition AddPInvoke(TypeDefinition type, ModuleReference dll, string name, TypeReference ret, params TypeReference[] args)
    {
        var m = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PInvokeImpl, ret);
        foreach (var a in args)
            m.Parameters.Add(new ParameterDefinition(a));
        m.PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvStdCall, name, dll);
        m.IsPInvokeImpl = true;
        type.Methods.Add(m);
        return m;
    }

    private static string Disassemble(MethodDefinition m) =>
        string.Join(";", m.Body.Instructions.Select(i => i.OpCode.Name + (i.Operand is null ? "" : ":" + i.Operand)));

    [Fact]
    public void StubModulePInvokes_LoaderQueries_FailClosed_Others_Keep_Sentinel()
    {
        // LoadLibrary/GetModuleHandle/GetProcAddress answer NULL (desktop
        // failure shape): callers null-check and degrade. The 0x1000
        // sentinel instead reads as a live address and the runtime traps
        // generating the call stub. Unrelated queries keep sentinels.
        // Regression: func[7893] OOB <- GetDelegateForFunctionPointer <-
        // DllWrapper <- ShapedForm..ctor <- ERP boot (silent death).
        using var asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("StubProbe", new Version(1, 0)), "StubProbe", ModuleKind.Dll);
        var module = asm.MainModule;
        var dll = new ModuleReference("kernel32");
        module.ModuleReferences.Add(dll);
        var pinv = new TypeDefinition("", "P", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(pinv);
        var loadLibrary = AddPInvoke(pinv, dll, "LoadLibrary", module.TypeSystem.IntPtr, module.TypeSystem.String);
        var getProcAddress = AddPInvoke(pinv, dll, "GetProcAddress", module.TypeSystem.IntPtr, module.TypeSystem.IntPtr, module.TypeSystem.String);
        var findWindow = AddPInvoke(pinv, dll, "FindWindow", module.TypeSystem.IntPtr, module.TypeSystem.String, module.TypeSystem.String);
        var getTz = AddPInvoke(pinv, dll, "GetDynamicTimeZoneInformation", module.TypeSystem.Int32, module.TypeSystem.IntPtr);
        var getTick = AddPInvoke(pinv, dll, "GetTickCount", module.TypeSystem.Int32);

        AssemblyRewriter.StubModulePInvokes(module);

        Assert.False(loadLibrary.IsPInvokeImpl);
        Assert.Equal("ldc.i4.0;conv.u;ret", Disassemble(loadLibrary));
        Assert.Equal("ldc.i4.0;conv.u;ret", Disassemble(getProcAddress));
        Assert.Equal("ldc.i4:4096;conv.i;ret", Disassemble(findWindow));
        Assert.Equal("ldc.i4.m1;ret", Disassemble(getTz));
        Assert.Equal("ldc.i4.0;ret", Disassemble(getTick));
    }
}
