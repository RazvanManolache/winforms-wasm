extern alias WfDraw;

using WfPdnDiagnostics = WfDraw::WfWasm.Drawing.PdnDiagnostics;
using Xunit;

public sealed class PdnDiagnosticsTests
{
    [Fact]
    public void Fallback_Is_Total_And_Explicitly_Degraded()
    {
        // Total function: never throws, deterministic shape, explicit note
        // that only the reported error (composed by PDN's caller) is
        // authoritative — the environment section is degraded, not faked.
        string a = WfPdnDiagnostics.GetErrorDetailsFallback(null, false);
        string b = WfPdnDiagnostics.GetErrorDetailsFallback(new object(), true);
        Assert.StartsWith("[WASM headless diagnostics]", a);
        Assert.StartsWith("[WASM headless diagnostics]", b);
        Assert.Contains("only the reported error above is authoritative", a);
        Assert.Contains("Runtime: ", b);
        Assert.Contains("Processors: ", b);
        Assert.DoesNotContain("Processors: ", a);
    }
}
