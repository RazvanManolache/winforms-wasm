using Mono.Cecil;
using WfWasm.Rewrite;

namespace WfWasm.Rewrite.Tests;

// Pins the rev81 DrawText neutralizer case on the real vendor shapes:
// Syncfusion declares app-private gdi32 DrawText P/Invokes in four
// flavours (NativeMethods/RECT lowercase fields, NativePaint/RECT
// PascalCase fields, ComboBoxEx/RECT, Win32API.RECT), all
// (IntPtr, String, Int32, RECT&, Int32) -> Int32. Each must retarget
// to the fork WasmGdiText.DrawText bridge (which decomposes the rect
// through the signature's own struct); ExtTextOut keeps the generic
// stub until its own milestone. Reads the pristine vendor binaries
// in place (never rewritten on disk).
public sealed class DrawTextRetargetTests
{
    private static readonly string VendorDir = TestPaths.Repo(@"external-apps\syncfusion\syncfusion-treeview");

    private static AssemblyDefinition? LoadVendor(string file)
    {
        string path = Path.Combine(VendorDir, file);
        if (!File.Exists(path)) return null;
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static void AssertBridgeCall(TypeDefinition type, string bridgeName)
    {
        var method = type.Methods.First(m => m.Name == "DrawText");
        Assert.False(method.IsPInvokeImpl);
        var call = method.Body.Instructions
            .Select(i => i.Operand as MethodReference)
            .Single(mr => mr is not null && mr.Name == bridgeName);
        Assert.Equal("WasmGdiText", call!.DeclaringType.Name);
        Assert.Equal("System.Windows.Forms", call.DeclaringType.Scope.Name);
        Assert.Equal(10, call.Parameters.Count);
    }

    [Fact]
    public void StubPInvokes_SharedBase_DrawText_Retargets()
    {
        using var asm = LoadVendor("Syncfusion.Shared.Base.dll");
        if (asm is null) return;
        AssemblyRewriter.StubModulePInvokes(asm.MainModule);

        // Lowercase RECT fields.
        var native = asm.MainModule.GetType("Syncfusion.Runtime.InteropServices.NativeMethods");
        Assert.NotNull(native);
        AssertBridgeCall(native, "DrawText");

        // PascalCase RECT fields via a nested struct from another class.
        var core = asm.MainModule.GetType("Syncfusion.WinForms.Core.NativeMethods");
        Assert.NotNull(core);
        AssertBridgeCall(core, "DrawText");
    }

    [Fact]
    public void StubPInvokes_ToolsWindows_DrawText_Retargets()
    {
        using var asm = LoadVendor("Syncfusion.Tools.Windows.dll");
        if (asm is null) return;
        AssemblyRewriter.StubModulePInvokes(asm.MainModule);

        var native = asm.MainModule.GetType("Syncfusion.Runtime.InteropServices.NativeMethods");
        Assert.NotNull(native);
        AssertBridgeCall(native, "DrawText");

        var combo = asm.MainModule.GetType("Syncfusion.Windows.Forms.Tools.ComboBoxEx");
        Assert.NotNull(combo);
        AssertBridgeCall(combo, "DrawText");

        var api = asm.MainModule.GetType("Syncfusion.Windows.Forms.Tools.Win32API.WindowsAPI");
        Assert.NotNull(api);
        AssertBridgeCall(api, "DrawText");
    }

    [Fact]
    public void StubPInvokes_ExtTextOut_Keeps_Generic_Stub()
    {
        foreach (string file in new[] { "Syncfusion.Shared.Base.dll", "Syncfusion.Tools.Windows.dll" })
        {
            using var asm = LoadVendor(file);
            if (asm is null) continue;
            AssemblyRewriter.StubModulePInvokes(asm.MainModule);
            var native = asm.MainModule.GetType("Syncfusion.Runtime.InteropServices.NativeMethods");
            Assert.NotNull(native);
            var textOut = native.Methods.First(m => m.Name == "ExtTextOut");
            Assert.DoesNotContain(
                textOut.Body.Instructions,
                i => (i.Operand as MethodReference)?.DeclaringType?.Name == "WasmGdiText");
        }
    }
}
