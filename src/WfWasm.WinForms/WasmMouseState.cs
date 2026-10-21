namespace System.Windows.Forms;

// Headless mouse state: desktop Control.MousePosition / Control.MouseButtons
// poll the OS cursor (GetCursorPos / GetKeyState), which sit frozen at
// (0,0) / None in the browser, so drag logic polling the statics instead
// of event args never sees the pointer move. The canvas host feeds every
// mouse event here (scene px, the fork's "screen space"; ScreenToClient /
// ClientToScreen / MapWindowPoints invert it through the control-chain
// walk in WasmScreenMapping), and the fork's PInvoke stubs read it back.
// Regression: Telerik photo drag ignored — press, moves and release all
// delivered, photo never moved.
public static class WasmMouseState
{
    private static int s_x;
    private static int s_y;
    private static int s_buttons; // System.Windows.Forms.MouseButtons bits
    private static int s_modifiers; // System.Windows.Forms.Keys bits (Shift/Control/Alt)

    public static System.Drawing.Point Position =>
        new(System.Threading.Volatile.Read(ref s_x), System.Threading.Volatile.Read(ref s_y));

    public static MouseButtons Buttons =>
        (MouseButtons)System.Threading.Volatile.Read(ref s_buttons);

    public static Keys Modifiers =>
        (Keys)System.Threading.Volatile.Read(ref s_modifiers);

    public static void Update(int x, int y, MouseButtons buttons) =>
        Update(x, y, buttons, (Keys)System.Threading.Volatile.Read(ref s_modifiers));

    public static void Update(int x, int y, MouseButtons buttons, Keys modifiers)
    {
        System.Threading.Volatile.Write(ref s_x, x);
        System.Threading.Volatile.Write(ref s_y, y);
        System.Threading.Volatile.Write(ref s_buttons, (int)buttons);
        System.Threading.Volatile.Write(ref s_modifiers, (int)(modifiers & Keys.Modifiers));
    }

    // Keyboard-only refresh: key events carry no position, but the Alt/Ctrl/
    // Shift flags they carry are the live truth for ModifierKeys readers
    // (menu/shortcut message filters). Mouse paths keep using Update.
    public static void SetModifiers(Keys modifiers)
    {
        System.Threading.Volatile.Write(ref s_modifiers, (int)(modifiers & Keys.Modifiers));
    }

    public static void Reset()
    {
        System.Threading.Volatile.Write(ref s_x, 0);
        System.Threading.Volatile.Write(ref s_y, 0);
        System.Threading.Volatile.Write(ref s_buttons, 0);
        System.Threading.Volatile.Write(ref s_modifiers, 0);
    }
}
