extern alias WasmDraw;

namespace WfWasm.WinForms.Tests;

// Screen mapping: "screen space" in the browser is the host's scene space.
// PointToClient/PointToScreen must invert it through the full control chain
// (MapWindowPoints used to be a no-op and ScreenToClient subtracted only
// the immediate window's bounds, so every polling API — Control.MousePosition
// via PointFromScreen — missed nested controls). Regression: RadDiagram
// clicks and drags silently hit nothing (shapes never select).
public sealed class ScreenMappingTests
{
    [Fact]
    public void PointToClient_Maps_Scene_Coords_Through_Nested_Chain()
    {
        using var form = new System.Windows.Forms.Form
        {
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
        };
        var outer = new System.Windows.Forms.Panel { Location = new WasmDraw::System.Drawing.Point(10, 20), Size = new WasmDraw::System.Drawing.Size(300, 200) };
        var inner = new System.Windows.Forms.Panel { Location = new WasmDraw::System.Drawing.Point(5, 7), Size = new WasmDraw::System.Drawing.Size(100, 60) };
        outer.Controls.Add(inner);
        form.Controls.Add(outer);
        _ = form.Handle;
        _ = outer.Handle;
        _ = inner.Handle;
        form.Location = new WasmDraw::System.Drawing.Point(100, 80);

        Assert.Equal(new WasmDraw::System.Drawing.Point(100, 80), form.PointToScreen(WasmDraw::System.Drawing.Point.Empty));
        Assert.Equal(new WasmDraw::System.Drawing.Point(118, 111), inner.PointToScreen(new WasmDraw::System.Drawing.Point(3, 4)));
        Assert.Equal(new WasmDraw::System.Drawing.Point(3, 4), inner.PointToClient(new WasmDraw::System.Drawing.Point(118, 111)));
        var round = inner.PointToClient(inner.PointToScreen(new WasmDraw::System.Drawing.Point(9, 9)));
        Assert.Equal(new WasmDraw::System.Drawing.Point(9, 9), round);
    }

    [Fact]
    public void MousePosition_Feeds_PointToClient_Consistently()
    {
        using var form = new System.Windows.Forms.Form
        {
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
        };
        var panel = new System.Windows.Forms.Panel { Location = new WasmDraw::System.Drawing.Point(10, 20), Size = new WasmDraw::System.Drawing.Size(300, 200) };
        form.Controls.Add(panel);
        _ = form.Handle;
        _ = panel.Handle;
        form.Location = new WasmDraw::System.Drawing.Point(100, 80);

        System.Windows.Forms.WasmMouseState.Update(118, 111, System.Windows.Forms.MouseButtons.None);
        Assert.Equal(new WasmDraw::System.Drawing.Point(118, 111), System.Windows.Forms.Control.MousePosition);
        Assert.Equal(new WasmDraw::System.Drawing.Point(8, 11), panel.PointToClient(System.Windows.Forms.Control.MousePosition));
    }

    [Fact]
    public void PointToScreen_Includes_Caption_Offset_For_Captioned_Forms()
    {
        using var form = new System.Windows.Forms.Form
        {
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
        };
        _ = form.Handle;
        form.Location = new WasmDraw::System.Drawing.Point(10, 20);

        // Matches WinFormsAppHost.ChromeHeight (30px painted bar): client
        // origin sits below the caption.
        Assert.Equal(new WasmDraw::System.Drawing.Point(10, 50), form.PointToScreen(WasmDraw::System.Drawing.Point.Empty));
        Assert.Equal(WasmDraw::System.Drawing.Point.Empty, form.PointToClient(new WasmDraw::System.Drawing.Point(10, 50)));
    }
}
