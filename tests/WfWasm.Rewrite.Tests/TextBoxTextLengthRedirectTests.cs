using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectTextBoxTextLengthToManaged: TextBoxBase.get_TextLength
// routes through a generic P/Invoke wrapper (GetWindowTextLength<T>) the
// WASM interpreter cannot transform, OOB-killing the session headless.
// Proven at SfDateTimeEdit.RepositionFields during Syncfusion calendar
// boot. The rule rewrites the call as get_Text().Length, which reads the
// same underlying text on this fork in every handle state.
public sealed class TextBoxTextLengthRedirectTests
{
    private static string BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("TextLengthSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "TextLengthSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var box = new TypeDefinition("System.Windows.Forms", "TextBoxBase",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var getTextLength = new MethodDefinition("get_TextLength",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.Virtual,
                module.TypeSystem.Int32);
            var getTextLengthIl = getTextLength.Body.GetILProcessor();
            getTextLengthIl.Emit(OpCodes.Ldc_I4_0);
            getTextLengthIl.Emit(OpCodes.Ret);
            box.Methods.Add(getTextLength);
            var getText = new MethodDefinition("get_Text",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.Virtual,
                module.TypeSystem.String);
            var getTextIl = getText.Body.GetILProcessor();
            getTextIl.Emit(OpCodes.Ldstr, "hello");
            getTextIl.Emit(OpCodes.Ret);
            box.Methods.Add(getText);
            var ctor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var ctorIl = ctor.Body.GetILProcessor();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));
            ctorIl.Emit(OpCodes.Ret);
            box.Methods.Add(ctor);
            module.Types.Add(box);

            var other = new TypeDefinition("TextLengthSynth", "Other",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var otherLength = new MethodDefinition("get_TextLength",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName,
                module.TypeSystem.Int32);
            var otherLengthIl = otherLength.Body.GetILProcessor();
            otherLengthIl.Emit(OpCodes.Ldc_I4_0);
            otherLengthIl.Emit(OpCodes.Ret);
            other.Methods.Add(otherLength);
            var otherCtor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var otherCtorIl = otherCtor.Body.GetILProcessor();
            otherCtorIl.Emit(OpCodes.Ldarg_0);
            otherCtorIl.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));
            otherCtorIl.Emit(OpCodes.Ret);
            other.Methods.Add(otherCtor);
            module.Types.Add(other);

            var user = new TypeDefinition("TextLengthSynth", "User",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var use = new MethodDefinition("Use",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Int32);
            use.Parameters.Add(new ParameterDefinition(box));
            use.Parameters.Add(new ParameterDefinition(other));
            var useIl = use.Body.GetILProcessor();
            useIl.Emit(OpCodes.Ldarg_0);
            useIl.Emit(OpCodes.Callvirt, getTextLength);
            useIl.Emit(OpCodes.Ldarg_1);
            useIl.Emit(OpCodes.Callvirt, otherLength);
            useIl.Emit(OpCodes.Add);
            useIl.Emit(OpCodes.Ret);
            user.Methods.Add(use);
            module.Types.Add(user);
            asm.Write(tempOut);
        }
        return tempOut;
    }

    [Fact]
    public void Synthetic_TextLength_Call_Rewritten_As_Text_Length()
    {
        string src = Path.Combine(Path.GetTempPath(), $"TextLengthSynth_{Guid.NewGuid()}.dll");
        string out2 = Path.Combine(Path.GetTempPath(), $"TextLengthSynthOut_{Guid.NewGuid()}.dll");
        BuildSynthetic(src);
        try
        {
            using (var read = AssemblyDefinition.ReadAssembly(src))
            {
                int count = AssemblyRewriter.RedirectTextBoxTextLengthToManaged(read.MainModule);
                Assert.Equal(1, count);
                // Write aside for the load below: the rule edits the
                // in-memory model, and the ALC must execute the rewritten
                // bytes, not the pristine input.
                read.Write(out2);
            }

            // Shape asserts run on a fresh read of the WRITTEN output, so
            // they also cover the Cecil round-trip, not just memory.
            using var verify = AssemblyDefinition.ReadAssembly(out2);
            var use = verify.MainModule.GetType("TextLengthSynth.User").Methods.First(m => m.Name == "Use");
            // get_TextLength is gone; the other-type call survives.
            Assert.DoesNotContain(use.Body.Instructions, i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "get_TextLength"
                && mr.DeclaringType.Name == "TextBoxBase");
            Assert.Contains(use.Body.Instructions, i =>
                i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference mr
                && mr.Name == "get_TextLength" && mr.DeclaringType.Name == "Other");
            // Replacement shape: get_Text immediately followed by get_Length.
            var instrs = use.Body.Instructions;
            bool shape = false;
            for (int k = 0; k + 1 < instrs.Count; k++)
            {
                if (instrs[k].Operand is MethodReference a && a.Name == "get_Text"
                    && instrs[k + 1].Operand is MethodReference b && b.Name == "get_Length")
                    shape = true;
            }
            Assert.True(shape, "get_Text().get_Length() splice missing");

            var alc = new AssemblyLoadContext($"TextLengthSynth_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(out2);
                var boxType = loaded.GetType("System.Windows.Forms.TextBoxBase");
                var otherType = loaded.GetType("TextLengthSynth.Other");
                Assert.NotNull(boxType);
                Assert.NotNull(otherType);
                var useMethod = loaded.GetType("TextLengthSynth.User")!.GetMethod("Use");
                Assert.NotNull(useMethod);
                // InvalidProgramException here means the splice broke the
                // method; the return value proves get_Text().Length flows
                // (5 from "hello" + 0 from the stub).
                RuntimeHelpers.PrepareMethod(useMethod.MethodHandle);
                object? result = useMethod.Invoke(null,
                    [Activator.CreateInstance(boxType)!, Activator.CreateInstance(otherType)!]);
                Assert.Equal(5, result);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(out2); } catch { }
        }
    }

    [Fact]
    public void Real_SfInput_TextLength_Sites_Rewritten_With_Intact_Control_Flow()
    {
        string src = TestPaths.Repo(@"external-apps\syncfusion\syncfusion-calendar\Syncfusion.SfInput.WinForms.dll");
        Assert.True(File.Exists(src), $"vendor SfInput missing: {src}");
        string tmp = Path.Combine(Path.GetTempPath(), $"SfInputTextLength_{Guid.NewGuid()}.dll");
        string out2 = Path.Combine(Path.GetTempPath(), $"SfInputTextLengthOut_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            // Order-based maxstack ledger (duplicate signatures break
            // dict keys; metadata order is deterministic across reads).
            var beforeMaxStack = new List<int>();
            using (var pre = AssemblyDefinition.ReadAssembly(tmp))
            {
                foreach (var t in pre.MainModule.GetTypes())
                    foreach (var m in t.Methods)
                    {
                        if (m.HasBody)
                            beforeMaxStack.Add(m.Body.MaxStackSize);
                    }
            }
            int count;
            using (var read = AssemblyDefinition.ReadAssembly(tmp))
            {
                count = AssemblyRewriter.RedirectTextBoxTextLengthToManaged(read.MainModule);
                // Write aside: Cecil keeps the read stream open for lazy
                // metadata, which self-locks a same-path write.
                read.Write(out2);
            }
            // RepositionFields + SfNumericTextBox.HandleFunctionKeys.
            Assert.Equal(2, count);

            using (var verify = AssemblyDefinition.ReadAssembly(out2))
            {
                var leftovers = new List<string>();
                var broken = new List<string>();
                int seq = 0;
                foreach (var t in verify.MainModule.GetTypes())
                    foreach (var m in t.Methods)
                    {
                        if (!m.HasBody) continue;
                        if (seq < beforeMaxStack.Count && m.Body.MaxStackSize != beforeMaxStack[seq])
                            broken.Add(t.FullName + "::" + m.Name + $" maxstack {beforeMaxStack[seq]}->{m.Body.MaxStackSize}");
                        seq++;
                        var inBody = new HashSet<Instruction>(m.Body.Instructions);
                        foreach (var i in m.Body.Instructions)
                        {
                            if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                                && i.Operand is MethodReference mr
                                && mr.Name == "get_TextLength"
                                && mr.Parameters.Count == 0
                                && mr.DeclaringType.Name == "TextBoxBase")
                                leftovers.Add(t.FullName + "::" + m.Name);
                            foreach (var target in BranchTargets(i))
                            {
                                if (!inBody.Contains(target))
                                    broken.Add(t.FullName + "::" + m.Name + " @" + i.Offset.ToString("X4"));
                            }
                        }
                        foreach (var h in m.Body.ExceptionHandlers)
                        {
                            foreach (var edge in new[] { h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd })
                            {
                                if (edge is not null && !inBody.Contains(edge))
                                    broken.Add(t.FullName + "::" + m.Name + " handler");
                            }
                        }
                    }
                Assert.Empty(leftovers);
                Assert.True(broken.Count == 0,
                    "control-flow damage:\n" + string.Join("\n", broken.Take(10)));
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            try { File.Delete(out2); } catch { }
        }
    }

    private static IEnumerable<Instruction> BranchTargets(Instruction i)
    {
        if (i.Operand is Instruction single)
            yield return single;
        else if (i.Operand is Instruction[] many)
        {
            foreach (var t in many)
                yield return t;
        }
    }
}
