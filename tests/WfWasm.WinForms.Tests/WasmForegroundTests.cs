namespace WfWasm.WinForms.Tests;

// Headless foreground window: desktop Form.ActiveForm reads the OS
// foreground window, frozen at null in the browser (no native activation
// messages ever arrive). The host publishes the active form's minted HWND
// into WasmForeground on every scene switch; the GetForegroundWindow /
// GetActiveWindow stubs read it back.
// Regression: MDI File menu Alt+F — the menu bar gates its mnemonic path
// on Form.ActiveForm, which stayed null forever headless.
public sealed class WasmForegroundTests
{
    [Fact]
    public void Foreground_Set_Clear_RoundTrips()
    {
        try
        {
            System.Windows.Forms.WasmForeground.Set(0x1234);
            Assert.Equal(0x1234, System.Windows.Forms.WasmForeground.Current);
        }
        finally
        {
            System.Windows.Forms.WasmForeground.Clear();
        }
        Assert.Equal(0, System.Windows.Forms.WasmForeground.Current);
    }

    [Fact]
    public void ActiveForm_Reflects_Published_Handle()
    {
        using var form = new System.Windows.Forms.Form();
        nint hwnd = (nint)form.Handle;
        Assert.NotEqual(0, hwnd);
        try
        {
            System.Windows.Forms.WasmForeground.Set(hwnd);
            Assert.Same(form, System.Windows.Forms.Form.ActiveForm);
        }
        finally
        {
            System.Windows.Forms.WasmForeground.Clear();
        }
        Assert.Null(System.Windows.Forms.Form.ActiveForm);
    }
}
