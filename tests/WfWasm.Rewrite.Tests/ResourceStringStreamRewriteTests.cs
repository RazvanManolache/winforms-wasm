using WfWasm.Rewrite;

// ResourceManager/ComponentResourceManager.GetString+GetStream retarget to
// the fork shim's throw-free SafeGetString/SafeGetStream (raw BCL reads of
// v1-header streams that name DeserializingResourceReader threw NSE with no
// managed catch on the ctor path: silent OOB trap, calendar+diagram dead).
public sealed class ResourceStringStreamRewriteTests
{
    private static string ShimDll(string project, string name)
    {
        string dll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", project, "bin", "Debug", "net9.0", name + ".dll"));
        Assert.True(File.Exists(dll), $"shim not built: {dll}");
        return dll;
    }

    private static string BuildCaller(
        string methodName, string returnTypeName, string returnTypeNs, int paramCount)
    {
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows",
            "WfWasm.Rewrite.Fixture.dll"));
        Assert.True(File.Exists(fixture), $"fixture not built: {fixture}");
        string input = Path.Combine(Path.GetTempPath(), $"resstring_{Guid.NewGuid()}.input.dll");
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
            Mono.Cecil.TypeReference returnRef = returnTypeNs == "System"
                ? module.TypeSystem.String
                : new Mono.Cecil.TypeReference(
                    returnTypeNs, returnTypeName, module, module.TypeSystem.Object.Scope);
            var target = new Mono.Cecil.MethodReference(methodName, returnRef, rmRef)
            {
                HasThis = true,
            };
            target.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
            if (paramCount == 2)
                target.Parameters.Add(new Mono.Cecil.ParameterDefinition(
                    new Mono.Cecil.TypeReference(
                        "System.Globalization", "CultureInfo", module, module.TypeSystem.Object.Scope)));
            var caller = new Mono.Cecil.MethodDefinition(
                "GetIt",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                returnRef);
            caller.Parameters.Add(new Mono.Cecil.ParameterDefinition(rmRef));
            var il = caller.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "Some.Key");
            if (paramCount == 2)
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, target);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            calls.Methods.Add(caller);
            asm.Write(input);
        }

        return input;
    }

    private static Mono.Cecil.MethodReference RewriteAndFindCall(string input, string expectedName)
    {
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
        string tempOut = Path.Combine(Path.GetTempPath(), $"ResourceString_{Guid.NewGuid()}.dll");
        try
        {
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var caller = asm.MainModule.GetTypes()
                .First(t => t.FullName == "ResourceProbe.Calls").Methods
                .First(m => m.Name == "GetIt");
            var call = caller.Body.Instructions
                .First(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == expectedName);
            Assert.Equal(Mono.Cecil.Cil.OpCodes.Call, call.OpCode);
            return (Mono.Cecil.MethodReference)call.Operand;
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            try { if (File.Exists(input)) File.Delete(input); } catch { }
        }
    }

    [Fact]
    public void Rewrite_ResourceManager_GetString_Redirects_To_Fork_Shim()
    {
        string input = BuildCaller("GetString", "String", "System", 1);
        var target = RewriteAndFindCall(input, "SafeGetString");
        Assert.Equal("System.Windows.Forms.WasmResourceShim", target.DeclaringType.FullName);
        Assert.Equal(2, target.Parameters.Count);
    }

    [Fact]
    public void Rewrite_ResourceManager_GetStream_Culture_Redirects_To_Fork_Shim()
    {
        string input = BuildCaller("GetStream", "Stream", "System.IO", 2);
        var target = RewriteAndFindCall(input, "SafeGetStream");
        Assert.Equal("System.Windows.Forms.WasmResourceShim", target.DeclaringType.FullName);
        Assert.Equal(3, target.Parameters.Count);
    }

    [Fact]
    public void Rewrite_ResourceManager_GetResourceSet_Redirects_To_Fork_Shim()
    {
        string input = BuildResourceSetCaller();
        var target = RewriteAndFindCall(input, "SafeGetResourceSet");
        Assert.Equal("System.Windows.Forms.WasmResourceShim", target.DeclaringType.FullName);
        Assert.Equal(4, target.Parameters.Count);
    }

    [Fact]
    public void Rewrite_InitializeComponent_Gains_Ctor_Guard()
    {
        string input = BuildGuardFixture();
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
        string tempOut = Path.Combine(Path.GetTempPath(), $"CtorGuard_{Guid.NewGuid()}.dll");
        try
        {
            var typeMap = GapAnalyzer.AutoMap(input, map);
            AssemblyRewriter.Rewrite(input, tempOut, map, typeMap);
            using var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(tempOut);
            var victim = asm.MainModule.GetTypes().First(t => t.FullName == "GuardProbe.Victim");
            var init = victim.Methods.First(m => m.Name == "InitializeComponent");
            Assert.Single(init.Body.ExceptionHandlers);
            var handler = init.Body.ExceptionHandlers[0];
            Assert.Equal(Mono.Cecil.Cil.ExceptionHandlerType.Catch, handler.HandlerType);
            Assert.Equal("System.Exception", handler.CatchType.FullName);
            Assert.Contains(init.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "Stash"
                && mr.DeclaringType.FullName == "System.Windows.Forms.WasmCtorGuard");
            var other = victim.Methods.First(m => m.Name == "Other");
            Assert.Empty(other.Body.ExceptionHandlers);
            var ctor = victim.Methods.First(m => m.Name == ".ctor");
            Assert.Single(ctor.Body.ExceptionHandlers);
            Assert.Contains(ctor.Body.Instructions, i =>
                i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "Stash"
                && mr.DeclaringType.FullName == "System.Windows.Forms.WasmCtorGuard");
            // No raw ret may sit inside a guard try region: it is
            // unverifiable (desktop InvalidProgramException, native
            // unwind trap in WASM); every in-try return must leave to
            // the shared tail.
            foreach (var guarded in new[] { init, ctor })
            {
                var eh = Assert.Single(guarded.Body.ExceptionHandlers);
                foreach (var ins in guarded.Body.Instructions)
                {
                    if (ins.OpCode != Mono.Cecil.Cil.OpCodes.Ret)
                        continue;
                    bool covered = eh.TryStart is not null && eh.TryEnd is not null
                        && ins.Offset >= eh.TryStart.Offset
                        && ins.Offset < eh.TryEnd.Offset;
                    Assert.False(covered,
                        $"raw ret at 0x{ins.Offset:X4} inside guard try of {guarded.Name}");
                }
            }
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            try { if (File.Exists(input)) File.Delete(input); } catch { }
        }
    }

    [Fact]
    public void Rewrite_Guarded_Ctor_Executes_Swallows_And_Stashes()
    {
        // End-to-end pin for the guard emission: a guarded ctor must JIT
        // and run (degraded), not throw InvalidProgramException. The
        // fixture Victim ctor throws "ctor-boom" past its base call, so
        // construction must succeed and stash that exact throw.
        string input = BuildGuardFixture();
        string tempOut = Path.Combine(Path.GetTempPath(), $"guardexec_{Guid.NewGuid()}.dll");
        try
        {
            using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(input))
            {
                AssemblyRewriter.GuardInitializeComponents(asm.MainModule);
                asm.Write(tempOut);
            }
            // Same-identity loads bind the first copy: isolate execution
            // in a collectible load context.
            var alc = new System.Runtime.Loader.AssemblyLoadContext(
                "guardexec", isCollectible: true);
            try
            {
                alc.Resolving += (context, name) =>
                {
                    string? want = name.Name;
                    var already = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, want,
                            StringComparison.OrdinalIgnoreCase));
                    if (already is not null)
                        return already;
                    foreach (string dir in new[]
                        { AppContext.BaseDirectory, TestPaths.Repo(@".artifacts\runtime") })
                    {
                        string cand = Path.Combine(dir, want + ".dll");
                        if (File.Exists(cand))
                        {
                            try { return context.LoadFromAssemblyPath(cand); }
                            catch { }
                        }
                    }
                    return null;
                };
                var outAsm = alc.LoadFromAssemblyPath(Path.GetFullPath(tempOut));
                Type? victim = outAsm.GetType("GuardProbe.Victim");
                Assert.NotNull(victim);
                Exception? thrown = null;
                object? instance = null;
                try
                {
                    instance = Activator.CreateInstance(victim!);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                    while (thrown.InnerException is not null)
                        thrown = thrown.InnerException;
                }
                Assert.Null(thrown);
                Assert.NotNull(instance);
                var guard = alc.Assemblies
                    .Select(a => a.GetType("System.Windows.Forms.WasmCtorGuard"))
                    .FirstOrDefault(t => t is not null);
                Assert.NotNull(guard);
                object? stashed = guard!.GetMethod("TakeStashed",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(null, null);
                Assert.Equal("ctor-boom", (stashed as Exception)?.Message);
            }
            finally
            {
                try { alc.Unload(); } catch { }
            }
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
            try { if (File.Exists(input)) File.Delete(input); } catch { }
        }
    }

    private static string BuildGuardFixture()
    {
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows",
            "WfWasm.Rewrite.Fixture.dll"));
        Assert.True(File.Exists(fixture), $"fixture not built: {fixture}");
        string input = Path.Combine(Path.GetTempPath(), $"ctorguard_{Guid.NewGuid()}.input.dll");
        using var image = new MemoryStream(File.ReadAllBytes(fixture));
        using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(image))
        {
            var module = asm.MainModule;
            var victim = new Mono.Cecil.TypeDefinition(
                "GuardProbe", "Victim",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(victim);
            var init = new Mono.Cecil.MethodDefinition(
                "InitializeComponent",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            var iil = init.Body.GetILProcessor();
            iil.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "boom");
            iil.Emit(Mono.Cecil.Cil.OpCodes.Newobj, module.ImportReference(
                typeof(Exception).GetConstructor(new[] { typeof(string) })!));
            iil.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            victim.Methods.Add(init);
            var other = new Mono.Cecil.MethodDefinition(
                "Other",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            other.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
            victim.Methods.Add(other);
            var ctor = new Mono.Cecil.MethodDefinition(
                ".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                    | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var cil = ctor.Body.GetILProcessor();
            cil.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            cil.Emit(Mono.Cecil.Cil.OpCodes.Call, module.ImportReference(
                typeof(object).GetConstructor(Type.EmptyTypes)!));
            cil.Emit(Mono.Cecil.Cil.OpCodes.Ldstr, "ctor-boom");
            cil.Emit(Mono.Cecil.Cil.OpCodes.Newobj, module.ImportReference(
                typeof(Exception).GetConstructor(new[] { typeof(string) })!));
            cil.Emit(Mono.Cecil.Cil.OpCodes.Throw);
            victim.Methods.Add(ctor);
            asm.Write(input);
        }

        return input;
    }

    private static string BuildResourceSetCaller()
    {
        string fixture = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "WfWasm.Rewrite.Fixture", "bin", "Debug", "net9.0-windows",
            "WfWasm.Rewrite.Fixture.dll"));
        Assert.True(File.Exists(fixture), $"fixture not built: {fixture}");
        string input = Path.Combine(Path.GetTempPath(), $"resresourceset_{Guid.NewGuid()}.input.dll");
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
            var rsRef = new Mono.Cecil.TypeReference(
                "System.Resources", "ResourceSet", module, module.TypeSystem.Object.Scope);
            var cultureRef = new Mono.Cecil.TypeReference(
                "System.Globalization", "CultureInfo", module, module.TypeSystem.Object.Scope);
            var target = new Mono.Cecil.MethodReference("GetResourceSet", rsRef, rmRef)
            {
                HasThis = true,
            };
            target.Parameters.Add(new Mono.Cecil.ParameterDefinition(cultureRef));
            target.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Boolean));
            target.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.Boolean));
            var caller = new Mono.Cecil.MethodDefinition(
                "GetIt",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                rsRef);
            caller.Parameters.Add(new Mono.Cecil.ParameterDefinition(rmRef));
            var il = caller.Body.GetILProcessor();
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldnull);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_1);
            il.Emit(Mono.Cecil.Cil.OpCodes.Callvirt, target);
            il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
            calls.Methods.Add(caller);
            asm.Write(input);
        }

        return input;
    }
}
