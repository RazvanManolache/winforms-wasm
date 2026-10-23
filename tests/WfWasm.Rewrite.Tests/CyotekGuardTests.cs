using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

public sealed class CyotekGuardTests
{
    [Fact]
    public void Guard_Ble_Becomes_Blt_Only_At_Guard_Sites()
    {
        // Hermetic: the vendored ColorGrid guard `index > count` is IL
        // `ble.s REAL` falling through to the Empty/None/throw block;
        // swapping to `blt.s` corrects the boundary to `>=`. The
        // SetColor dispatch ble (fall-through = custom branch) and
        // same-named methods on other types must stay put; long form
        // maps Ble->Blt, short Ble_S->Blt_S.
        string tempOut = Path.Combine(Path.GetTempPath(), $"CyotekSynth_{Guid.NewGuid()}.dll");
        try
        {
            ModuleDefinition module;
            using (var asm = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition("CyotekSynth", new Version(1, 0, 0, 0)), "CyotekSynth", ModuleKind.Dll))
            {
                module = asm.MainModule;
                var drawing = new AssemblyNameReference("System.Drawing", new Version(9, 0, 0, 0));
                module.AssemblyReferences.Add(drawing);
                var color = new TypeReference("System.Drawing", "Color", module, drawing);
                var emptyField = new FieldReference("Empty", color,
                    new TypeReference("System.Drawing", "Color", module, drawing));

                TypeDefinition grid = new TypeDefinition("Cyotek.Windows.Forms", "ColorGrid",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);

                // GetColor(int):Color with the exact guard shape.
                var getColor = new MethodDefinition("GetColor",
                    Mono.Cecil.MethodAttributes.Public, color);
                getColor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                getColor.Body.Variables.Add(new VariableDefinition(color));
                getColor.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                getColor.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                getColor.Body.InitLocals = true;
                {
                    var il = getColor.Body.GetILProcessor();
                    var fail = il.Create(OpCodes.Ldsfld, emptyField);
                    var real = il.Create(OpCodes.Ldc_I4_1);
                    var end = il.Create(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Blt_S, fail);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldloc_2);
                    il.Emit(OpCodes.Add);
                    var guard = il.Create(OpCodes.Ble_S, real);
                    il.Append(guard);
                    il.Append(fail);
                    il.Emit(OpCodes.Stloc_0);
                    var skip = il.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Br_S, skip);
                    il.Append(real);
                    il.Emit(OpCodes.Pop);
                    il.Append(skip);
                    il.Append(end);
                    il.Emit(OpCodes.Ret);
                }
                grid.Methods.Add(getColor);

                // GetColorSource(int):int with guard shape (None=0 block).
                var getSource = new MethodDefinition("GetColorSource",
                    Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Int32);
                getSource.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                getSource.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                getSource.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                getSource.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                getSource.Body.InitLocals = true;
                {
                    var il = getSource.Body.GetILProcessor();
                    var fail = il.Create(OpCodes.Ldc_I4_0);
                    var real = il.Create(OpCodes.Ldc_I4_1);
                    var end = il.Create(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Blt_S, fail);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldloc_2);
                    il.Emit(OpCodes.Add);
                    var guard = il.Create(OpCodes.Ble_S, real);
                    il.Append(guard);
                    il.Append(fail);
                    il.Emit(OpCodes.Stloc_0);
                    var skip = il.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Br_S, skip);
                    il.Append(real);
                    il.Emit(OpCodes.Pop);
                    il.Append(skip);
                    il.Append(end);
                    il.Emit(OpCodes.Ret);
                }
                grid.Methods.Add(getSource);

                // SetColor(int,int):void with guard ble + dispatch ble.
                var setColor = new MethodDefinition("SetColor",
                    Mono.Cecil.MethodAttributes.Public, module.TypeSystem.Void);
                setColor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                setColor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                setColor.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Int32));
                setColor.Body.InitLocals = true;
                {
                    var il = setColor.Body.GetILProcessor();
                    var fail = il.Create(OpCodes.Ldstr, "colorIndex");
                    var real = il.Create(OpCodes.Ldarg_0);
                    var std = il.Create(OpCodes.Ldarg_1);
                    var end = il.Create(OpCodes.Ret);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Blt_S, fail);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Add);
                    var guard = il.Create(OpCodes.Ble_S, real);
                    il.Append(guard);
                    il.Append(fail);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Br_S, end);
                    il.Append(real);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Sub);
                    var dispatch = il.Create(OpCodes.Ble_S, std);
                    il.Append(dispatch);
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Br_S, end);
                    il.Append(std);
                    il.Emit(OpCodes.Pop);
                    il.Append(end);
                }
                grid.Methods.Add(setColor);
                module.Types.Add(grid);

                // Same-named methods on another type: must stay put.
                TypeDefinition other = new TypeDefinition("Other", "Grid",
                    Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
                var otherGet = new MethodDefinition("GetColor",
                    Mono.Cecil.MethodAttributes.Public, color);
                otherGet.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                {
                    var il = otherGet.Body.GetILProcessor();
                    var fail = il.Create(OpCodes.Ldsfld, emptyField);
                    var real = il.Create(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldarg_1);
                    var guard = il.Create(OpCodes.Ble, real);
                    il.Append(guard);
                    il.Append(fail);
                    il.Emit(OpCodes.Ret);
                    il.Append(real);
                    il.Emit(OpCodes.Ret);
                }
                other.Methods.Add(otherGet);
                module.Types.Add(other);
                asm.Write(tempOut);
            }

            using (var read = AssemblyDefinition.ReadAssembly(tempOut))
            {
                ModuleDefinition mod = read.MainModule;
                int bleBefore = mod.Types.SelectMany(t => t.Methods).Where(x => x.HasBody)
                    .SelectMany(x => x.Body.Instructions)
                    .Count(i => i.OpCode == OpCodes.Ble_S || i.OpCode == OpCodes.Ble);
                Assert.Equal(5, bleBefore);

                AssemblyRewriter.FixCyotekColorGridBounds(mod);

                var grid = mod.GetType("Cyotek.Windows.Forms.ColorGrid");
                Assert.NotNull(grid);
                var getColor = grid.Methods.First(m => m.Name == "GetColor");
                var getSource = grid.Methods.First(m => m.Name == "GetColorSource");
                var setColor = grid.Methods.First(m => m.Name == "SetColor");
                // Guard sites flipped to blt*: each method already had one
                // blt.s (the index<0 check), so post-rule counts are 2/2/2
                // (setColor also keeps its dispatch ble.s).
                Assert.Equal(2, getColor.Body.Instructions.Count(i => i.OpCode == OpCodes.Blt_S));
                Assert.DoesNotContain(getColor.Body.Instructions, i => i.OpCode == OpCodes.Ble_S);
                Assert.Equal(2, getSource.Body.Instructions.Count(i => i.OpCode == OpCodes.Blt_S));
                Assert.DoesNotContain(getSource.Body.Instructions, i => i.OpCode == OpCodes.Ble_S);
                Assert.Equal(2, setColor.Body.Instructions.Count(i => i.OpCode == OpCodes.Blt_S));
                // Dispatch ble untouched (still ble, one left).
                Assert.Single(setColor.Body.Instructions.Where(i => i.OpCode == OpCodes.Ble_S));
                // Other type untouched (long Ble kept).
                var other = mod.GetType("Other.Grid");
                Assert.Contains(other.Methods.First(m => m.Name == "GetColor").Body.Instructions,
                    i => i.OpCode == OpCodes.Ble);
                // Idempotent: second run changes nothing.
                AssemblyRewriter.FixCyotekColorGridBounds(mod);
                Assert.Single(setColor.Body.Instructions.Where(i => i.OpCode == OpCodes.Ble_S));
                Assert.Equal(2, getColor.Body.Instructions.Count(i => i.OpCode == OpCodes.Blt_S));
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
