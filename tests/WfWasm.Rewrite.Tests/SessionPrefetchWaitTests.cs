using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins NeutralizeSessionPrefetchWait (rev83): Paint.NET history-undo dies
// headless because ChunkedReadStream.Dispose drains the prefetch queue
// with a blocking Task.Wait per task, and single-threaded wasm has no
// pump to complete them (PNSE ObjWait "Cannot wait on monitors on this
// runtime" from every Ctrl+Z). The rule swaps the one void Wait call for
// pop (identical stack effect, no branch or handler moves); abandoned
// prefetches never run headless so nothing races the freed buffers.
// Shape assertions pin the single-site swap; the JIT test proves the
// patched body stays verifiable (a mistyped swap would InvalidProgram on
// desktop and trap the wasm interpreter with no managed frame).
public sealed class SessionPrefetchWaitTests
{
    private const string OwnerFullName = "PaintDotNet.AppModel.WriteOnceSessionFile/ChunkedReadStream";

    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("PrefetchSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "PrefetchSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var owner = new TypeDefinition("PaintDotNet.AppModel", "WriteOnceSessionFile",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(owner);
            var nested = new TypeDefinition("", "ChunkedReadStream",
                Mono.Cecil.TypeAttributes.NestedPrivate | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            owner.NestedTypes.Add(nested);
            var dispose = new MethodDefinition("Dispose",
                Mono.Cecil.MethodAttributes.Family | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual,
                module.TypeSystem.Void);
            dispose.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            var dil = dispose.Body.GetILProcessor();
            var completed = module.ImportReference(typeof(Task).GetProperty("CompletedTask")!.GetMethod!);
            var wait = module.ImportReference(typeof(Task).GetMethod("Wait", Type.EmptyTypes)!);
            dil.Emit(OpCodes.Call, completed);
            dil.Emit(OpCodes.Callvirt, wait);
            dil.Emit(OpCodes.Ret);
            nested.Methods.Add(dispose);
            // Hermetic prefetch shape: a LOCAL Task<T>/TaskContinuationOptions
            // pair (framework generic typerefs self-scope in synthetic
            // modules and won't JIT). The rule only matches names/shapes,
            // so local stubs exercise the identical path: one one-arg
            // ContinueWith<string> site becomes the two-arg inline form.
            var tasksNs = "System.Threading.Tasks";
            var tco = new TypeDefinition(tasksNs, "TaskContinuationOptions",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed,
                module.ImportReference(typeof(Enum)));
            tco.Fields.Add(new FieldDefinition("value__",
                Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.SpecialName | Mono.Cecil.FieldAttributes.RTSpecialName,
                module.TypeSystem.Int32));
            module.Types.Add(tco);
            var execSync = new FieldDefinition("ExecuteSynchronously",
                Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static | Mono.Cecil.FieldAttributes.Literal,
                module.ImportReference(tco));
            execSync.Constant = 524288;
            tco.Fields.Add(execSync);
            var taskDef = new TypeDefinition(tasksNs, "Task`1",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var taskT = new GenericParameter("T", taskDef);
            taskDef.GenericParameters.Add(taskT);
            module.Types.Add(taskDef);
            var cont1 = new MethodDefinition("ContinueWith",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Object);
            var cont1New = new GenericParameter("TNew", cont1);
            cont1.GenericParameters.Add(cont1New);
            cont1.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            var c1il = cont1.Body.GetILProcessor();
            c1il.Emit(OpCodes.Ldnull);
            c1il.Emit(OpCodes.Ret);
            taskDef.Methods.Add(cont1);
            var cont2 = new MethodDefinition("ContinueWith",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Object);
            var cont2New = new GenericParameter("TNew", cont2);
            cont2.GenericParameters.Add(cont2New);
            cont2.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            cont2.Parameters.Add(new ParameterDefinition(module.ImportReference(tco)));
            var c2il = cont2.Body.GetILProcessor();
            c2il.Emit(OpCodes.Ldnull);
            c2il.Emit(OpCodes.Ret);
            taskDef.Methods.Add(cont2);
            var prefetch = new MethodDefinition("PrefetchNextChunkImpl",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            var pil = prefetch.Body.GetILProcessor();
            // Explicit instantiated declarer, exactly as Cecil materializes
            // a real Task<T>.ContinueWith call (a same-module ImportReference
            // of the local stub would leave a bare TypeDefinition and dodge
            // the declarer check the rule now enforces).
            var taskStr = new GenericInstanceType(taskDef);
            taskStr.GenericArguments.Add(module.TypeSystem.String);
            var contBase = new MethodReference("ContinueWith", module.TypeSystem.Object, taskStr);
            contBase.HasThis = true;
            contBase.GenericParameters.Add(new GenericParameter("TNew", contBase));
            contBase.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            var contRef = new GenericInstanceMethod(contBase);
            contRef.GenericArguments.Add(module.TypeSystem.String);
            pil.Emit(OpCodes.Ldnull);
            pil.Emit(OpCodes.Ldnull);
            pil.Emit(OpCodes.Callvirt, contRef);
            pil.Emit(OpCodes.Pop);
            pil.Emit(OpCodes.Ret);
            nested.Methods.Add(prefetch);
            asm.Write(tempOut);
        }
    }

    private static string PatchCopy(string src, string tag)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"{tag}_{Guid.NewGuid()}.dll");
        byte[] bytes = File.ReadAllBytes(src);
        string? dir = Path.GetDirectoryName(src);
        using var resolver = new FrameworkResolver(dir, [TestPaths.Repo(@".artifacts\runtime"), TestPaths.Repo(@"external-apps\paintnet-support")]);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using (var ms = new MemoryStream(bytes))
        using (var read = AssemblyDefinition.ReadAssembly(ms, readerParams))
        {
            int patched = AssemblyRewriter.NeutralizeSessionPrefetchWait(read.MainModule);
            Assert.True(patched <= 1, "at most the one known Wait site may patch");
            read.Write(tmp);
        }
        return tmp;
    }

    private static string PatchInlineCopy(string src, string tag)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"{tag}_{Guid.NewGuid()}.dll");
        byte[] bytes = File.ReadAllBytes(src);
        string? dir = Path.GetDirectoryName(src);
        using var resolver = new FrameworkResolver(dir, [TestPaths.Repo(@".artifacts\runtime"), TestPaths.Repo(@"external-apps\paintnet-support")]);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using (var ms = new MemoryStream(bytes))
        using (var read = AssemblyDefinition.ReadAssembly(ms, readerParams))
        {
            int patched = AssemblyRewriter.NeutralizeSessionPrefetchInline(read.MainModule);
            Assert.True(patched <= 2, "at most the two known chain sites may patch");
            read.Write(tmp);
        }
        return tmp;
    }

    private static void AssertPrefetch(string path, Action<MethodDefinition> check)
    {
        using var read = AssemblyDefinition.ReadAssembly(path);
        var owner = read.MainModule.GetType("PaintDotNet.AppModel.WriteOnceSessionFile");
        Assert.NotNull(owner);
        var nested = owner.NestedTypes.First(t => t.Name == "ChunkedReadStream");
        var target = nested.Methods.First(m => m.Name == "PrefetchNextChunkImpl" && m.Parameters.Count == 0);
        check(target);
    }

    private static void AssertDispose(string path, Action<MethodDefinition> check)
    {
        using var read = AssemblyDefinition.ReadAssembly(path);
        var owner = read.MainModule.GetType("PaintDotNet.AppModel.WriteOnceSessionFile");
        Assert.NotNull(owner);
        var nested = owner.NestedTypes.First(t => t.Name == "ChunkedReadStream");
        var target = nested.Methods.First(m => m.Name == "Dispose" && m.Parameters.Count == 1);
        check(target);
    }

    [Fact]
    public void Synthetic_Wait_Swaps_To_Pop_Only()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"PrefetchSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? patched = null;
        try
        {
            patched = PatchCopy(tempOut, "PrefetchPatched");
            AssertDispose(patched, target =>
            {
                var instrs = target.Body.Instructions;
                Assert.Equal(3, instrs.Count);
                Assert.Equal(OpCodes.Call, instrs[0].OpCode);
                Assert.Equal(OpCodes.Pop, instrs[1].OpCode);
                Assert.Null(instrs[1].Operand);
                Assert.Equal(OpCodes.Ret, instrs[2].OpCode);
                Assert.DoesNotContain(instrs, i => i.Operand is MethodReference mr && mr.Name == "Wait");
            });
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (patched is not null) { try { File.Delete(patched); } catch { } }
        }
    }

    [Fact]
    public void Patched_Synthetic_Body_Jits_Without_InvalidProgram()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"PrefetchJit_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? patched = null;
        try
        {
            patched = PatchCopy(tempOut, "PrefetchJitOut");
            var alc = new AssemblyLoadContext($"PrefetchJit_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(patched);
                var owner = loaded.GetType("PaintDotNet.AppModel.WriteOnceSessionFile");
                Assert.NotNull(owner);
                var nested = owner.GetNestedType("ChunkedReadStream",
                    System.Reflection.BindingFlags.NonPublic);
                Assert.NotNull(nested);
                var target = nested.GetMethod("Dispose",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(target);
                RuntimeHelpers.PrepareMethod(target.MethodHandle);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (patched is not null) { try { File.Delete(patched); } catch { } }
        }
    }

    [Fact]
    public void Real_Paintdotnet_Dispose_Loses_Its_Only_Wait()
    {
        string src = @"E:\tmp\rewritten-paint\paintdotnet.dll";
        string? patched = null;
        try
        {
            patched = PatchCopy(src, "PrefetchReal");
            AssertDispose(patched, target =>
            {
                Assert.DoesNotContain(target.Body.Instructions,
                    i => i.Operand is MethodReference mr
                        && mr.DeclaringType.FullName == "System.Threading.Tasks.Task"
                        && mr.Name == "Wait" && mr.Parameters.Count == 0);
                Assert.Contains(target.Body.Instructions, i => i.OpCode == OpCodes.Pop);
            });
        }
        finally
        {
            if (patched is not null) { try { File.Delete(patched); } catch { } }
        }
    }

    [Fact]
    public void Synthetic_ContinueWith_Gains_Inline_Flag()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"PrefetchInline_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? patched = null;
        try
        {
            patched = PatchInlineCopy(tempOut, "PrefetchInlineOut");
            AssertPrefetch(patched, target =>
            {
                var instrs = target.Body.Instructions;
                var call = instrs.First(i => i.Operand is GenericInstanceMethod m && m.Name == "ContinueWith");
                var gim = Assert.IsAssignableFrom<GenericInstanceMethod>(call.Operand);
                Assert.Equal(2, gim.Parameters.Count);
                // The declarer must stay instantiated (open Task<T> dies in
                // the interpreter with "containing type is not fully
                // instantiated" - caught live on PDN undo, rev84).
                var decl = Assert.IsAssignableFrom<GenericInstanceType>(gim.DeclaringType);
                Assert.Single(decl.GenericArguments);
                Assert.Equal("System.Threading.Tasks.TaskContinuationOptions",
                    gim.Parameters[1].ParameterType.FullName);
                int at = instrs.IndexOf(call);
                Assert.Equal(OpCodes.Ldc_I4, instrs[at - 1].OpCode);
                Assert.Equal(524288, instrs[at - 1].Operand);
            });
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (patched is not null) { try { File.Delete(patched); } catch { } }
        }
    }

    [Fact]
    public void Patched_Inline_Body_Keeps_Exact_Verifiable_Sequence()
    {
        // The swap preserves the call shape (the extra int is consumed by
        // the call; no branch or handler moves), so the post-patch opcode
        // sequence plus a bumped MaxStack is the verifiability contract.
        // (A loader JIT like the olecb test uses is impossible here:
        // framework generic typerefs self-scope in synthetic modules.)
        string tempOut = Path.Combine(Path.GetTempPath(), $"PrefetchInlineJit_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? patched = null;
        try
        {
            patched = PatchInlineCopy(tempOut, "PrefetchInlineJitOut");
            AssertPrefetch(patched, target =>
            {
                var ops = target.Body.Instructions.Select(i => i.OpCode).ToArray();
                Assert.Equal(new[]
                {
                    OpCodes.Ldnull, OpCodes.Ldnull, OpCodes.Ldc_I4,
                    OpCodes.Callvirt, OpCodes.Pop, OpCodes.Ret,
                }, ops);
                Assert.True(target.Body.MaxStackSize >= 3);
            });
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (patched is not null) { try { File.Delete(patched); } catch { } }
        }
    }

    [Fact]
    public void Real_Paintdotnet_Prefetch_Runs_Inline()
    {
        string src = @"E:\tmp\rewritten-paint\paintdotnet.dll";
        string? patched = null;
        try
        {
            patched = PatchInlineCopy(src, "PrefetchInlineReal");
            AssertPrefetch(patched, target =>
            {
                // No one-arg ContinueWith may remain on the chain.
                Assert.DoesNotContain(target.Body.Instructions,
                    i => i.Operand is GenericInstanceMethod m && m.Name == "ContinueWith"
                        && m.Parameters.Count == 1);
                int flags = target.Body.Instructions.Count(i =>
                    i.OpCode == OpCodes.Ldc_I4 && Equals(i.Operand, 524288));
                Assert.True(flags >= 1, "inline flag present");
                foreach (var i in target.Body.Instructions)
                {
                    if (i.Operand is GenericInstanceMethod m && m.Name == "ContinueWith"
                        && m.Parameters.Count == 2)
                    {
                        var decl = Assert.IsAssignableFrom<GenericInstanceType>(m.DeclaringType);
                        Assert.NotEmpty(decl.GenericArguments);
                    }
                }
            });
        }
        finally
        {
            if (patched is not null) { try { File.Delete(patched); } catch { } }
        }
    }

    [Fact]
    public void Missing_Member_Fails_Loudly()
    {
        var asmName = new AssemblyNameDefinition("PrefetchEmpty", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "PrefetchEmpty", ModuleKind.Dll))
        {
            Assert.Throws<InvalidOperationException>(() =>
                AssemblyRewriter.NeutralizeSessionPrefetchWait(asm.MainModule));
            Assert.Throws<InvalidOperationException>(() =>
                AssemblyRewriter.NeutralizeSessionPrefetchInline(asm.MainModule));
        }
    }
}
