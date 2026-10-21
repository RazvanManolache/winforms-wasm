using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// Perf item 4 mark-source attribution: first-mark capture + snapshot.
public sealed class WasmFrameCountersTests
{
    [Fact]
    public void TakeMarks_Snapshots_AndResets()
    {
        WasmFrameCounters.PerfEnabled = true;
        try
        {
            WasmFrameCounters.ResetFrame();
            WasmFrameCounters.TakeMarks();
            Assert.Equal(0, WasmFrameCounters.OpenMarksControl);
            Assert.Null(WasmFrameCounters.OpenFirstMarkType);
            using var control = new Control();
            WasmFrameCounters.NoteMark(control, 10, 20);
            WasmFrameCounters.NoteMark(control, -1, -1);
            WasmFrameCounters.NoteGlobal();
            WasmFrameCounters.TakeMarks();
            Assert.Equal(2, WasmFrameCounters.OpenMarksControl);
            Assert.Equal(1, WasmFrameCounters.OpenMarksGlobal);
            Assert.Equal("Control", WasmFrameCounters.OpenFirstMarkType);
            Assert.Equal(10, WasmFrameCounters.OpenFirstMarkW);
            Assert.Equal(20, WasmFrameCounters.OpenFirstMarkH);
            // Live counters reset; accumulation continues after.
            Assert.Equal(0, WasmFrameCounters.MarksControl);
            WasmFrameCounters.NoteMark(control, 1, 1);
            Assert.Equal(1, WasmFrameCounters.MarksControl);
            // S5 split contract: the host times the layout drain before
            // the frame reset and adds it back after (ResetFrame zeroes).
            WasmFrameCounters.ResetFrame();
            Assert.Equal(0, WasmFrameCounters.DrainTicks);
            WasmFrameCounters.AddDrainTicks(7);
            WasmFrameCounters.AddDrainTicks(5);
            Assert.Equal(12, WasmFrameCounters.DrainTicks);
            // Render-side split: OnPaint ticks accumulate live and reset
            // with the frame; text-measure volume snapshots like marks.
            WasmFrameCounters.ResetFrame();
            Assert.Equal(0, WasmFrameCounters.OnPaintTicks);
            WasmFrameCounters.AddOnPaintTicks(9);
            Assert.Equal(9, WasmFrameCounters.OnPaintTicks);
            WasmFrameCounters.TakeMarks();
            WasmFrameCounters.NoteTextMeasure(2, 100, 1);
            WasmFrameCounters.TakeMarks();
            Assert.Equal(2, WasmFrameCounters.OpenTextMeasureCalls);
            Assert.Equal(100, WasmFrameCounters.OpenTextMeasureChars);
            Assert.Equal(1, WasmFrameCounters.OpenTextMeasureFallbacks);
            Assert.Equal(0, WasmFrameCounters.TextMeasureCalls);
            // TPF layout-inside-paint split accumulates live, resets per frame.
            WasmFrameCounters.ResetFrame();
            Assert.Equal(0, WasmFrameCounters.EnsureLayoutTicks);
            WasmFrameCounters.AddEnsureLayoutTicks(11);
            Assert.Equal(11, WasmFrameCounters.EnsureLayoutTicks);
            WasmFrameCounters.ResetFrame();
            Assert.Equal(0, WasmFrameCounters.PaintInvokeTicks);
            WasmFrameCounters.AddPaintInvokeTicks(13);
            Assert.Equal(13, WasmFrameCounters.PaintInvokeTicks);
            WasmFrameCounters.ResetFrame();
            Assert.Equal(0, WasmFrameCounters.BgInvokeTicks);
            WasmFrameCounters.AddBgInvokeTicks(17);
            Assert.Equal(17, WasmFrameCounters.BgInvokeTicks);
            // Profiling P1: extract/hash/store ticks accumulate live,
            // reset with the frame.
            WasmFrameCounters.ResetFrame();
            Assert.Equal(0, WasmFrameCounters.ExtractTicks);
            Assert.Equal(0, WasmFrameCounters.HashTicks);
            Assert.Equal(0, WasmFrameCounters.StoreTicks);
            WasmFrameCounters.AddExtractTicks(3);
            WasmFrameCounters.AddHashTicks(5);
            WasmFrameCounters.AddStoreTicks(7);
            Assert.Equal(3, WasmFrameCounters.ExtractTicks);
            Assert.Equal(5, WasmFrameCounters.HashTicks);
            Assert.Equal(7, WasmFrameCounters.StoreTicks);
        }
        finally
        {
            WasmFrameCounters.PerfEnabled = false;
            WasmFrameCounters.ResetFrame();
            WasmFrameCounters.TakeMarks();
        }
    }
}
