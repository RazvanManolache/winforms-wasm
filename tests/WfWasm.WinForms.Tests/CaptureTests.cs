namespace WfWasm.WinForms.Tests;

// Headless mouse capture: Control.Capture pins input to one control via
// SetCapture/GetCapture/ReleaseCapture. The fork persists the capturing
// HWND so the round-trip works; without it Capture reads false right
// after being set and drag logic that requires capture never engages.
// Regression: Telerik photo drag ignored.
public sealed class CaptureTests
{
    [Fact]
    public void Capture_RoundTrips_On_Control()
    {
        using var control = new System.Windows.Forms.Control();
        _ = control.Handle; // force headless handle creation
        Assert.False(control.Capture);
        control.Capture = true;
        Assert.True(control.Capture);
        control.Capture = false;
        Assert.False(control.Capture);
    }

    [Fact]
    public void Capture_Stubs_Track_Hwnd()
    {
        var hwnd = new Windows.Win32.Foundation.HWND(1234);
        Windows.Win32.PInvoke.SetCapture(hwnd);
        Assert.Equal(hwnd, Windows.Win32.PInvoke.GetCapture());
        Windows.Win32.PInvoke.ReleaseCapture();
        Assert.True(Windows.Win32.PInvoke.GetCapture().IsNull);
    }
}
