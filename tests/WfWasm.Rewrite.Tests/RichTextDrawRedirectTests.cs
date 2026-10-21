using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins the rev78 headless RichText paint rules: diagram RichTextNode
// defaults to the bitmap path (the metafile shell cannot exist headless)
// and RichTextBoxDrawing.Draw(Graphics, Rectangle) becomes a trampoline
// into the fork's managed WasmRtfDraw (native EM_FORMATRANGE cannot
// survive headless).
public sealed class RichTextDrawRedirectTests
{
    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("RtfDrawSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "RtfDrawSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var formsRef = new AssemblyNameReference("WfWasm.WinForms", new Version(1, 0, 0, 0));
            var drawingRef = new AssemblyNameReference("WfWasm.Drawing", new Version(1, 0, 0, 0));
            module.AssemblyReferences.Add(formsRef);
            module.AssemblyReferences.Add(drawingRef);
            var streamRef = module.ImportReference(typeof(System.IO.Stream));

            var boxRef = new TypeReference("System.Windows.Forms", "RichTextBox", module, formsRef);
            var gfxRef = new TypeReference("System.Drawing", "Graphics", module, drawingRef);
            var rectRef = new TypeReference("System.Drawing", "Rectangle", module, drawingRef) { IsValueType = true };

            var drawing = new TypeDefinition("Syncfusion.Windows.Forms.Diagram", "RichTextBoxDrawing",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                boxRef);
            var draw = new MethodDefinition("Draw",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            draw.Parameters.Add(new ParameterDefinition(gfxRef));
            draw.Parameters.Add(new ParameterDefinition(rectRef));
            var drawIl = draw.Body.GetILProcessor();
            drawIl.Emit(OpCodes.Ldnull);
            drawIl.Emit(OpCodes.Pop);
            drawIl.Emit(OpCodes.Ret);
            drawing.Methods.Add(draw);
            module.Types.Add(drawing);

            var node = new TypeDefinition("Syncfusion.Windows.Forms.Diagram", "RichTextNode",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var flag = new FieldDefinition("m_bUseBitmap",
                Mono.Cecil.FieldAttributes.Private, module.TypeSystem.Boolean);
            node.Fields.Add(flag);
            foreach (int pcount in new[] { 0, 1 })
            {
                var ctor = new MethodDefinition(".ctor",
                    Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig
                    | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                    module.TypeSystem.Void);
                if (pcount == 1)
                {
                    ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                }

                var il = ctor.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stfld, flag);
                il.Emit(OpCodes.Ret);
                node.Methods.Add(ctor);
            }

            module.Types.Add(node);
            asm.Write(tempOut);
        }
    }

    private static string RewriteCopy(string src, string tag)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"{tag}_{Guid.NewGuid()}.dll");
        byte[] bytes = File.ReadAllBytes(src);
        using (var ms = new MemoryStream(bytes))
        using (var read = AssemblyDefinition.ReadAssembly(ms))
        {
            AssemblyRewriter.ForceDiagramBitmapText(read.MainModule);
            AssemblyRewriter.ReplaceRtfDrawMethod(read.MainModule);
            read.Write(tmp);
        }

        return tmp;
    }

    [Fact]
    public void Redirect_Synthetic_Draw_Becomes_Fork_Trampoline()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"RtfDrawSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? replaced = null;
        try
        {
            replaced = RewriteCopy(tempOut, "RtfDrawReplaced");
            using var read = AssemblyDefinition.ReadAssembly(replaced);
            var drawing = read.MainModule.GetType("Syncfusion.Windows.Forms.Diagram.RichTextBoxDrawing");
            var draw = drawing.Methods.First(m => m.Name == "Draw");
            var instrs = draw.Body.Instructions;
            Assert.Equal(5, instrs.Count);
            Assert.Equal(OpCodes.Ldarg_0, instrs[0].OpCode);
            Assert.Equal(OpCodes.Ldarg_1, instrs[1].OpCode);
            Assert.Equal(OpCodes.Ldarg_2, instrs[2].OpCode);
            Assert.Equal(OpCodes.Call, instrs[3].OpCode);
            var target = Assert.IsAssignableFrom<MethodReference>(instrs[3].Operand);
            Assert.Equal("DrawRtfManaged", target.Name);
            Assert.Equal("WasmRtfDraw", target.DeclaringType.Name);
            Assert.Equal("WfWasm.WinForms", target.DeclaringType.Scope.Name);
            Assert.Equal(OpCodes.Ret, instrs[4].OpCode);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (replaced is not null) { try { File.Delete(replaced); } catch { } }
        }
    }

    [Fact]
    public void Force_Synthetic_Ctors_Set_Bitmap_Flag()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"RtfDrawSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? replaced = null;
        try
        {
            replaced = RewriteCopy(tempOut, "RtfDrawForced");
            using var read = AssemblyDefinition.ReadAssembly(replaced);
            var node = read.MainModule.GetType("Syncfusion.Windows.Forms.Diagram.RichTextNode");
            foreach (var ctor in node.Methods.Where(m => m.Name == ".ctor"))
            {
                var instrs = ctor.Body.Instructions;
                // ldarg.0, ldc.i4.0, stfld, ldarg.0, ldc.i4.1, stfld, ret
                Assert.Equal(7, instrs.Count);
                Assert.Equal(OpCodes.Stfld, instrs[2].OpCode);
                Assert.Equal(OpCodes.Stfld, instrs[5].OpCode);
                var first = Assert.IsAssignableFrom<FieldReference>(instrs[2].Operand);
                var second = Assert.IsAssignableFrom<FieldReference>(instrs[5].Operand);
                Assert.Equal("m_bUseBitmap", first.Name);
                Assert.Equal("m_bUseBitmap", second.Name);
                Assert.Equal(OpCodes.Ret, instrs[6].OpCode);
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (replaced is not null) { try { File.Delete(replaced); } catch { } }
        }
    }

    [Fact]
    public void Rewritten_Synthetic_Jits_Without_InvalidProgram()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"RtfDrawSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? replaced = null;
        try
        {
            replaced = RewriteCopy(tempOut, "RtfDrawJit");
            var alc = new AssemblyLoadContext($"RtfDrawJit_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(replaced);
                var node = loaded.GetType("Syncfusion.Windows.Forms.Diagram.RichTextNode");
                Assert.NotNull(node);
                foreach (var ctor in node.GetConstructors())
                {
                    RuntimeHelpers.PrepareMethod(ctor.MethodHandle);
                }

                // RichTextBoxDrawing derives from the fork's RichTextBox, so
                // the type cannot load without the fork assembly; its
                // trampoline shape is pinned by the rewrite test above.
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (replaced is not null) { try { File.Delete(replaced); } catch { } }
        }
    }

    [Fact]
    public void Redirect_Real_Diagram_Draw_Binds_And_Jits()
    {
        // Real-bytes binding proof for the cross-assembly trampoline: a
        // hand-built struct reference without IsValueType misbinds at
        // runtime (MissingMethod on desktop, TypeLoad in wasm) even though
        // the metadata looks right. Runs the rules over pristine vendor
        // bytes and JITs the result with the real fork on the load path.
        string src = TestPaths.Repo(@"external-apps\syncfusion\syncfusion-diagram\Syncfusion.Diagram.Base.dll");
        string tmp = Path.Combine(Path.GetTempPath(), $"RtfDrawReal_{Guid.NewGuid()}.dll");
        try
        {
            byte[] bytes = File.ReadAllBytes(src);
            using var resolver = new FrameworkResolver(Path.GetDirectoryName(src),
                [TestPaths.Repo(@".artifacts\runtime"), TestPaths.Repo(@"external-apps\syncfusion\syncfusion-diagram")]);
            var readerParams = new ReaderParameters { AssemblyResolver = resolver };
            using (var ms = new MemoryStream(bytes))
            using (var read = AssemblyDefinition.ReadAssembly(ms, readerParams))
            {
                AssemblyRewriter.ForceDiagramBitmapText(read.MainModule);
                AssemblyRewriter.ReplaceRtfDrawMethod(read.MainModule);
                read.Write(tmp);
            }

            var alc = new AssemblyLoadContext($"RtfDrawReal_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                alc.Resolving += (ctx, name) =>
                {
                    foreach (string dir in new[]
                    {
                        TestPaths.Repo(@".artifacts\runtime"),
                        TestPaths.Repo(@"external-apps\syncfusion\syncfusion-diagram"),
                    })
                    {
                        string p = Path.Combine(dir, name.Name + ".dll");
                        if (File.Exists(p))
                        {
                            return ctx.LoadFromAssemblyPath(Path.GetFullPath(p));
                        }
                    }

                    return null;
                };
                var loaded = alc.LoadFromAssemblyPath(tmp);
                var drawing = loaded.GetType("Syncfusion.Windows.Forms.Diagram.RichTextBoxDrawing");
                Assert.NotNull(drawing);
                var draw = drawing.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static)
                    .First(m => m.Name == "Draw" && m.GetParameters().Length == 2);
                RuntimeHelpers.PrepareMethod(draw.MethodHandle);
                var node = loaded.GetType("Syncfusion.Windows.Forms.Diagram.RichTextNode");
                Assert.NotNull(node);
                foreach (var ctor in node.GetConstructors())
                {
                    RuntimeHelpers.PrepareMethod(ctor.MethodHandle);
                }
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Rules_Missing_Types_Fail_Loudly()
    {
        var asmName = new AssemblyNameDefinition("RtfDrawNoHelp", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "RtfDrawNoHelp", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.ForceDiagramBitmapText(module));
            Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.ReplaceRtfDrawMethod(module));
        }
    }
}
