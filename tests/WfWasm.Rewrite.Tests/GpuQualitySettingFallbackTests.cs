using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins EnsureGpuQualitySettingFallback: GpuEffect.get_DefaultQualityLevelGpu
// is answered by a synthesized IScalarAppSetting<int> (1, range 1..8 — the
// device-less-desktop default) because the settings lookup is missing
// headless and every GPU effect NREs in OnCreatePropertyCollection
// dereferencing it (live: RotateZoom, Bulge).
public sealed class GpuQualitySettingFallbackTests
{
    private static void BuildFake(string dir, string asmName, string[][] ifaces)
    {
        var asmDef = new AssemblyNameDefinition(asmName, new Version(1, 0, 0, 0));
        using var asm = AssemblyDefinition.CreateAssembly(asmDef, asmName, ModuleKind.Dll);
        ModuleDefinition module = asm.MainModule;
        foreach (string[] spec in ifaces)
        {
            string full = spec[0];
            int dot = full.LastIndexOf('.');
            var td = new TypeDefinition(full.Substring(0, dot), full.Substring(dot + 1),
                Mono.Cecil.TypeAttributes.Interface | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Public,
                null);
            foreach (string gp in spec.Skip(1))
                td.GenericParameters.Add(new GenericParameter(gp, td));
            module.Types.Add(td);
        }
        asm.Write(Path.Combine(dir, asmName + ".dll"));
    }

    private static string BuildTarget(string dir)
    {
        var asmName = new AssemblyNameDefinition("Fx", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "Fx", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            module.AssemblyReferences.Add(new AssemblyNameReference("PaintDotNet.Core", new Version(1, 0, 0, 0)));
            module.AssemblyReferences.Add(new AssemblyNameReference("PaintDotNet.ComponentModel", new Version(1, 0, 0, 0)));
            string corePath = Path.Combine(dir, "PaintDotNet.Core.dll");
            using var core = AssemblyDefinition.ReadAssembly(corePath);
            var open = core.MainModule.Types.First(t => t.FullName == "PaintDotNet.AppModel.IScalarAppSetting`1");
            var closed = new GenericInstanceType(module.ImportReference(open));
            closed.GenericArguments.Add(module.TypeSystem.Int32);
            var ret = module.ImportReference(closed);
            var effect = new TypeDefinition("PaintDotNet.Effects.Gpu", "GpuEffect",
                Mono.Cecil.TypeAttributes.Class | Mono.Cecil.TypeAttributes.Public,
                module.TypeSystem.Object);
            var getter = new MethodDefinition("get_DefaultQualityLevelGpu",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                | Mono.Cecil.MethodAttributes.SpecialName,
                ret);
            var il = getter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            effect.Methods.Add(getter);
            module.Types.Add(effect);
            string target = Path.Combine(dir, "Fx.dll");
            asm.Write(target);
            return target;
        }
    }

    [Fact]
    public void Synthetic_QualityGetter_Answers_Synthesized_Setting()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"GpuQual_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            BuildFake(dir, "PaintDotNet.Core", [
                ["PaintDotNet.AppModel.IScalarAppSetting`1", "T"],
                ["PaintDotNet.AppModel.ISetting`1", "T"],
                ["PaintDotNet.AppModel.ISetting"],
            ]);
            BuildFake(dir, "PaintDotNet.ComponentModel", [
                ["PaintDotNet.IInternalImpl"],
            ]);
            string target = BuildTarget(dir);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(dir);
            using var read = AssemblyDefinition.ReadAssembly(target,
                new ReaderParameters { AssemblyResolver = resolver });
            ModuleDefinition module = read.MainModule;
            AssemblyRewriter.EnsureGpuQualitySettingFallback(module);

            var synth = module.GetType("PaintDotNet.Gpu.CpuGpuQualitySetting");
            Assert.NotNull(synth);
            var getter = module.GetType("PaintDotNet.Effects.Gpu.GpuEffect").Methods
                .First(m => m.Name == "get_DefaultQualityLevelGpu");
            Assert.Equal(2, getter.Body.Instructions.Count);
            Assert.Equal(OpCodes.Newobj, getter.Body.Instructions[0].OpCode);
            Assert.Equal(".ctor", ((MethodReference)getter.Body.Instructions[0].Operand).Name);
            Assert.Equal(OpCodes.Ret, getter.Body.Instructions[1].OpCode);

            MethodDefinition Body(string name, int argc) =>
                synth.Methods.First(m => m.Name == name && m.Parameters.Count == argc);
            var max = Body("get_MaxValue", 0);
            Assert.Equal(OpCodes.Ldc_I4_8, max.Body.Instructions[0].OpCode);
            Assert.Equal(OpCodes.Ret, max.Body.Instructions[1].OpCode);
            var val = Body("get_Value", 0);
            Assert.Equal(OpCodes.Ldc_I4_1, val.Body.Instructions[0].OpCode);
            var min = Body("get_MinValue", 0);
            Assert.Equal(OpCodes.Ldc_I4_1, min.Body.Instructions[0].OpCode);
            var path = Body("get_Path", 0);
            Assert.Equal(OpCodes.Ldstr, path.Body.Instructions[0].OpCode);
            Assert.Equal("Effects/DefaultQualityLevelGpu", (string)path.Body.Instructions[0].Operand);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
