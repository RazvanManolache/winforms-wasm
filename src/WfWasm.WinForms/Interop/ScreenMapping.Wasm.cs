// Fork-owned: coherent client<->screen mapping. "Screen space" in the
// browser is the host's scene space: the host feeds every pointer position
// to WasmMouseState in scene coordinates, and this walk inverts it for
// MapWindowPoints / ClientToScreen / ScreenToClient. WindowBoundsStorage
// alone is parent-relative, and the previous MapWindowPoints stub was a
// no-op, so PointToClient/PointToScreen were identity and polling APIs
// (Control.MousePosition via PointFromScreen, e.g. Telerik's diagram hit
// testing) missed every nested control. Regression: RadDiagram clicks and
// drags silently hit nothing (shapes never select).

using System.Drawing;
using Windows.Win32.Foundation;

namespace Windows.Win32;

internal static class WasmScreenMapping
{
    /// <summary>
    ///  Client origin of <paramref name="hWnd"/> in scene ("screen")
    ///  coordinates. Null/unknown windows map to the origin.
    /// </summary>
    internal static Point ClientOrigin(HWND hWnd)
    {
        if (hWnd.IsNull)
        {
            return Point.Empty;
        }

        var control = global::System.Windows.Forms.Control.FromHandle((nint)hWnd.Value);
        if (control is null)
        {
            return Point.Empty;
        }

        int x = 0;
        int y = 0;
        for (var current = control; current != null; current = current.Parent)
        {
            x += current.Location.X;
            y += current.Location.Y;
            if (current is global::System.Windows.Forms.Form form)
            {
                y += ChromeHeight(form);
                break;
            }
        }

        return new Point(x, y);
    }

    // Must match WinFormsAppHost.ChromeHeight: the host paints a 30px bar for
    // captioned forms and none for chromeless ones; client coords start below it.
    private static int ChromeHeight(global::System.Windows.Forms.Form form)
    {
        if (form.FormBorderStyle == global::System.Windows.Forms.FormBorderStyle.None)
        {
            return 0;
        }

        int style = form.IsHandleCreated
            ? global::System.Windows.Forms.WasmWindowLongs.GetWindowStyle(form.Handle)
            : 0;
        return (style & (int)global::Windows.Win32.UI.WindowsAndMessaging.WINDOW_STYLE.WS_CAPTION) != 0 ? 30 : 0;
    }
}
