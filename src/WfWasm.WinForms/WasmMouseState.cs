namespace System.Windows.Forms;

// Headless mouse state: desktop Control.MousePosition / Control.MouseButtons
// poll the OS cursor (GetCursorPos / GetKeyState), which sit frozen at
// (0,0) / None in the browser, so drag logic polling the statics instead
// of event args never sees the pointer move. The canvas host feeds every
// mouse event here (canvas CSS px, which is screen space; ScreenToClient /
// ClientToScreen map through WindowBoundsStorage), and the fork's PInvoke
// stubs read it back. Regression: Telerik photo drag ignored — press,
// moves and release all delivered, photo never moved.
// NOTE: keyboard modifiers (Shift/Ctrl/Alt via GetKeyState) still read 0;
// the host does not feed them yet.
public static class WasmMouseState
{
    private static int s_x;
    private static int s_y;
    private static int s_buttons; // System.Windows.Forms.MouseButtons bits

    public static System.Drawing.Point Position =>
        new(System.Threading.Volatile.Read(ref s_x), System.Threading.Volatile.Read(ref s_y));

    public static MouseButtons Buttons =>
        (MouseButtons)System.Threading.Volatile.Read(ref s_buttons);

    public static void Update(int x, int y, MouseButtons buttons)
    {
        System.Threading.Volatile.Write(ref s_x, x);
        System.Threading.Volatile.Write(ref s_y, y);
        System.Threading.Volatile.Write(ref s_buttons, (int)buttons);
    }
}
