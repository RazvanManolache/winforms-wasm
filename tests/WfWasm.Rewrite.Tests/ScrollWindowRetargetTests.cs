using Mono.Cecil;
using WfWasm.Rewrite;

namespace WfWasm.Rewrite.Tests;

// Pins the rev289 ScrollWindowEx retarget on the real vendor shape:
// Syncfusion.WinForms.Core.NativeMethods declares app-private
// user32 ScrollWindowEx(nint, int, int, RECT&, RECT&, nint, RECT&,
// int) -> bool, consumed by SfScrollControl.ScrollWindow (the
// SfDataGrid TableControl scroll path). It must retarget to the fork
// WasmScrollWindow.Scroll bridge (scroll+clip rects decomposed,
// 11-arg call mirroring the shim); CreateRectRgn keeps the generic
// sentinel stub.
public sealed class ScrollWindowRetargetTests
{
    private const string VendorDir = @"E:/tmp/wfwasm-syncfusion-apps/syncfusion-datagrid";

    private static AssemblyDefinition? LoadVendor(string file)
    {
        string path = Path.Combine(VendorDir, file);
        if (!File.Exists(path)) return null;
        return AssemblyDefinition.ReadAssembly(path);
    }

    [Fact]
    public void StubPInvokes_SharedBase_ScrollWindowEx_Retargets()
    {
        using var asm = LoadVendor("Syncfusion.Shared.Base.dll");
        if (asm is null) return;
        AssemblyRewriter.StubModulePInvokes(asm.MainModule);

        var native = asm.MainModule.GetType("Syncfusion.WinForms.Core.NativeMethods");
        Assert.NotNull(native);
        var scroll = native.Methods.First(m => m.Name == "ScrollWindowEx");
        Assert.False(scroll.IsPInvokeImpl);
        var call = scroll.Body.Instructions
            .Select(i => i.Operand as MethodReference)
            .SingleOrDefault(mr => mr is not null && mr.Name == "Scroll");
        Assert.NotNull(call);
        Assert.Equal("WasmScrollWindow", call!.DeclaringType.Name);
        Assert.Equal("System.Windows.Forms", call.DeclaringType.Namespace);
        Assert.Equal(11, call.Parameters.Count);
        Assert.Equal("System.IntPtr", call.Parameters[0].ParameterType.FullName);
        foreach (var p in call.Parameters.Skip(1))
            Assert.Equal("System.Int32", p.ParameterType.FullName);
        Assert.Equal("System.Boolean", call.ReturnType.FullName);
    }

    [Fact]
    public void Fork_WasmScrollWindow_Shape_Matches_Retarget()
    {
        // Guards emitter/shim drift (a 12-arg emit against the 11-arg
        // shim dies at call time inside the vendor try/catch, showing
        // the old silent no-op). Reads the staged fork in place.
        string staged = @"E:/tmp/rewritten-small/WfWasm.WinForms.dll";
        if (!File.Exists(staged)) return;
        using var fork = AssemblyDefinition.ReadAssembly(staged);
        var shim = fork.MainModule.GetType("System.Windows.Forms.WasmScrollWindow");
        Assert.NotNull(shim);
        var scroll = shim.Methods.First(m => m.Name == "Scroll");
        Assert.Equal("System.Boolean", scroll.ReturnType.FullName);
        Assert.Equal(11, scroll.Parameters.Count);
        Assert.Equal("System.IntPtr", scroll.Parameters[0].ParameterType.FullName);
        foreach (var p in scroll.Parameters.Skip(1))
            Assert.Equal("System.Int32", p.ParameterType.FullName);
    }

    [Fact]
    public void StubPInvokes_SharedBase_CreateRectRgn_Keeps_Generic_Stub()
    {
        using var asm = LoadVendor("Syncfusion.Shared.Base.dll");
        if (asm is null) return;
        AssemblyRewriter.StubModulePInvokes(asm.MainModule);

        var native = asm.MainModule.GetType("Syncfusion.WinForms.Core.NativeMethods");
        Assert.NotNull(native);
        var rgn = native.Methods.First(m => m.Name == "CreateRectRgn");
        Assert.False(rgn.IsPInvokeImpl);
        Assert.DoesNotContain(
            rgn.Body.Instructions,
            i => (i.Operand as MethodReference)?.DeclaringType?.Name == "WasmScrollWindow");
    }
}
