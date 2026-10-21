using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectFileTypeCctorTables (renamed file, kept for continuity):
// built-in FileType .cctors keep every
// instruction except the WIC argument chains, which answer desktop-exact
// literal tables (so DefaultSaveExtension is never null headless).
public sealed class FileTypeCctorTableTests
{
    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("CctorSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "CctorSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var win = new AssemblyNameReference("PaintDotNet.Windows", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(win);
            var format = new TypeDefinition("PaintDotNet.Imaging", "ContainerFormat",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
                module.ImportReference(typeof(ValueType)));
            module.Types.Add(format);
            var formats = new TypeDefinition("PaintDotNet.Imaging", "ContainerFormats",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            var getPng = new MethodDefinition("get_Png",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                format);
            getPng.Body.GetILProcessor().Emit(OpCodes.Ret);
            formats.Methods.Add(getPng);
            module.Types.Add(formats);

            var helpers = new TypeDefinition("PaintDotNet.Data", "WicHelpers",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            foreach (string name in new[] { "GetLoadExtensions", "GetSaveExtensions" })
            {
                var m = new MethodDefinition(name,
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                    module.TypeSystem.Object);
                m.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                m.Body.GetILProcessor().Emit(OpCodes.Ret);
                helpers.Methods.Add(m);
            }
            module.Types.Add(helpers);

            // Fake PngFileType .cctor with the verified WIC-chain shape.
            var ft = new TypeDefinition("PaintDotNet.Data", "PngFileType",
                TypeAttributes.Public | TypeAttributes.Class,
                module.TypeSystem.Object);
            var cctor = new MethodDefinition(".cctor",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static
                | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName
                | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            var il = cctor.Body.GetILProcessor();
            cctor.Body.Variables.Add(new VariableDefinition(format));
            cctor.Body.InitLocals = true;
            var wicLoad = helpers.Methods.First(m => m.Name == "GetLoadExtensions");
            var wicSave = helpers.Methods.First(m => m.Name == "GetSaveExtensions");
            il.Emit(OpCodes.Call, getPng);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloca_S, cctor.Body.Variables[0]);
            il.Emit(OpCodes.Call, wicLoad);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Call, getPng);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloca_S, cctor.Body.Variables[0]);
            il.Emit(OpCodes.Call, wicSave);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            ft.Methods.Add(cctor);
            module.Types.Add(ft);
            asm.Write(tempOut);
        }
    }

    [Fact]
    public void Redirect_Synthetic_PngCctor_Answers_Literal_Table()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"CctorSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        try
        {
            using var read = AssemblyDefinition.ReadAssembly(tempOut);
            ModuleDefinition module = read.MainModule;
            AssemblyRewriter.RedirectFileTypeCctorTables(module);

            var cctor = module.GetType("PaintDotNet.Data.PngFileType").Methods
                .First(m => m.Name == ".cctor");
            var calls = cctor.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Call)
                .Select(i => ((MethodReference)i.Operand).Name)
                .ToList();
            // No WIC/format/vendor calls remain; only in-module shape.
            Assert.DoesNotContain("GetLoadExtensions", calls);
            Assert.DoesNotContain("GetSaveExtensions", calls);
            Assert.DoesNotContain("get_Png", calls);
            var strings = cctor.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Ldstr)
                .Select(i => (string)i.Operand)
                .ToList();
            Assert.Contains(".png", strings);
            Assert.Equal(OpCodes.Ret, cctor.Body.Instructions.Last().OpCode);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
