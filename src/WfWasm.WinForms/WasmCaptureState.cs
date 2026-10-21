namespace System.Windows.Forms;

// Headless mouse capture: desktop Control.Capture pins all mouse input to
// one control (SetCapture/GetCapture/ReleaseCapture). Persist the capturing
// HWND here so the Capture round-trip works; controls release explicitly,
// matching Win32 (the framework does not auto-release on button-up).
//
// Win32 also delivers WM_CAPTURECHANGED to the window LOSING capture (on
// steal and on release); upstream raises MouseCaptureChanged only from
// WmCaptureChanged, which never runs headless. Synthesize the notification
// here, at the single state choke point, so every capture loser is told.
public static class WasmCaptureState
{
    private static nint s_hwnd;

    public static nint Current => System.Threading.Volatile.Read(ref s_hwnd);

    // Returns the previously capturing HWND (Win32 SetCapture semantics).
    public static nint Set(nint hwnd)
    {
        nint previous = System.Threading.Interlocked.Exchange(ref s_hwnd, hwnd);
        if (previous != 0 && previous != hwnd)
        {
            NotifyCaptureLost(previous);
        }

        return previous;
    }

    public static void Clear()
    {
        nint previous = System.Threading.Interlocked.Exchange(ref s_hwnd, 0);
        if (previous != 0)
        {
            NotifyCaptureLost(previous);
        }
    }

    private static void NotifyCaptureLost(nint hwnd)
    {
        try
        {
            Control.FromHandle(hwnd)?.RaiseMouseCaptureChangedWasm();
        }
        catch
        {
            // A throwing event handler must not corrupt capture state;
            // desktop would propagate through WndProc, but the stub is
            // also called from framework internals that cannot unwind.
        }
    }
}
