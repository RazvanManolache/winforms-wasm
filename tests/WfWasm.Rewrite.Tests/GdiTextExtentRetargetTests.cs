using Mono.Cecil;
using WfWasm.Rewrite;

namespace WfWasm.Rewrite.Tests;

public sealed class GdiTextExtentRetargetTests
{
    [Fact]
    public void StubPInvokes_NativeTextRenderer_Extents_Retarget_ToForkBridge()
    {
        // Rev61: Telerik's NativeTextRenderer declares private gdi32
        // imports. The generic stub returned 0 without writing the
        // out Size, so RadTextMeasurer cached (0,0) per symbol and
        // every document SpanLayoutBox measured zero: typed glyphs
        // never rastered and TPF layout never converged.
        string input = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "telerik", "Telerik UI for WinForms demos", "Bin", "Release",
            "Telerik.WinControls.RichTextEditor.dll"));
        if (!File.Exists(input)) return;

        using var asm = AssemblyDefinition.ReadAssembly(input);
        AssemblyRewriter.StubModulePInvokes(asm.MainModule);
        var renderer = asm.MainModule.GetType("Telerik.WinControls.NativeTextRenderer");
        Assert.NotNull(renderer);

        AssertBridgeCall(renderer, "GetTextExtentPoint32", "GetTextExtentPoint32");
        AssertBridgeCall(renderer, "GetTextExtentExPoint", "GetTextExtentExPoint");
        AssertBridgeCall(renderer, "SelectObject", "SelectObject");

        // Neighbouring imports keep the generic stub: the retarget is
        // scoped to the extent/font-selection surface only.
        var textOut = renderer.Methods.First(m => m.Name == "TextOut");
        Assert.DoesNotContain(
            textOut.Body.Instructions,
            i => (i.Operand as MethodReference)?.DeclaringType?.Name == "WasmGdiText");
    }

    private static void AssertBridgeCall(TypeDefinition type, string methodName, string bridgeName)
    {
        var method = type.Methods.First(m => m.Name == methodName);
        Assert.False(method.IsPInvokeImpl);
        var call = method.Body.Instructions
            .Select(i => i.Operand as MethodReference)
            .Single(mr => mr is not null && mr.Name == bridgeName);
        Assert.Equal("WasmGdiText", call!.DeclaringType.Name);
        Assert.Equal("System.Windows.Forms", call.DeclaringType.Scope.Name);
    }
}
