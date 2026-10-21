namespace WfWasm.WinForms.Tests;

// Headless mouse state: Control.MousePosition/MouseButtons poll the OS
// cursor, frozen at (0,0)/None in the browser. The host feeds live state
// into WasmMouseState; drag logic polling the statics must see it.
// Regression: Telerik photo drag ignored (photo never moved despite
// delivered press/moves/release).
public sealed class MouseStateTests
{
    [Fact]
    public void MousePosition_Reflects_Host_Feed()
    {
        System.Windows.Forms.WasmMouseState.Update(582, 326, System.Windows.Forms.MouseButtons.None);
        var pos = System.Windows.Forms.Control.MousePosition;
        Assert.Equal(582, pos.X);
        Assert.Equal(326, pos.Y);
    }

    [Fact]
    public void Modifiers_Reflects_Key_Feed()
    {
        // Keyboard events feed modifiers only (no position); menu and
        // shortcut filters read them through Control.ModifierKeys.
        System.Windows.Forms.WasmMouseState.SetModifiers(System.Windows.Forms.Keys.Alt);
        Assert.Equal(System.Windows.Forms.Keys.Alt, System.Windows.Forms.Control.ModifierKeys);
        System.Windows.Forms.WasmMouseState.SetModifiers(System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Control);
        Assert.Equal(System.Windows.Forms.Keys.Shift | System.Windows.Forms.Keys.Control, System.Windows.Forms.Control.ModifierKeys);
        System.Windows.Forms.WasmMouseState.SetModifiers(System.Windows.Forms.Keys.None);
        Assert.Equal(System.Windows.Forms.Keys.None, System.Windows.Forms.Control.ModifierKeys);
    }

    [Fact]
    public void MouseButtons_Reflects_Host_Feed()
    {
        System.Windows.Forms.WasmMouseState.Update(0, 0, System.Windows.Forms.MouseButtons.Left);
        Assert.Equal(System.Windows.Forms.MouseButtons.Left, System.Windows.Forms.Control.MouseButtons);
        System.Windows.Forms.WasmMouseState.Update(0, 0, System.Windows.Forms.MouseButtons.None);
        Assert.Equal(System.Windows.Forms.MouseButtons.None, System.Windows.Forms.Control.MouseButtons);
    }
}
