namespace WfWasm.WinForms.Tests;

// Scroll-shift record (retained scroll reuse): a scrolled control records
// its pixel delta via NoteScroll; the next frame consumes it once via
// TakeScrollShift (single-frame TTL). Null controls and zero deltas are
// ignored; a second scroll before the frame accumulates (still a pure
// shift). Dropped silently when the frame cannot use it.
public sealed class ScrollShiftGateTests
{
    [Fact]
    public void Take_Empty_ReturnsFalse_Zeroed()
    {
        using var control = new System.Windows.Forms.Control();
        Assert.False(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out int dx, out int dy));
        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void Note_Take_RoundTrips_And_Consumes()
    {
        using var control = new System.Windows.Forms.Control();
        try
        {
            System.Windows.Forms.WasmRepaintGate.NoteScroll(control, 10, 0);
            Assert.True(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out int dx, out int dy));
            Assert.Equal(10, dx);
            Assert.Equal(0, dy);
            // Single-frame TTL: a second take finds nothing.
            Assert.False(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out _, out _));
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out _, out _);
        }
    }

    [Fact]
    public void SecondNote_BeforeTake_Accumulates()
    {
        using var control = new System.Windows.Forms.Control();
        try
        {
            System.Windows.Forms.WasmRepaintGate.NoteScroll(control, 10, 0);
            System.Windows.Forms.WasmRepaintGate.NoteScroll(control, 5, 20);
            Assert.True(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out int dx, out int dy));
            Assert.Equal(15, dx);
            Assert.Equal(20, dy);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out _, out _);
        }
    }

    [Fact]
    public void Null_And_Zero_Ignored()
    {
        using var control = new System.Windows.Forms.Control();
        System.Windows.Forms.WasmRepaintGate.NoteScroll(null, 10, 0);
        System.Windows.Forms.WasmRepaintGate.NoteScroll(control, 0, 0);
        Assert.False(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(control, out _, out _));
    }

    [Fact]
    public void OtherControl_Unaffected()
    {
        using var a = new System.Windows.Forms.Control();
        using var b = new System.Windows.Forms.Control();
        try
        {
            System.Windows.Forms.WasmRepaintGate.NoteScroll(a, 0, 30);
            Assert.False(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(b, out _, out _));
            Assert.True(System.Windows.Forms.WasmRepaintGate.TakeScrollShift(a, out int dx, out int dy));
            Assert.Equal(0, dx);
            Assert.Equal(30, dy);
        }
        finally
        {
            System.Windows.Forms.WasmRepaintGate.TakeScrollShift(a, out _, out _);
        }
    }
}
