using System.Drawing;
using System.Windows.Forms;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Producer side of retained scroll reuse: a real ScrollableControl
// scroll must record its pixel displacement via NoteScroll so the next
// frame can shift retained content (consumed by TryShiftRetained).
// This fails if the fork's SetDisplayRectLocation hook is ever lost.
public sealed class ScrollShiftProducerTests
{
    private sealed class ScrollPanel : Panel
    {
        public void ScrollContentTo(int x, int y) => SetDisplayRectLocation(x, y);
    }

    [Fact]
    public void SetDisplayRectLocation_RecordsPixelDisplacement()
    {
        // No handles: with real desktop windows ScrollWindowEx delivers
        // re-entrant messages (stack overflow in the test runner); the
        // browser stubs it, so handle-free exercises the same managed
        // path the browser runs (NoteScroll + _displayRect, no native).
        using var panel = new ScrollPanel
        {
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 500),
            Size = new Size(200, 200),
        };
        panel.PerformLayout();
        Size display = panel.DisplayRectangle.Size;
        try
        {
            WasmRepaintGate.TakeScrollShift(panel, out _, out _);
            panel.ScrollContentTo(0, -120);
            Point pos = panel.AutoScrollPosition;
            Assert.True(WasmRepaintGate.TakeScrollShift(
                panel, out int dx, out int dy), $"pos={pos} display={display}");
            Assert.Equal(0, dx);
            Assert.Equal(-120, dy);
        }
        finally
        {
            WasmRepaintGate.TakeScrollShift(panel, out _, out _);
        }
    }
}
