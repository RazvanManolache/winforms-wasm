using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins ReplaceRichTextStreamMethods (rev77): RichTextBox.StreamIn(Stream,
// uint) and StreamOut(Stream, uint, bool) must become trampolines to the
// managed WasmRtf helpers. The desktop bodies drive native RichEdit through
// Marshal.GetFunctionPointerForDelegate, whose thunk traps the wasm
// interpreter with "memory access out of bounds" and no managed frame
// (Syncfusion diagram RichTextNode boot). Shape assertions pin the
// trampolines; the JIT test proves verifiability.
public sealed class RichTextStreamRedirectTests
{
    private static void BuildSynthetic(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("RtfSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "RtfSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var streamRef = module.ImportReference(typeof(System.IO.Stream));
            var box = new TypeDefinition("System.Windows.Forms", "RichTextBox",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);

            var streamIn = new MethodDefinition("StreamIn",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            streamIn.Parameters.Add(new ParameterDefinition(streamRef));
            streamIn.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            var sinIl = streamIn.Body.GetILProcessor();
            sinIl.Emit(OpCodes.Ldnull);
            sinIl.Emit(OpCodes.Pop);
            sinIl.Emit(OpCodes.Ret);
            box.Methods.Add(streamIn);

            var streamOut = new MethodDefinition("StreamOut",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            streamOut.Parameters.Add(new ParameterDefinition(streamRef));
            streamOut.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            streamOut.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            var soutIl = streamOut.Body.GetILProcessor();
            soutIl.Emit(OpCodes.Ldnull);
            soutIl.Emit(OpCodes.Pop);
            soutIl.Emit(OpCodes.Ret);
            box.Methods.Add(streamOut);

            var getTextEx = new MethodDefinition("GetTextEx",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.String);
            getTextEx.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            var gtIl = getTextEx.Body.GetILProcessor();
            gtIl.Emit(OpCodes.Ldnull);
            gtIl.Emit(OpCodes.Ret);
            box.Methods.Add(getTextEx);

            var wasm = new TypeDefinition("", "WasmRtf",
                Mono.Cecil.TypeAttributes.NestedAssembly | Mono.Cecil.TypeAttributes.Sealed
                | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var boxRef = new TypeReference("System.Windows.Forms", "RichTextBox", module, null);
            var inHelper = new MethodDefinition("StreamInManaged",
                Mono.Cecil.MethodAttributes.Assembly | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            inHelper.Parameters.Add(new ParameterDefinition(boxRef));
            inHelper.Parameters.Add(new ParameterDefinition(streamRef));
            inHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            inHelper.Body.GetILProcessor().Emit(OpCodes.Ret);
            wasm.Methods.Add(inHelper);
            var outHelper = new MethodDefinition("StreamOutManaged",
                Mono.Cecil.MethodAttributes.Assembly | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            outHelper.Parameters.Add(new ParameterDefinition(boxRef));
            outHelper.Parameters.Add(new ParameterDefinition(streamRef));
            outHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            outHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
            outHelper.Body.GetILProcessor().Emit(OpCodes.Ret);
            wasm.Methods.Add(outHelper);
            var gtHelper = new MethodDefinition("GetTextManaged",
                Mono.Cecil.MethodAttributes.Assembly | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.String);
            gtHelper.Parameters.Add(new ParameterDefinition(boxRef));
            gtHelper.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            var gtHIl = gtHelper.Body.GetILProcessor();
            gtHIl.Emit(OpCodes.Ldnull);
            gtHIl.Emit(OpCodes.Ret);
            wasm.Methods.Add(gtHelper);
            box.NestedTypes.Add(wasm);

            module.Types.Add(box);
            asm.Write(tempOut);
        }
    }

    private static string ReplaceCopy(string src, string tag)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"{tag}_{Guid.NewGuid()}.dll");
        byte[] bytes = File.ReadAllBytes(src);
        using (var ms = new MemoryStream(bytes))
        using (var read = AssemblyDefinition.ReadAssembly(ms))
        {
            AssemblyRewriter.ReplaceRichTextStreamMethods(read.MainModule);
            read.Write(tmp);
        }

        return tmp;
    }

    private static void AssertTrampoline(MethodDefinition target, string helper, int args)
    {
        var instrs = target.Body.Instructions;
        Assert.Equal(args + 2, instrs.Count);
        Assert.Equal(OpCodes.Ldarg_0, instrs[0].OpCode);
        Assert.Equal(OpCodes.Ldarg_1, instrs[1].OpCode);
        if (args > 2)
        {
            Assert.Equal(OpCodes.Ldarg_2, instrs[2].OpCode);
        }

        if (args > 3)
        {
            Assert.Equal(OpCodes.Ldarg_3, instrs[3].OpCode);
        }

        var call = instrs[args];
        Assert.Equal(OpCodes.Call, call.OpCode);
        var operand = Assert.IsAssignableFrom<MethodReference>(call.Operand);
        Assert.Equal(helper, operand.Name);
        Assert.EndsWith("WasmRtf", operand.DeclaringType.FullName);
        Assert.Equal(OpCodes.Ret, instrs[args + 1].OpCode);
        Assert.Empty(target.Body.ExceptionHandlers);
    }

    [Fact]
    public void Redirect_Synthetic_StreamMethods_Become_Trampolines()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"RtfSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? replaced = null;
        try
        {
            replaced = ReplaceCopy(tempOut, "RtfReplaced");
            using var read = AssemblyDefinition.ReadAssembly(replaced);
            var box = read.MainModule.GetType("System.Windows.Forms.RichTextBox");
            var streamIn = box.Methods.First(m => m.Name == "StreamIn");
            var streamOut = box.Methods.First(m => m.Name == "StreamOut");
            var getTextEx = box.Methods.First(m => m.Name == "GetTextEx");
            AssertTrampoline(streamIn, "StreamInManaged", 3);
            AssertTrampoline(streamOut, "StreamOutManaged", 4);
            AssertTrampoline(getTextEx, "GetTextManaged", 2);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (replaced is not null) { try { File.Delete(replaced); } catch { } }
        }
    }

    [Fact]
    public void Replaced_Bodies_Jit_Without_InvalidProgram()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"RtfSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut);
        string? replaced = null;
        try
        {
            replaced = ReplaceCopy(tempOut, "RtfJit");
            var alc = new AssemblyLoadContext($"RtfJit_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(replaced);
                var box = loaded.GetType("System.Windows.Forms.RichTextBox");
                Assert.NotNull(box);
                foreach (string name in new[] { "StreamIn", "StreamOut", "GetTextEx" })
                {
                    var target = box.GetMethod(name,
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Assert.NotNull(target);
                    RuntimeHelpers.PrepareMethod(target.MethodHandle);
                }
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
    public void Redirect_Missing_Helper_Fails_Loudly()
    {
        var asmName = new AssemblyNameDefinition("RtfNoHelp", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "RtfNoHelp", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var box = new TypeDefinition("System.Windows.Forms", "RichTextBox",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            module.Types.Add(box);
            Assert.Throws<InvalidOperationException>(() => AssemblyRewriter.ReplaceRichTextStreamMethods(module));
        }
    }

    [Fact]
    public void Redirect_Real_Fork_StreamMethods_Become_Trampolines()
    {
        string src = TestPaths.Repo(@".artifacts\runtime\WfWasm.WinForms.dll");
        string tmp = Path.Combine(Path.GetTempPath(), $"RtfFork_{Guid.NewGuid()}.dll");
        try
        {
            byte[] bytes = File.ReadAllBytes(src);
            using var resolver = new FrameworkResolver(Path.GetDirectoryName(src), [AppContext.BaseDirectory, TestPaths.Repo(@".artifacts\runtime"), TestPaths.Repo(@"external-apps\paintnet-support")]);
            var readerParams = new ReaderParameters { AssemblyResolver = resolver };
            using (var ms = new MemoryStream(bytes))
            using (var read = AssemblyDefinition.ReadAssembly(ms, readerParams))
            {
                AssemblyRewriter.ReplaceRichTextStreamMethods(read.MainModule);
                read.Write(tmp);
            }

            using var check = AssemblyDefinition.ReadAssembly(tmp);
            var box = check.MainModule.GetType("System.Windows.Forms.RichTextBox");
            AssertTrampoline(box.Methods.First(m => m.Name == "StreamIn" && m.Parameters.Count == 2
                    && m.Parameters[0].ParameterType.FullName == "System.IO.Stream"),
                "StreamInManaged", 3);
            AssertTrampoline(box.Methods.First(m => m.Name == "StreamOut" && m.Parameters.Count == 3
                    && m.Parameters[0].ParameterType.FullName == "System.IO.Stream"),
                "StreamOutManaged", 4);
            AssertTrampoline(box.Methods.First(m => m.Name == "GetTextEx" && m.Parameters.Count == 1),
                "GetTextManaged", 2);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
