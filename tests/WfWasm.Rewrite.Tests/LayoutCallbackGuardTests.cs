using Mono.Cecil;
using Mono.Cecil.Cil;
using WfWasm.Rewrite;

namespace WfWasm.Rewrite.Tests;

public sealed class LayoutCallbackGuardTests
{
    [Fact]
    public void NeutralizeFile_RadControl_InvokeLayoutCallback_Bypasses_HandleCheck()
    {
        // Rev59: Telerik's ContextLayoutManager posts its measure/arrange
        // pass via RadControl.InvokeLayoutCallback, whose first check
        // drops the post when IsHandleCreated is false (always, headless);
        // TPF layout queues then starve and edited content paints stale
        // layout forever. The check becomes pop+true; BeginInvoke stays.
        string input = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "telerik", "Telerik UI for WinForms demos", "Bin", "Release",
            "Telerik.WinControls.dll"));
        if (!File.Exists(input)) return;
        string tempOut = Path.Combine(Path.GetTempPath(), $"LC_Guard_{Guid.NewGuid()}.dll");
        try
        {
            int sites = AssemblyRewriter.NeutralizeLayoutHandleGuard(input, tempOut);
            Assert.Equal(1, sites);
            using var asm = AssemblyDefinition.ReadAssembly(tempOut);
            var invoke = asm.MainModule.GetType("Telerik.WinControls.RadControl")
                .Methods.First(m => m.Name == "InvokeLayoutCallback");
            var ops = invoke.Body.Instructions.Select(i => i.OpCode.Name).ToList();
            Assert.Equal(new[] { "ldarg.0", "pop", "ldc.i4.1" }, ops.Take(3));
            var called = invoke.Body.Instructions
                .Select(i => (i.Operand as MethodReference)?.Name)
                .Where(n => n is not null)
                .ToList();
            // Handle-less BeginInvoke throws, so rev60 retargets the post
            // to the fork layout queue (host drains it once per frame).
            Assert.Contains("Post", called);
            Assert.DoesNotContain("BeginInvoke", called);
            Assert.Contains("get_Disposing", called);
            Assert.DoesNotContain("get_IsHandleCreated", called);
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }

    [Fact]
    public void NeutralizeFile_WithoutRadControl_IsNoOp()
    {
        // Modules without RadControl stay byte-identical (no round-trip).
        string input = typeof(LayoutCallbackGuardTests).Assembly.Location;
        string tempOut = Path.Combine(Path.GetTempPath(), $"LC_NoOp_{Guid.NewGuid()}.dll");
        try
        {
            int sites = AssemblyRewriter.NeutralizeLayoutHandleGuard(input, tempOut);
            Assert.Equal(0, sites);
            Assert.False(File.Exists(tempOut));
        }
        finally
        {
            try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
        }
    }
}
