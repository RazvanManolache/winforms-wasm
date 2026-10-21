using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using WfWasm.Rewrite;
using Xunit;

// Pins the GetScrollBarInfo neutralizer case: the rewritten stub must
// materialize a null SCROLLBARINFO.rgstate (native CCHILDREN_SCROLLBAR layout,
// 6 DWORDs) while still returning FALSE. Desktop fills the buffer via the
// native marshaller; headless readers (Syncfusion
// ScrollersFrame.UpdateScrollerSettings ldelem's rgstate unconditionally)
// NRE on the virgin struct. Zeros mean "no state flags", matching the vendor
// defaults the pre-handle bail takes on desktop.
public sealed class ScrollBarInfoStubTests
{
    private static string BuildNative(string tempOut)
    {
        var asmName = new AssemblyNameDefinition("ReproNative", new Version(1, 0, 0, 0));
        using (var asm = AssemblyDefinition.CreateAssembly(asmName, "ReproNative", ModuleKind.Dll))
        {
            ModuleDefinition module = asm.MainModule;
            var sbi = new TypeDefinition("ReproNative", "SCROLLBARINFO",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.SequentialLayout
                    | Mono.Cecil.TypeAttributes.Class | Mono.Cecil.TypeAttributes.Sealed,
                module.ImportReference(typeof(ValueType)));
            sbi.Fields.Add(new FieldDefinition("cbSize", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.Int32));
            sbi.Fields.Add(new FieldDefinition("rgstate", Mono.Cecil.FieldAttributes.Public,
                new ArrayType(module.TypeSystem.Int32)));
            module.Types.Add(sbi);

            var native = new TypeDefinition("ReproNative", "NativeMethods",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            var modref = new ModuleReference("user32.dll");
            module.ModuleReferences.Add(modref);

            var scroll = new MethodDefinition("GetScrollBarInfo",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static
                    | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.PInvokeImpl,
                module.TypeSystem.Int32);
            scroll.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
            scroll.Parameters.Add(new ParameterDefinition(module.TypeSystem.UInt32));
            scroll.Parameters.Add(new ParameterDefinition(new ByReferenceType(sbi)));
            scroll.PInvokeInfo = new PInvokeInfo(
                Mono.Cecil.PInvokeAttributes.CallConvWinapi, "GetScrollBarInfo", modref);
            scroll.IsPInvokeImpl = true;
            native.Methods.Add(scroll);

            var other = new MethodDefinition("UnrelatedApi",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static
                    | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.PInvokeImpl,
                module.TypeSystem.Int32);
            other.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
            other.PInvokeInfo = new PInvokeInfo(
                Mono.Cecil.PInvokeAttributes.CallConvWinapi, "UnrelatedApi", modref);
            other.IsPInvokeImpl = true;
            native.Methods.Add(other);

            module.Types.Add(native);
            asm.Write(tempOut);
        }
        return tempOut;
    }

    private static (Assembly alc, string dst) LoadStubbed(string src)
    {
        string dst = Path.Combine(Path.GetTempPath(), $"ReproNativeOut_{Guid.NewGuid()}.dll");
        AssemblyRewriter.StubPInvokes(src, dst);
        var alc = new AssemblyLoadContext($"ReproNative_{Guid.NewGuid()}", isCollectible: true);
        return (alc.LoadFromAssemblyPath(dst), dst);
    }

    [Fact]
    public void GetScrollBarInfo_Materializes_Rgstate_Still_False()
    {
        string src = Path.Combine(Path.GetTempPath(), $"ReproNative_{Guid.NewGuid()}.dll");
        BuildNative(src);
        var (loaded, dst) = LoadStubbed(src);
        try
        {
            var native = loaded.GetType("ReproNative.NativeMethods");
            Assert.NotNull(native);
            var sbiT = loaded.GetType("ReproNative.SCROLLBARINFO");
            Assert.NotNull(sbiT);
            var rgField = sbiT.GetField("rgstate");
            Assert.NotNull(rgField);
            var target = native.GetMethod("GetScrollBarInfo");
            Assert.NotNull(target);

            object? sbi = Activator.CreateInstance(sbiT)!;
            object?[] args = [IntPtr.Zero, 0u, sbi];
            object? ret = target.Invoke(null, args);
            Assert.Equal(0, ret);
            var rg = (Array?)rgField.GetValue(args[2]);
            Assert.NotNull(rg);
            Assert.Equal(6, rg.Length);
            foreach (object? v in rg)
                Assert.Equal(0, v);
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(dst); } catch { }
        }
    }

    [Fact]
    public void GetScrollBarInfo_Preserves_Prefilled_Rgstate()
    {
        string src = Path.Combine(Path.GetTempPath(), $"ReproNative_{Guid.NewGuid()}.dll");
        BuildNative(src);
        var (loaded, dst) = LoadStubbed(src);
        try
        {
            var native = loaded.GetType("ReproNative.NativeMethods");
            Assert.NotNull(native);
            var sbiT = loaded.GetType("ReproNative.SCROLLBARINFO");
            Assert.NotNull(sbiT);
            var rgField = sbiT.GetField("rgstate");
            Assert.NotNull(rgField);
            var target = native.GetMethod("GetScrollBarInfo");
            Assert.NotNull(target);

            object? sbi = Activator.CreateInstance(sbiT)!;
            rgField.SetValue(sbi, new[] { 9, 9, 9, 9, 9, 9 });
            object?[] args = [IntPtr.Zero, 0u, sbi];
            object? ret = target.Invoke(null, args);
            Assert.Equal(0, ret);
            var rg = (int[]?)rgField.GetValue(args[2]);
            Assert.NotNull(rg);
            Assert.All(rg, v => Assert.Equal(9, v));
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(dst); } catch { }
        }
    }

    [Fact]
    public void Unrelated_PInvoke_Keeps_Default_Stub()
    {
        string src = Path.Combine(Path.GetTempPath(), $"ReproNative_{Guid.NewGuid()}.dll");
        BuildNative(src);
        var (loaded, dst) = LoadStubbed(src);
        try
        {
            var native = loaded.GetType("ReproNative.NativeMethods");
            Assert.NotNull(native);
            var target = native.GetMethod("UnrelatedApi");
            Assert.NotNull(target);
            Assert.Equal(0, target.Invoke(null, [IntPtr.Zero]));
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(dst); } catch { }
        }
    }
}
