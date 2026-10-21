using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins NeutralizeTextBoxSelectAllCalls: TextBoxBase.SelectAll() routes
// through the live RichEdit selection (EM_SETSEL against the created
// handle plus UIA automation events) and OOB-traps headless once the
// handle exists. Proven at SfDateTimeEdit.SetFocusInEditingMode during
// Syncfusion calendar boot (clean runs + click proof with the call
// dropped); same family in SfNumericTextBox focus/click/shortcut paths.
// The rule drops the void call, keeping the instance evaluation.
public sealed class TextBoxSelectAllNeutralizeTests
{
    private static string BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("SelectAllSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "SelectAllSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var box = new TypeDefinition("System.Windows.Forms", "TextBoxBase",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var selectAll = new MethodDefinition("SelectAll",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Virtual,
                module.TypeSystem.Void);
            selectAll.Body.GetILProcessor().Emit(OpCodes.Ret);
            box.Methods.Add(selectAll);
            var selectAllInt = new MethodDefinition("SelectAll",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            selectAllInt.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            selectAllInt.Body.GetILProcessor().Emit(OpCodes.Ret);
            box.Methods.Add(selectAllInt);
            var ctor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var ctorIl = ctor.Body.GetILProcessor();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));
            ctorIl.Emit(OpCodes.Ret);
            box.Methods.Add(ctor);
            module.Types.Add(box);

            var other = new TypeDefinition("SelectAllSynth", "Other",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var otherSelect = new MethodDefinition("SelectAll",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            otherSelect.Body.GetILProcessor().Emit(OpCodes.Ret);
            other.Methods.Add(otherSelect);
            var otherCtor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var otherCtorIl = otherCtor.Body.GetILProcessor();
            otherCtorIl.Emit(OpCodes.Ldarg_0);
            otherCtorIl.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));
            otherCtorIl.Emit(OpCodes.Ret);
            other.Methods.Add(otherCtor);
            module.Types.Add(other);

            // Use(box, other): target call + int-overload decoy + other-type decoy.
            var user = new TypeDefinition("SelectAllSynth", "User",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var use = new MethodDefinition("Use",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            use.Parameters.Add(new ParameterDefinition(box));
            use.Parameters.Add(new ParameterDefinition(other));
            var useIl = use.Body.GetILProcessor();
            useIl.Emit(OpCodes.Ldarg_0);
            useIl.Emit(OpCodes.Callvirt, selectAll);
            useIl.Emit(OpCodes.Ldarg_0);
            useIl.Emit(OpCodes.Ldc_I4_0);
            useIl.Emit(OpCodes.Callvirt, selectAllInt);
            useIl.Emit(OpCodes.Ldarg_1);
            useIl.Emit(OpCodes.Callvirt, otherSelect);
            useIl.Emit(OpCodes.Ret);
            user.Methods.Add(use);
            module.Types.Add(user);
            asm.Write(tempOut);
        }
        return tempOut;
    }

    private static bool IsSelectAllCall(Instruction i)
    {
        return (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && i.Operand is MethodReference mr
            && mr.Name == "SelectAll";
    }

    [Fact]
    public void Synthetic_SelectAll_Call_Replaced_With_Pop()
    {
        string src = Path.Combine(Path.GetTempPath(), $"SelectAllSynth_{Guid.NewGuid()}.dll");
        string out2 = Path.Combine(Path.GetTempPath(), $"SelectAllSynthOut_{Guid.NewGuid()}.dll");
        BuildSynthetic(src);
        try
        {
            using (var read = AssemblyDefinition.ReadAssembly(src))
            {
                int count = AssemblyRewriter.NeutralizeTextBoxSelectAllCalls(read.MainModule);
                Assert.Equal(1, count);
                // Write aside: the ALC below must execute the rewritten
                // bytes, not the pristine input.
                read.Write(out2);
            }

            // Shape asserts on a fresh read of the written output.
            using var verify = AssemblyDefinition.ReadAssembly(out2);
            var use = verify.MainModule.GetType("SelectAllSynth.User").Methods.First(m => m.Name == "Use");
            var remaining = use.Body.Instructions.Where(IsSelectAllCall).ToList();
            // The int overload and the other-type calls survive.
            Assert.Equal(2, remaining.Count);
            Assert.All(remaining, i =>
            {
                var mr = (MethodReference)i.Operand;
                Assert.True(mr.Parameters.Count == 1 || mr.DeclaringType.Name == "Other");
            });
            // The dropped void call leaves a Pop consuming the instance.
            Assert.Contains(use.Body.Instructions, i => i.OpCode == OpCodes.Pop);

            var alc = new AssemblyLoadContext($"SelectAllSynth_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(out2);
                var boxType = loaded.GetType("System.Windows.Forms.TextBoxBase");
                var otherType = loaded.GetType("SelectAllSynth.Other");
                Assert.NotNull(boxType);
                Assert.NotNull(otherType);
                var useMethod = loaded.GetType("SelectAllSynth.User")!.GetMethod("Use");
                Assert.NotNull(useMethod);
                // InvalidProgramException here means the neutralization
                // broke the method it was meant to save; invoking proves
                // the Pop splice executes with a balanced stack.
                RuntimeHelpers.PrepareMethod(useMethod.MethodHandle);
                useMethod.Invoke(null, [Activator.CreateInstance(boxType)!, Activator.CreateInstance(otherType)!]);
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
    public void Real_SfInput_SelectAll_Sites_Neutralized_With_Intact_Control_Flow()
    {
        string src = TestPaths.Repo(@"external-apps\syncfusion\syncfusion-calendar\Syncfusion.SfInput.WinForms.dll");
        Assert.True(File.Exists(src), $"vendor SfInput missing: {src}");
        string tmp = Path.Combine(Path.GetTempPath(), $"SfInputSelectAll_{Guid.NewGuid()}.dll");
        string out2 = Path.Combine(Path.GetTempPath(), $"SfInputSelectAllOut_{Guid.NewGuid()}.dll");
        File.Copy(src, tmp);
        try
        {
            // Order-based: duplicate signatures (e.g. twin instance
            // SetText(string) bodies with different headers) break dict
            // keys, but metadata order is deterministic across reads.
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
                count = AssemblyRewriter.NeutralizeTextBoxSelectAllCalls(read.MainModule);
                // Write aside: Cecil keeps the read stream open for lazy
                // metadata, which self-locks a same-path write.
                read.Write(out2);
            }
            // SetFocusInEditingMode + 4 SfNumericTextBox paths.
            Assert.Equal(5, count);

            // Structural integrity of the splice: no TextBoxBase.SelectAll
            // calls remain, every branch/handler target still resolves
            // in-body (the --probe corruptions taught us to check control
            // flow, not just call shapes), and MaxStackSize headers are
            // untouched (Pop only shrinks depth, so the old header still
            // covers the new body).
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
                            broken.Add(MethodKey(t, m) + $" maxstack {beforeMaxStack[seq]}->{m.Body.MaxStackSize}");
                        seq++;
                        var inBody = new HashSet<Instruction>(m.Body.Instructions);
                        foreach (var i in m.Body.Instructions)
                        {
                            if (IsSelectAllCall(i) && i.Operand is MethodReference mr
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

    private static string MethodKey(TypeDefinition t, MethodDefinition m)
    {
        return (m.IsStatic ? "static " : "instance ") + t.FullName + "::" + m.Name + "/" + m.Parameters.Count;
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
