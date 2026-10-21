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
    public void Capture_Loss_Notifies_On_Release()
    {
        using var control = new System.Windows.Forms.Control();
        _ = control.Handle; // force headless handle creation
        int fired = 0;
        control.MouseCaptureChanged += (_, _) => fired++;
        try
        {
            control.Capture = true;
            Assert.Equal(0, fired); // acquiring never notifies
            control.Capture = false;
            Assert.Equal(1, fired);
        }
        finally
        {
            Windows.Win32.PInvoke.ReleaseCapture();
        }
    }

    [Fact]
    public void Capture_Loss_Notifies_On_Steal()
    {
        using var first = new System.Windows.Forms.Control();
        using var second = new System.Windows.Forms.Control();
        _ = first.Handle;
        _ = second.Handle;
        int firstFired = 0, secondFired = 0;
        first.MouseCaptureChanged += (_, _) => firstFired++;
        second.MouseCaptureChanged += (_, _) => secondFired++;
        try
        {
            first.Capture = true;
            second.Capture = true; // steals: first loses
            Assert.Equal(1, firstFired);
            Assert.Equal(0, secondFired);
            second.Capture = false;
            Assert.Equal(1, secondFired);
        }
        finally
        {
            Windows.Win32.PInvoke.ReleaseCapture();
        }
    }

    [Fact]
    public void SetCapture_Returns_Previous_Hwnd()
    {
        var first = new Windows.Win32.Foundation.HWND(4321);
        var second = new Windows.Win32.Foundation.HWND(8765);
        try
        {
            Assert.Equal(0, (nint)Windows.Win32.PInvoke.SetCapture(first));
            Assert.Equal(first.Value, (nint)Windows.Win32.PInvoke.SetCapture(second));
        }
        finally
        {
            Windows.Win32.PInvoke.ReleaseCapture();
        }
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
