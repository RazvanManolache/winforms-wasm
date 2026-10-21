extern alias Fork;

using WasmIdentity = Fork::System.Windows.Forms.WasmAppIdentity;

namespace WfWasm.WinForms.Tests;

// Browser BaseDirectory contract: the rewriter points AppDomain +
// AppContext get_BaseDirectory at the shim. Wherever the runtime has a
// real answer (desktop, tests) it passes through untouched; only a
// null/empty runtime answer (browser: assemblies load from bytes)
// falls back to "/app". The fallback half cannot run on desktop
// (BaseDirectory is never empty here); the ERP Purchases probe
// (erpdata-verify.py) pins it in the browser.
public sealed class WasmAppIdentityTests
{
    [Fact]
    public void BaseDirectory_Passes_Real_Value_Through()
    {
        string real = System.AppContext.BaseDirectory;
        Assert.False(string.IsNullOrEmpty(real));
        Assert.Equal(real, WasmIdentity.BaseDirectory());
    }

    [Fact]
    public void BaseDirectory_Instance_Shape_Agrees()
    {
        string real = System.AppDomain.CurrentDomain.BaseDirectory;
        Assert.False(string.IsNullOrEmpty(real));
        Assert.Equal(real, WasmIdentity.BaseDirectory(System.AppDomain.CurrentDomain));
        Assert.Equal(WasmIdentity.BaseDirectory(), WasmIdentity.BaseDirectory(new object()));
    }
}
