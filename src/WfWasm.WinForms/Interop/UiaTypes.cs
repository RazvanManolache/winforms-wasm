// Fork-owned: UiaRect and UiaPoint structs for UI Automation.

using System.Drawing;

namespace Windows.Win32.UI.Accessibility;

internal partial struct UiaPoint
{
    public double x;
    public double y;
}

internal partial struct UiaRect
{
    public double left;
    public double top;
    public double width;
    public double height;

    public static implicit operator Rectangle(UiaRect value) =>
        new((int)value.left, (int)value.top, (int)value.width, (int)value.height);

    public static implicit operator UiaRect(Rectangle value) =>
        new() { left = value.Left, top = value.Top, width = value.Width, height = value.Height };
}
