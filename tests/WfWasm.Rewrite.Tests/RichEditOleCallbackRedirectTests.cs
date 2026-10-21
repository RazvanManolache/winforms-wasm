using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins NeutralizeRichEditOleCallback: RichTextBox.UpdateOleCallback must keep
// only a type-correct DragAcceptFiles(this.Handle, false) tail (COM OLE
// callback absent headless; the GetComScope PNS kills every RichTextBox
// handle creation and the worker-thread instance OOB-traps the session).
// The tail MUST be verifiable IL: the earlier ldarg.0(RichTextBox) +
// ldc.i4.0 direct call passed a class where HWND was expected and an int
// where BOOL was expected, so desktop PrepareMethod throws InvalidProgram
// and the wasm interpreter dies in transform (OOB-89A) with no managed
// frame. Shape assertions below pin the converting sequence; the
// Neutralized_Body_Jits_Without_InvalidProgram test proves verifiability.
public sealed class RichEditOleCallbackRedirectTests
{
    // callGeneric mirrors the real fork, whose original body calls the
    // generic DragAcceptFiles<T> wrapper; false calls the raw overload.
    private static void BuildSynthetic(string tempOut, bool callGeneric)
    {
        var asmName = new AssemblyNameDefinition("OleSynth", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "OleSynth", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;

            var hwnd = new TypeDefinition("Windows.Win32.Foundation", "HWND",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.SequentialLayout,
                module.ImportReference(typeof(ValueType)));
            var hwndValue = new FieldDefinition("Value",
                Mono.Cecil.FieldAttributes.Public, module.TypeSystem.IntPtr);
            hwnd.Fields.Add(hwndValue);
            var hwndCtor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            hwndCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
            var hil = hwndCtor.Body.GetILProcessor();
            hil.Emit(OpCodes.Ldarg_0);
            hil.Emit(OpCodes.Ldarg_1);
            hil.Emit(OpCodes.Stfld, hwndValue);
            hil.Emit(OpCodes.Ret);
            hwnd.Methods.Add(hwndCtor);
            module.Types.Add(hwnd);

            var bl = new TypeDefinition("Windows.Win32.Foundation", "BOOL",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.SequentialLayout,
                module.ImportReference(typeof(ValueType)));
            var blValue = new FieldDefinition("Value",
                Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Int32);
            bl.Fields.Add(blValue);
            var blCtor = new MethodDefinition(".ctor",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            blCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            var bil = blCtor.Body.GetILProcessor();
            bil.Emit(OpCodes.Ldarg_0);
            bil.Emit(OpCodes.Ldarg_1);
            bil.Emit(OpCodes.Stfld, blValue);
            bil.Emit(OpCodes.Ret);
            bl.Methods.Add(blCtor);
            module.Types.Add(bl);

            var control = new TypeDefinition("System.Windows.Forms", "Control",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var getHandle = new MethodDefinition("get_Handle",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.IntPtr);
            var gil = getHandle.Body.GetILProcessor();
            gil.Emit(OpCodes.Ldc_I4_0);
            gil.Emit(OpCodes.Conv_I);
            gil.Emit(OpCodes.Ret);
            control.Methods.Add(getHandle);
            module.Types.Add(control);

            var pinvoke = new TypeDefinition("Windows.Win32", "PInvokeCore",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var drag = new MethodDefinition("DragAcceptFiles",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            drag.Parameters.Add(new ParameterDefinition(hwnd));
            drag.Parameters.Add(new ParameterDefinition(bl));
            var dil = drag.Body.GetILProcessor();
            dil.Emit(OpCodes.Ret);
            pinvoke.Methods.Add(drag);
            // Generic wrapper taking the control directly (mirrors the fork:
            // the original body calls this, and the pass must still pin the
            // raw (HWND, BOOL) overload for its verifiable tail).
            var dragGen = new MethodDefinition("DragAcceptFiles",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Void);
            var genT = new GenericParameter("T", dragGen);
            dragGen.GenericParameters.Add(genT);
            dragGen.Parameters.Add(new ParameterDefinition(genT));
            dragGen.Parameters.Add(new ParameterDefinition(bl));
            var gdil = dragGen.Body.GetILProcessor();
            gdil.Emit(OpCodes.Ret);
            pinvoke.Methods.Add(dragGen);
            module.Types.Add(pinvoke);

            var box = new TypeDefinition("System.Windows.Forms", "RichTextBox",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.TypeSystem.Object);
            var target = new MethodDefinition("UpdateOleCallback",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            // Fake original shape: COM acquisition noise, then the tail call.
            var til = target.Body.GetILProcessor();
            til.Emit(OpCodes.Ldnull);
            til.Emit(OpCodes.Pop);
            til.Emit(OpCodes.Ldarg_0);
            til.Emit(OpCodes.Ldc_I4_0);
            if (callGeneric)
            {
                var dragGenRef = new GenericInstanceMethod(module.ImportReference(dragGen));
                dragGenRef.GenericArguments.Add(module.TypeSystem.Object);
                til.Emit(OpCodes.Call, dragGenRef);
            }
            else
            {
                til.Emit(OpCodes.Call, module.ImportReference(drag));
            }
            til.Emit(OpCodes.Ret);
            box.Methods.Add(target);
            module.Types.Add(box);
            asm.Write(tempOut);
        }
    }

    private static string NeutralizeCopy(string src, string tag)
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"{tag}_{Guid.NewGuid()}.dll");
        // Buffer fully first: Cecil keeps the read stream open for lazy
        // metadata, which self-locks a same-path write.
        byte[] bytes = File.ReadAllBytes(src);
        string? dir = Path.GetDirectoryName(src);
        using var resolver = new FrameworkResolver(dir, [TestPaths.Repo(@".artifacts\runtime"), TestPaths.Repo(@"external-apps\paintnet-support")]);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using (var ms = new MemoryStream(bytes))
        using (var read = AssemblyDefinition.ReadAssembly(ms, readerParams))
        {
            AssemblyRewriter.NeutralizeRichEditOleCallback(read.MainModule);
            read.Write(tmp);
        }
        return tmp;
    }

    private static void AssertNeutralShape(MethodDefinition target)
    {
        var instrs = target.Body.Instructions;
        Assert.Equal(7, instrs.Count);
        Assert.Equal(OpCodes.Ldarg_0, instrs[0].OpCode);
        Assert.Equal(OpCodes.Call, instrs[1].OpCode);
        Assert.Equal("get_Handle",
            Assert.IsAssignableFrom<MethodReference>(instrs[1].Operand).Name);
        Assert.Equal(OpCodes.Newobj, instrs[2].OpCode);
        Assert.Equal(".ctor",
            Assert.IsAssignableFrom<MethodReference>(instrs[2].Operand).Name);
        Assert.Equal("Windows.Win32.Foundation.HWND",
            Assert.IsAssignableFrom<MethodReference>(instrs[2].Operand).DeclaringType.FullName);
        Assert.Equal(OpCodes.Ldc_I4_0, instrs[3].OpCode);
        Assert.Equal(OpCodes.Newobj, instrs[4].OpCode);
        Assert.Equal("Windows.Win32.Foundation.BOOL",
            Assert.IsAssignableFrom<MethodReference>(instrs[4].Operand).DeclaringType.FullName);
        Assert.Equal(OpCodes.Call, instrs[5].OpCode);
        var operand = Assert.IsAssignableFrom<MethodReference>(instrs[5].Operand);
        Assert.Equal("DragAcceptFiles", operand.Name);
        Assert.Equal(2, operand.Parameters.Count);
        Assert.Equal("Windows.Win32.Foundation.HWND", operand.Parameters[0].ParameterType.FullName);
        Assert.Equal("Windows.Win32.Foundation.BOOL", operand.Parameters[1].ParameterType.FullName);
        Assert.Equal(OpCodes.Ret, instrs[6].OpCode);
        Assert.Empty(target.Body.ExceptionHandlers);
    }

    [Fact]
    public void Redirect_Synthetic_OleCallback_Keeps_Only_DragAccept_Tail()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"OleSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut, callGeneric: true);
        string? neutral = null;
        try
        {
            neutral = NeutralizeCopy(tempOut, "OleNeutral");
            using var read = AssemblyDefinition.ReadAssembly(neutral);
            var target = read.MainModule.GetType("System.Windows.Forms.RichTextBox").Methods
                .First(m => m.Name == "UpdateOleCallback");
            AssertNeutralShape(target);
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (neutral is not null) { try { File.Delete(neutral); } catch { } }
        }
    }

    [Fact]
    public void Neutralized_Body_Jits_Without_InvalidProgram()
    {
        string tempOut = Path.Combine(Path.GetTempPath(), $"OleSynth_{Guid.NewGuid()}.dll");
        BuildSynthetic(tempOut, callGeneric: false);
        string? neutral = null;
        try
        {
            neutral = NeutralizeCopy(tempOut, "OleJit");
            var alc = new AssemblyLoadContext($"OleJit_{Guid.NewGuid()}", isCollectible: true);
            try
            {
                var loaded = alc.LoadFromAssemblyPath(neutral);
                var box = loaded.GetType("System.Windows.Forms.RichTextBox");
                Assert.NotNull(box);
                var target = box.GetMethod("UpdateOleCallback",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(target);
                // Throws InvalidProgramException while the tail passes a
                // class/int where HWND/BOOL are expected (OOB-89A).
                RuntimeHelpers.PrepareMethod(target.MethodHandle);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            if (neutral is not null) { try { File.Delete(neutral); } catch { } }
        }
    }

    [Fact]
    public void Redirect_Real_Fork_OleCallback_Keeps_Only_DragAccept_Tail()
    {
        string src = TestPaths.Repo(@".artifacts\runtime\WfWasm.WinForms.dll");
        string? tmp = null;
        try
        {
            tmp = NeutralizeCopy(src, "OleReal");
            using var read = AssemblyDefinition.ReadAssembly(tmp);
            var target = read.MainModule.GetType("System.Windows.Forms.RichTextBox").Methods
                .First(m => m.Name == "UpdateOleCallback" && m.Parameters.Count == 0);
            AssertNeutralShape(target);
        }
        finally
        {
            if (tmp is not null) { try { File.Delete(tmp); } catch { } }
        }
    }

    [Fact]
    public void Redirect_Missing_Member_Fails_Loudly()
    {
        var asmName = new AssemblyNameDefinition("OleEmpty", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "OleEmpty", ModuleKind.Dll))
        {
            Assert.Throws<InvalidOperationException>(() =>
                AssemblyRewriter.NeutralizeRichEditOleCallback(asm.MainModule));
        }
    }
}
