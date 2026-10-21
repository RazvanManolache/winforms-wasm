using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class NativeLoaderRedirectTests
{
    [Fact]
    public void Redirect_Synthetic_Loader_Calls_Move_To_Null_Handle_Stubs()
    {
        // Hermetic: fabricated calls to the CsWinRT kernel32 loader
        // forwarders must move to the fork null-handle stubs (headless
        // MsftEdit absence), while wider signatures (PCWSTR) and the
        // shims' own desktop passthrough stay exactly put.
        using var asm = BuildSynthetic().Asm;
        var module = asm.MainModule;
        AssemblyRewriter.RetargetNativeLoaderCalls(module);

        var host = module.GetType("NativeSynth.Host");
        Assert.NotNull(host);
        var load = host.Methods.First(m => m.Name == "Load");
        var loadCall = load.Body.Instructions.First(i => i.OpCode == OpCodes.Call);
        var loadTarget = Assert.IsAssignableFrom<MethodReference>(loadCall.Operand);
        Assert.Equal("LoadLibraryExNull", loadTarget.Name);
        Assert.Equal("System.Windows.Forms.WasmNativeLibrary", loadTarget.DeclaringType.FullName);
        Assert.Equal("WfWasm.WinForms", loadTarget.DeclaringType.Scope.Name);

        var get = host.Methods.First(m => m.Name == "Get");
        var getCall = get.Body.Instructions.First(i => i.OpCode == OpCodes.Call);
        var getTarget = Assert.IsAssignableFrom<MethodReference>(getCall.Operand);
        Assert.Equal("GetModuleHandleNull", getTarget.Name);
        Assert.Equal("System.Windows.Forms.WasmNativeLibrary", getTarget.DeclaringType.FullName);

        // Callvirt sites convert to Call on the stub.
        var virt = host.Methods.First(m => m.Name == "LoadVirt");
        var virtCall = virt.Body.Instructions.First(i =>
            i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt);
        Assert.Equal(OpCodes.Call, virtCall.OpCode);
        Assert.Equal("LoadLibraryExNull",
            Assert.IsAssignableFrom<MethodReference>(virtCall.Operand).Name);

        // Wider (non-string) overloads keep their imports and stay visible.
        var wide = host.Methods.First(m => m.Name == "GetWide");
        var wideCall = wide.Body.Instructions.First(i => i.OpCode == OpCodes.Call);
        var wideTarget = Assert.IsAssignableFrom<MethodReference>(wideCall.Operand);
        Assert.Equal("GetModuleHandle", wideTarget.Name);
        Assert.Equal("Windows.Win32.PInvoke", wideTarget.DeclaringType.FullName);

        // The shims' own desktop passthrough must not recurse.
        var shim = module.GetType("System.Windows.Forms.WasmNativeLibrary");
        Assert.NotNull(shim);
        var self = shim.Methods.First(m => m.Name == "LoadLibraryExNull");
        var selfCall = self.Body.Instructions.First(i => i.OpCode == OpCodes.Call);
        var selfTarget = Assert.IsAssignableFrom<MethodReference>(selfCall.Operand);
        Assert.Equal("LoadLibraryEx", selfTarget.Name);
        Assert.Equal("Windows.Win32.PInvoke", selfTarget.DeclaringType.FullName);
    }

    private static (AssemblyDefinition Asm, ModuleDefinition Module) BuildSynthetic()
    {
        var asmName = new AssemblyNameDefinition("NativeSynth", new Version(1, 0, 0, 0));
        var asm = AssemblyDefinition.CreateAssembly(asmName, "NativeSynth", ModuleKind.Dll);
        var module = asm.MainModule;
        // Phantom interop scope: the rule matches by type name, so no real
        // Windows.Win32 assembly is needed (and none may load here).
        var win32 = new AssemblyNameReference("Windows.Win32", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(win32);
        var pinvoke = new TypeReference("Windows.Win32", "PInvoke", module, win32);
        var hinstance = new TypeReference("Windows.Win32.Foundation", "HINSTANCE", module, win32);
        var flags = new TypeReference("Windows.Win32.System.LibraryLoader", "LOAD_LIBRARY_FLAGS", module, win32);
        var pcwstr = new TypeReference("Windows.Win32.Foundation", "PCWSTR", module, win32);

        MethodReference LoadLib() => new MethodReference("LoadLibraryEx", hinstance, pinvoke)
        {
            HasThis = false,
            Parameters =
            {
                new ParameterDefinition(module.TypeSystem.String),
                new ParameterDefinition(flags),
            },
        };
        MethodReference GetHandle() => new MethodReference("GetModuleHandle", hinstance, pinvoke)
        {
            HasThis = false,
            Parameters = { new ParameterDefinition(module.TypeSystem.String) },
        };
        MethodReference GetHandleWide() => new MethodReference("GetModuleHandle", hinstance, pinvoke)
        {
            HasThis = false,
            Parameters = { new ParameterDefinition(pcwstr) },
        };

        var host = new TypeDefinition("NativeSynth", "Host",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        AddCaller(host, "Load", OpCodes.Call, LoadLib(), loadFlags: true);
        AddCaller(host, "Get", OpCodes.Call, GetHandle(), loadFlags: false);
        AddCaller(host, "LoadVirt", OpCodes.Callvirt, LoadLib(), loadFlags: true);
        AddCaller(host, "GetWide", OpCodes.Call, GetHandleWide(), loadFlags: false);
        module.Types.Add(host);

        var shim = new TypeDefinition("System.Windows.Forms", "WasmNativeLibrary",
            TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        AddCaller(shim, "LoadLibraryExNull", OpCodes.Call, LoadLib(), loadFlags: true);
        module.Types.Add(shim);
        return (asm, module);

        void AddCaller(TypeDefinition owner, string name, OpCode callOp,
            MethodReference target, bool loadFlags)
        {
            var m = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
            var il = m.Body.GetILProcessor();
            il.Emit(OpCodes.Ldstr, "MsftEdit.DLL");
            if (loadFlags) il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(callOp, target);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            owner.Methods.Add(m);
        }
    }
}
