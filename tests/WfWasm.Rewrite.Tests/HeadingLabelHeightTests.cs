using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;
using Xunit;

// Pins RedirectHeadingLabelPreferredHeight: the null-DWrite measurement
// answers the fork font line height (declaring types pinned — a wrong
// scope fails at runtime, cf. the op_Equality lesson).
public sealed class HeadingLabelHeightTests
{
    [Fact]
    public void Redirect_Synthetic_HeadingLabel_Answers_FontHeight()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"HeadingSynth_{Guid.NewGuid()}.dll");
        try
        {
            var asmName = new AssemblyNameDefinition("HeadingSynth", new Version(1, 0, 0, 0));
            using (var asm = AssemblyDefinition.CreateAssembly(asmName, "HeadingSynth", ModuleKind.Dll))
            {
                ModuleDefinition module = asm.MainModule;
                module.AssemblyReferences.Add(new AssemblyNameReference("WfWasm.Shim", new Version(1, 0, 0, 0)));
                module.AssemblyReferences.Add(new AssemblyNameReference("WfWasm.Drawing", new Version(1, 0, 0, 0)));
                var label = new TypeDefinition("PaintDotNet.Controls", "HeadingLabel",
                    TypeAttributes.Public | TypeAttributes.Class,
                    module.TypeSystem.Object);
                var m = new MethodDefinition("GetPreferredTextHeight",
                    Mono.Cecil.MethodAttributes.Public,
                    module.TypeSystem.Int32);
                var il = m.Body.GetILProcessor();
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Throw);
                label.Methods.Add(m);
                module.Types.Add(label);
                asm.Write(tempOut);
            }

            using var read = AssemblyDefinition.ReadAssembly(tempOut);
            ModuleDefinition rw = read.MainModule;
            AssemblyRewriter.RedirectHeadingLabelPreferredHeight(rw);

            var target = rw.GetType("PaintDotNet.Controls.HeadingLabel").Methods
                .First(x => x.Name == "GetPreferredTextHeight");
            var calls = target.Body.Instructions
                .Where(i => i.OpCode == OpCodes.Callvirt)
                .Select(i => (MethodReference)i.Operand).ToList();
            Assert.Equal(2, calls.Count);
            Assert.Equal("get_Font", calls[0].Name);
            Assert.Equal("System.Windows.Forms.Control", calls[0].DeclaringType.FullName);
            Assert.Equal("get_Height", calls[1].Name);
            Assert.Equal("System.Drawing.Font", calls[1].DeclaringType.FullName);
            Assert.Equal(OpCodes.Ret, target.Body.Instructions.Last().OpCode);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
        }
    }
}
