namespace WfWasm.WinForms.Tests;

// Headless registry contract: opens return null, reads return defaults,
// writes are no-ops, counts are zero. The rewriter retargets PDN's
// Registry/RegistryKey calls here; these tests lock the stub semantics.
// Regression: NRE in UIHelpers.GetWindowColorizationInfo <- PdnToolBar
// paint on every PDN frame (Registry.CurrentUser is null in browser).
public sealed class WasmRegistryTests
{
    [Fact]
    public void OpenSubKey_Returns_Null()
    {
        Assert.Null(System.Windows.Forms.WasmRegistry.OpenSubKey(null, "SOFTWARE\\Microsoft\\Windows\\DWM"));
        Assert.Null(System.Windows.Forms.WasmRegistry.OpenSubKey(null, "x", writable: true));
    }

    [Fact]
    public void GetValue_Returns_Default()
    {
        Assert.Null(System.Windows.Forms.WasmRegistry.GetValue(null, "AccentColor"));
        Assert.Equal(42, System.Windows.Forms.WasmRegistry.GetValue((Microsoft.Win32.RegistryKey?)null, "AccentColor", 42));
        Assert.Equal("d", System.Windows.Forms.WasmRegistry.GetValue("key", "v", "d"));
    }

    [Fact]
    public void Writes_Are_NoOps()
    {
        var ex = Record.Exception(() =>
        {
            System.Windows.Forms.WasmRegistry.SetValue((Microsoft.Win32.RegistryKey?)null, "v", 1);
            System.Windows.Forms.WasmRegistry.DeleteValue(null, "v");
            System.Windows.Forms.WasmRegistry.DeleteSubKey(null, "s");
            System.Windows.Forms.WasmRegistry.Close(null);
            System.Windows.Forms.WasmRegistry.Flush(null);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Enumerations_Are_Empty()
    {
        Assert.Empty(System.Windows.Forms.WasmRegistry.GetSubKeyNames(null));
        Assert.Empty(System.Windows.Forms.WasmRegistry.GetValueNames(null));
        Assert.Equal(0, System.Windows.Forms.WasmRegistry.get_ValueCount(null));
        Assert.Equal(Microsoft.Win32.RegistryValueKind.Unknown,
            System.Windows.Forms.WasmRegistry.GetValueKind(null, "v"));
    }
}
