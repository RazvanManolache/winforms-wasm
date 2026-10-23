using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class ScrollButtonRedirectTests
{
    private static void EmitCall(ILProcessor il, string name, string declaringType, params TypeReference[] pars)
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
        var mr = new MethodReference(name, il.Body.Method.Module.TypeSystem.Void, dt) { HasThis = false };
        foreach (var p in pars)
            mr.Parameters.Add(new ParameterDefinition(p));
        for (int a = 0; a < pars.Length; a++)
        {
            if (a == 0) il.Emit(OpCodes.Ldarg_0);
            else if (a == 1) il.Emit(OpCodes.Ldarg_1);
            else if (a == 2) il.Emit(OpCodes.Ldarg_2);
            else if (a == 3) il.Emit(OpCodes.Ldarg_3);
            else il.Emit(OpCodes.Ldarg_S, il.Body.Method.Parameters[a]);
        }
        il.Emit(OpCodes.Call, mr);
    }

    [Fact]
    public void Redirect_Synthetic_ScrollButton_Moves_To_Fork_Painter()
    {
        // Hermetic: ControlPaint.DrawBackground classic ends in the
        // stubbed DrawFrameControl P/Invoke, so both static overloads
        // must retarget to the fork's GDI+ scroll painter; unrelated
        // calls and same-named methods on other types stay put. The
        // helper reference must reuse the call site's own parameter
        // scopes so the emitted signature binds where the original did.
        string tempOut = Path.Combine(Path.GetTempPath(), $"ScrollSynth_{Guid.NewGuid()}.dll");
        try
        {
            ModuleDefinition module;
            using (var asm = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("ScrollSynth", new Version(1, 0, 0, 0)), "ScrollSynth", ModuleKind.Dll))
            {
                module = asm.MainModule;
                var forms = new AssemblyNameReference("System.Windows.Forms", new Version(9, 0, 0, 0));
                var drawing = new AssemblyNameReference("System.Drawing", new Version(9, 0, 0, 0));
                module.AssemblyReferences.Add(forms);
                module.AssemblyReferences.Add(drawing);
                var graphics = new TypeReference("System.Drawing", "Graphics", module, drawing);
                var rect = new TypeReference("System.Drawing", "Rectangle", module, drawing);
                var scrollButton = new TypeReference("System.Windows.Forms", "ScrollButton", module, forms);
                var buttonState = new TypeReference("System.Windows.Forms", "ButtonState", module, forms);
                var host = new TypeDefinition("ScrollSynth", "Host",
                    TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
                var m = new MethodDefinition("Paint", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    module.TypeSystem.Void);
                m.Parameters.Add(new ParameterDefinition(graphics));
                m.Parameters.Add(new ParameterDefinition(rect));
                m.Parameters.Add(new ParameterDefinition(scrollButton));
                m.Parameters.Add(new ParameterDefinition(buttonState));
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                host.Methods.Add(m);
                module.Types.Add(host);
                var il = m.Body.GetILProcessor();
                EmitCall(il, "DrawScrollButton", "System.Windows.Forms.ControlPaint",
                    graphics, rect, scrollButton, buttonState);
                il.Emit(OpCodes.Pop);
                EmitCall(il, "DrawScrollButton", "System.Windows.Forms.ControlPaint",
                    graphics, module.TypeSystem.Int32, module.TypeSystem.Int32,
                    module.TypeSystem.Int32, module.TypeSystem.Int32, scrollButton, buttonState);
                il.Emit(OpCodes.Pop);
                EmitCall(il, "DrawString", "System.Drawing.Graphics", graphics);
                il.Emit(OpCodes.Pop);
                EmitCall(il, "DrawScrollButton", "System.Windows.Forms.CustomPaint",
                    graphics, rect, scrollButton, buttonState);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition mod = read.MainModule;
                AssemblyRewriter.RedirectScrollButtonDraw(mod);

                var calls = mod.Types.SelectMany(t => t.Methods).Where(x => x.HasBody)
                    .SelectMany(x => x.Body.Instructions)
                    .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                        && i.Operand is MethodReference)
                    .Select(i => (Op: i.OpCode, Mr: (MethodReference)i.Operand))
                    .ToList();
                var painters = calls.Where(c => c.Mr.DeclaringType.FullName == "System.Windows.Forms.ControlPaintScrollPainter").ToList();
                Assert.Equal(2, painters.Count);
                Assert.All(painters, p => Assert.Equal(OpCodes.Call, p.Op));
                var four = painters.Single(p => p.Mr.Parameters.Count == 4);
                Assert.Equal("System.Drawing.Graphics", four.Mr.Parameters[0].ParameterType.FullName);
                Assert.Equal("System.Drawing", four.Mr.Parameters[0].ParameterType.Scope.Name);
                Assert.Equal("System.Windows.Forms.ScrollButton", four.Mr.Parameters[2].ParameterType.FullName);
                Assert.Equal("System.Windows.Forms", four.Mr.Parameters[2].ParameterType.Scope.Name);
                Assert.Equal("System.Windows.Forms.ButtonState", four.Mr.Parameters[3].ParameterType.FullName);
                Assert.Single(painters, p => p.Mr.Parameters.Count == 7);
                Assert.DoesNotContain(calls, c => c.Mr.Name == "DrawScrollButton"
                    && c.Mr.DeclaringType.FullName == "System.Windows.Forms.ControlPaint");
                Assert.Contains(calls, c => c.Mr.Name == "DrawString"
                    && c.Mr.DeclaringType.FullName == "System.Drawing.Graphics");
                Assert.Contains(calls, c => c.Mr.Name == "DrawScrollButton"
                    && c.Mr.DeclaringType.FullName == "System.Windows.Forms.CustomPaint");
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
