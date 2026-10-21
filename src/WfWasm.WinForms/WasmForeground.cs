namespace System.Windows.Forms;

// Headless foreground window: desktop Form.ActiveForm, GetForegroundWindow
// and GetActiveWindow read the OS foreground window, which sits frozen at
// null in the browser (no native activation messages ever arrive). The
// canvas host owns the only activation signal (scene/app select), so it
// publishes the active top-level form's minted HWND here; the PInvoke
// stubs read it back. Cleared on session teardown so a disposed form is
// never reported as active.
// Regression: MDI File menu Alt+F — the menu bar gates its mnemonic path
// on Form.ActiveForm, which stayed null forever headless.
public static class WasmForeground
{
    private static nint s_hwnd;

    public static nint Current => System.Threading.Volatile.Read(ref s_hwnd);

    public static void Set(nint hwnd) => System.Threading.Volatile.Write(ref s_hwnd, hwnd);

    public static void Clear() => System.Threading.Volatile.Write(ref s_hwnd, 0);
}
