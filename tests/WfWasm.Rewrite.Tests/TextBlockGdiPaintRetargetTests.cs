using Mono.Cecil;
using WfWasm.Rewrite;

namespace WfWasm.Rewrite.Tests;

public sealed class TextBlockGdiPaintRetargetTests
{
    [Fact]
    public void RetargetTextBlockGdiPaint_PaintElement_CallsDrawGdiPlus()
    {
        // Rev62: document span glyphs paint through TextBlock.DrawGdi
        // (GDI DrawTransparentText needs a DIB section + TextOut +
        // AlphaBlend; none has a headless backend, so every run
        // paints nothing). The sibling DrawGdiPlus path paints the
        // same runs through managed Graphics.DrawString.
        string input = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "telerik", "Telerik UI for WinForms demos", "Bin", "Release",
            "Telerik.WinControls.RichTextEditor.dll"));
        if (!File.Exists(input)) return;

        using var asm = AssemblyDefinition.ReadAssembly(input);
        int sites = AssemblyRewriter.RetargetTextBlockGdiPaint(asm.MainModule);
        Assert.Equal(1, sites);

        var paint = asm.MainModule
            .GetType("Telerik.WinControls.RichTextEditor.UI.TextBlock")
            .Methods.First(m => m.Name == "PaintElement");
        var called = paint.Body.Instructions
            .Select(i => (i.Operand as MethodReference)?.Name)
            .Where(n => n is not null)
            .ToList();
        Assert.Contains("DrawGdiPlus", called);
        Assert.DoesNotContain("DrawGdi", called);
    }
}
