using WfWasm.Rewrite;

public sealed class ResourceShimRewriteTests
{
    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    [Fact]
    public void Rewrite_ResourceManager_GetObject_Redirects_To_Fork_Shim()
    {
        // ResourceManager.GetObject retargets to the fork's WasmResourceShim
        // (not Drawing.ResourceHelper): designer ".ImageStream" entries
        // hydrate into real ImageListStreamers there, everything else flows
        // through. The fixture carries no Drawing reference, so this also
        // locks that the redirect no longer needs one (formsScope is
        // established unconditionally).
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows",
            "WfWasm.Rewrite.Fixture.dll"));
        Assert.True(File.Exists(fixture), $"fixture not built: {fixture}");
        string input = Path.Combine(Path.GetTempPath(), "resourceshim.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(fixture));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var calls = new Mono.Cecil.TypeDefinition(
                "ResourceProbe", "Calls",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(calls);
            var rmRef = new Mono.Cecil.TypeReference(
                "System.Resources", "ResourceManager", module, module.TypeSystem.Object.Scope);
            var getObject = new Mono.Cecil.MethodReference("GetObject", module.TypeSystem.Object, rmRef)
            {
                HasThis = true,
            };
            getObject.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
            var caller = new Mono.Cecil.MethodDefinition(
                "GetIt",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Object);
            caller.Parameters.Add(new Mono.Cecil.ParameterDefinition(rmRef));
            var il = caller.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "imageList2.ImageStream");
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, getObject);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            calls.Methods.Add(caller);
            asm.Write(input);
        }

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
        string tempOut = Path.Combine(Path.GetTempPath(), $"ResourceShim_{Guid.NewGuid()}.dll");
        try
        {
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var caller = asm.MainModule.GetTypes()
                .First(t => t.FullName == "ResourceProbe.Calls").Methods
                .First(m => m.Name == "GetIt");
            var call = caller.Body.Instructions
                .First(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "SafeGetObject");
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, call.OpCode);
            var target = (Mono.Cecil.MethodReference)call.Operand;
            Assert.Equal("System.Windows.Forms.WasmResourceShim", target.DeclaringType.FullName);
            Assert.Equal(2, target.Parameters.Count);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            try { if (File.Exists(input)) File.Delete(input); } catch { }
        }
    }
}
