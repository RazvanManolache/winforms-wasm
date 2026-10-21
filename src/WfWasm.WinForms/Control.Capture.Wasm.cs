// Headless capture notification: Win32 delivers WM_CAPTURECHANGED to the
// window losing capture, and upstream Control raises MouseCaptureChanged
// only from WmCaptureChanged (Control.cs), which never runs without a
// message pump. WasmCaptureState calls this raiser when capture changes
// hands or is released, mirroring the broadcast-to-loser semantics.
// Regression: calculator "2" dead (its Number_Click hangs on
// MouseCaptureChanged, an upstream designer slip that works on desktop
// only because native capture transitions fire the event around clicks).

namespace System.Windows.Forms;

public unsafe partial class Control
{
    internal void RaiseMouseCaptureChangedWasm() => OnMouseCaptureChanged(EventArgs.Empty);
}
