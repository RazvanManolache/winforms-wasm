using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class SpinRedirectTests
{
    private static void EmitCall(ILProcessor il, string name, string declaringType, bool hasThis, params TypeReference[] pars)
    {
        var module = il.Body.Method.Module;
        var scope = module.AssemblyReferences.First(r =>
            declaringType.StartsWith("System.Drawing.", StringComparison.Ordinal)
                ? r.Name == "System.Drawing"
                : r.Name == "System.Windows.Forms");
        var dt = new TypeReference(
            declaringType.Substring(0, declaringType.LastIndexOf('.')),
            declaringType.Substring(declaringType.LastIndexOf('.') + 1),
            module, scope);
        var mr = new MethodReference(name, il.Body.Method.Module.TypeSystem.Void, dt) { HasThis = hasThis };
        foreach (var p in pars)
            mr.Parameters.Add(new ParameterDefinition(p));
        OpCode[] loads = new OpCode[] { OpCodes.Ldarg_0, OpCodes.Ldarg_1, OpCodes.Ldarg_2, OpCodes.Ldarg_3 };
        for (int a = 0; a <= pars.Length && a < 4; a++)
            il.Emit(loads[a]);
        il.Emit(hasThis ? OpCodes.Callvirt : OpCodes.Call, mr);
    }

    [Fact]
    public void Redirect_Synthetic_SpinDrawBackground_Moves_To_Fork_Painter()
    {
        // Hermetic: DrawBackground callvirt sites on the sealed upstream
        // renderer must retarget to the fork's GDI+ spin painter (both
        // overloads); unrelated calls stay put.
        string tempOut = Path.Combine(Path.GetTempPath(), $"SpinSynth_{Guid.NewGuid()}.dll");
        try
        {
            ModuleDefinition module;
            using (var asm = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("SpinSynth", new Version(1, 0, 0, 0)), "SpinSynth", ModuleKind.Dll))
            {
                module = asm.MainModule;
                var forms = new AssemblyNameReference("System.Windows.Forms", new Version(9, 0, 0, 0));
                var drawing = new AssemblyNameReference("System.Drawing", new Version(9, 0, 0, 0));
                module.AssemblyReferences.Add(forms);
                module.AssemblyReferences.Add(drawing);
                var rect = new TypeReference("System.Drawing", "Rectangle", module, drawing);
                var host = new TypeDefinition("SpinSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
                var m = new MethodDefinition("Paint", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    module.TypeSystem.Void);
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                m.Parameters.Add(new ParameterDefinition(rect));
                m.Parameters.Add(new ParameterDefinition(rect));
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                host.Methods.Add(m);
                module.Types.Add(host);
                var il = m.Body.GetILProcessor();
                EmitCall(il, "DrawBackground", "System.Windows.Forms.VisualStyles.VisualStyleRenderer", true,
                    module.TypeSystem.Object, rect);
                il.Emit(OpCodes.Pop);
                EmitCall(il, "DrawBackground", "System.Windows.Forms.VisualStyles.VisualStyleRenderer", true,
                    module.TypeSystem.Object, rect, rect);
                il.Emit(OpCodes.Pop);
                EmitCall(il, "DrawString", "System.Drawing.Graphics", true,
                    module.TypeSystem.Object);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition mod = read.MainModule;
                AssemblyRewriter.RedirectSpinDrawBackground(mod);

                var calls = mod.Types.SelectMany(t => t.Methods).Where(x => x.HasBody)
                    .SelectMany(x => x.Body.Instructions)
                    .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                        && i.Operand is MethodReference)
                    .Select(i => (Op: i.OpCode, Mr: (MethodReference)i.Operand))
                    .ToList();
                var spins = calls.Where(c => c.Mr.Name == "DrawSpinBackground").ToList();
                Assert.Equal(2, spins.Count);
                Assert.All(spins, s =>
                {
                    Assert.Equal(OpCodes.Call, s.Op);
                    Assert.Equal("System.Windows.Forms.VisualStyles.VisualStyleSpinPainter", s.Mr.DeclaringType.FullName);
                });
                Assert.Contains(calls, c => c.Mr.Name == "DrawString"
                    && c.Mr.DeclaringType.FullName == "System.Drawing.Graphics");
                Assert.DoesNotContain(calls, c => c.Mr.Name == "DrawBackground"
                    && c.Mr.DeclaringType.FullName == "System.Windows.Forms.VisualStyles.VisualStyleRenderer");
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
