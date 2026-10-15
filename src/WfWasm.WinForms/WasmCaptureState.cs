namespace System.Windows.Forms;

// Headless mouse capture: desktop Control.Capture pins all mouse input to
// one control (SetCapture/GetCapture/ReleaseCapture). Persist the capturing
// HWND here so the Capture round-trip works; controls release explicitly,
// matching Win32 (the framework does not auto-release on button-up).
public static class WasmCaptureState
{
    private static nint s_hwnd;

    public static nint Current => System.Threading.Volatile.Read(ref s_hwnd);

    public static void Set(nint hwnd) => System.Threading.Volatile.Write(ref s_hwnd, hwnd);

    public static void Clear() => System.Threading.Volatile.Write(ref s_hwnd, 0);
}
