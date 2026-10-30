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
                    .SelectMany(x => x.Body.Instructions.Select(i => (M: x, I: i)))
                    .Where(x => (x.I.OpCode == OpCodes.Call || x.I.OpCode == OpCodes.Callvirt)
                        && x.I.Operand is MethodReference)
                    .Select(x => (M: x.M, Op: x.I.OpCode, Mr: (MethodReference)x.I.Operand, Ins: x.I))
                    .ToList();
                var spins = calls.Where(c => c.Mr.Name == "DrawSpinBackground").ToList();
                Assert.Equal(2, spins.Count);
                Assert.All(spins, s =>
                {
                    Assert.Equal(OpCodes.Call, s.Op);
                    Assert.Equal("System.Windows.Forms.VisualStyles.VisualStyleSpinPainter", s.Mr.DeclaringType.FullName);
                });
                // Stack check: the (object, object, Rectangle) call's pushes
                // must match their slots (static method: ldarg_N = param N).
                var spin2 = spins.Single(s => s.Mr.Parameters.Count == 3);
                AssertPushTypesMatch(spin2.M, spin2.Ins, new[]
                {
                    "System.Object",
                    "System.Object",
                    "System.Drawing.Rectangle",
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

    [Fact]
    public void Redirect_Hwnd_Tailed_SpinDrawBackground_Stays_Unrewritten()
    {
        // kind-17 regression (DockSample DummyDoc/DummySolutionExplorer,
        // PDN numerics): DrawBackground(HDC, Rectangle, HWND) must stay on
        // the original stubbed P/Invoke. HDC carries no pixels without a
        // Graphics (the helper would no-op anyway), and any valuetype-exact
        // helper reference fails to bind headless. Only all-reference tails
        // are retargeted; unrecognized tails stay unrewritten.
        string tempOut = Path.Combine(Path.GetTempPath(), $"SpinHwnd_{Guid.NewGuid()}.dll");
        try
        {
            ModuleDefinition module;
            using (var asm = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("SpinHwnd", new Version(1, 0, 0, 0)), "SpinHwnd", ModuleKind.Dll))
            {
                module = asm.MainModule;
                var forms = new AssemblyNameReference("System.Windows.Forms", new Version(9, 0, 0, 0));
                var drawing = new AssemblyNameReference("System.Drawing", new Version(9, 0, 0, 0));
                module.AssemblyReferences.Add(forms);
                module.AssemblyReferences.Add(drawing);
                var rect = new TypeReference("System.Drawing", "Rectangle", module, drawing);
                // In-module valuetypes so IsValueType resolves hermetically.
                var sysValueType = new TypeReference("System", "ValueType", module, module.TypeSystem.Corlib);
                var hdcDef = new TypeDefinition("Windows.Win32.Graphics.Gdi", "HDC",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.SequentialLayout | Mono.Cecil.TypeAttributes.Sealed,
                    sysValueType);
                var hwndDef = new TypeDefinition("Windows.Win32.Foundation", "HWND",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.SequentialLayout | Mono.Cecil.TypeAttributes.Sealed,
                    sysValueType);
                module.Types.Add(hdcDef);
                module.Types.Add(hwndDef);
                var hdc = new TypeReference("Windows.Win32.Graphics.Gdi", "HDC", module, module);
                var hwnd = new TypeReference("Windows.Win32.Foundation", "HWND", module, module);
                var intPtr = module.TypeSystem.IntPtr;
                var host = new TypeDefinition("SpinHwnd", "Host",
                    TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
                var m = new MethodDefinition("Paint", Mono.Cecil.MethodAttributes.Public,
                    module.TypeSystem.Void);
                // this + (HDC, Rectangle, HWND): the Form-grip/UpDown shape.
                m.Parameters.Add(new ParameterDefinition(hdc));
                m.Parameters.Add(new ParameterDefinition(rect));
                m.Parameters.Add(new ParameterDefinition(hwnd));
                // this + (object, Rectangle, IntPtr): unrecognized tail.
                var m2 = new MethodDefinition("PaintIntPtr", Mono.Cecil.MethodAttributes.Public,
                    module.TypeSystem.Void);
                m2.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                m2.Parameters.Add(new ParameterDefinition(rect));
                m2.Parameters.Add(new ParameterDefinition(intPtr));
                host.Methods.Add(m);
                host.Methods.Add(m2);
                module.Types.Add(host);
                var il = m.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldarg_3);
                EmitDrawBackground(il, hdc, rect, hwnd);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
                var il2 = m2.Body.GetILProcessor();
                il2.Emit(OpCodes.Ldarg_0);
                il2.Emit(OpCodes.Ldarg_1);
                il2.Emit(OpCodes.Ldarg_2);
                il2.Emit(OpCodes.Ldarg_3);
                EmitDrawBackground(il2, module.TypeSystem.Object, rect, intPtr);
                il2.Emit(OpCodes.Pop);
                il2.Emit(OpCodes.Ret);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition mod = read.MainModule;
                AssemblyRewriter.RedirectSpinDrawBackground(mod);

                var calls = mod.Types.SelectMany(t => t.Methods).Where(x => x.HasBody)
                    .SelectMany(x => x.Body.Instructions.Select(i => (M: x, I: i)))
                    .Where(x => (x.I.OpCode == OpCodes.Call || x.I.OpCode == OpCodes.Callvirt)
                        && x.I.Operand is MethodReference)
                    .Select(x => (M: x.M, Op: x.I.OpCode, Mr: (MethodReference)x.I.Operand, Ins: x.I))
                    .ToList();
                var spins = calls.Where(c => c.Mr.Name == "DrawSpinBackground").ToList();
                Assert.Empty(spins);
                // HWND tail: stays on the original stubbed call.
                var bg = calls.Where(c => c.Mr.Name == "DrawBackground"
                    && c.Mr.DeclaringType.FullName == "System.Windows.Forms.VisualStyles.VisualStyleRenderer").ToList();
                Assert.Equal(2, bg.Count);
                Assert.All(bg, c => Assert.Equal(OpCodes.Callvirt, c.Op));
                var hwndCall = bg.Single(c => c.M.Parameters.Any(p => p.ParameterType.FullName == "Windows.Win32.Foundation.HWND"));
                var pars = hwndCall.Mr.Parameters.Select(p => p.ParameterType.FullName).ToArray();
                Assert.Equal(new[]
                {
                    "Windows.Win32.Graphics.Gdi.HDC",
                    "System.Drawing.Rectangle",
                    "Windows.Win32.Foundation.HWND",
                }, pars);
                // IntPtr tail: no matching overload, stays unrewritten.
                Assert.Contains(bg, c => c.M.Parameters.Any(p => p.ParameterType.FullName == "System.IntPtr"));
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }

    private static void EmitDrawBackground(ILProcessor il, params TypeReference[] pars)
    {
        var module = il.Body.Method.Module;
        var scope = module.AssemblyReferences.First(r => r.Name == "System.Windows.Forms");
        var dt = new TypeReference("System.Windows.Forms.VisualStyles", "VisualStyleRenderer", module, scope);
        var mr = new MethodReference("DrawBackground", module.TypeSystem.Void, dt) { HasThis = true };
        foreach (var p in pars)
            mr.Parameters.Add(new ParameterDefinition(p));
        il.Emit(OpCodes.Callvirt, mr);
    }

    private static void AssertPushTypesMatch(MethodDefinition m, Instruction call, string[] argTypes)
    {
        // Straight-line prologue only (the shapes this rule handles): the N
        // pushes immediately precede the call; each must be Ldarg with a
        // parameter whose type FullName equals the expected slot type.
        // Static methods have no this: ldarg_N maps to parameter N.
        var ins = m.Body.Instructions;
        int at = ins.IndexOf(call);
        Assert.True(at >= argTypes.Length, "call prologue too short");
        for (int a = 0; a < argTypes.Length; a++)
        {
            var push = ins[at - argTypes.Length + a];
            int paramIndex = push.OpCode == OpCodes.Ldarg_0 ? 0
                : push.OpCode == OpCodes.Ldarg_1 ? 1
                : push.OpCode == OpCodes.Ldarg_2 ? 2
                : push.OpCode == OpCodes.Ldarg_3 ? 3 : -1;
            Assert.True(paramIndex >= 0, $"unexpected push {push.OpCode} for arg {a}");
            string actual = !m.IsStatic
                ? (paramIndex == 0 ? m.DeclaringType.FullName : m.Parameters[paramIndex - 1].ParameterType.FullName)
                : m.Parameters[paramIndex].ParameterType.FullName;
            Assert.Equal(argTypes[a], actual);
        }
    }
}
