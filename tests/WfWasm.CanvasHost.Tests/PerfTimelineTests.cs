using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

// Perf item 4 timeline storage: rings, wrap accounting, dump shape.
// Pure-C# parts only (no JS runtime); the browser battery proves joins.
public sealed class PerfTimelineTests
{
    [Fact]
    public void Disabled_ByDefault()
    {
        Assert.False(PerfTimeline.Enabled);
    }

    [Fact]
    public void RecordInput_RingWraps_WithOverflow()
    {
        PerfTimeline.Reset();
        for (int i = 1; i <= PerfTimeline.InputCap + 5; i++)
            PerfTimeline.RecordInput(new PerfTimeline.InputSample { Seq = i, Kind = PerfTimeline.KindDown });
        Assert.Equal(PerfTimeline.InputCap + 5, PerfTimeline.InputCount);
        Assert.Equal(5, PerfTimeline.InputOverflow);
        string json = PerfTimeline.DumpJson();
        Assert.Contains("\"inputOverflow\":5", json);
        Assert.Contains("\"inputCount\":" + (PerfTimeline.InputCap + 5), json);
        PerfTimeline.Reset();
        Assert.Equal(0, PerfTimeline.InputCount);
    }

    [Fact]
    public void RecordFrame_SeqOrder_PreservedAfterWrap()
    {
        PerfTimeline.Reset();
        for (int i = 1; i <= PerfTimeline.FrameCap + 3; i++)
            PerfTimeline.RecordFrame(new PerfTimeline.FrameSample { Seq = i, Class = PerfTimeline.ClassGlobal });
        Assert.Equal(3, PerfTimeline.FrameOverflow);
        string json = PerfTimeline.DumpJson();
        // Oldest retained sample first (seq 4), newest last.
        int first = json.IndexOf("\"Seq\":4,", StringComparison.Ordinal);
        int last = json.IndexOf("\"Seq\":" + (PerfTimeline.FrameCap + 3) + ",", StringComparison.Ordinal);
        Assert.True(first >= 0, "oldest retained frame missing");
        Assert.True(last > first, "frame order not preserved");
        Assert.DoesNotContain("\"Seq\":1,", json);
        PerfTimeline.Reset();
    }

    [Fact]
    public void DumpJson_Header_Shape()
    {
        PerfTimeline.Reset();
        PerfTimeline.RecordInput(new PerfTimeline.InputSample { Seq = 7, Kind = PerfTimeline.KindKeyDown, KeyHash = 1 });
        PerfTimeline.RecordFrame(new PerfTimeline.FrameSample { Seq = 9, Class = PerfTimeline.ClassFull, Fault = 0 });
        string json = PerfTimeline.DumpJson();
        Assert.Contains("\"enabled\":false", json);
        Assert.Contains("\"stopwatchFrequency\":", json);
        Assert.Contains("\"inputs\":", json);
        Assert.Contains("\"frames\":", json);
        PerfTimeline.Reset();
    }

    [Fact]
    public void UploadSerialize_Accumulate_AndDrain()
    {
        PerfTimeline.Reset();
        PerfTimeline.AddUpload(100);
        PerfTimeline.AddUpload(50);
        PerfTimeline.AddSerialize(1000, 2, 200);
        PerfTimeline.TakeUploadSerialize(
            out long upBytes, out int upCount, out long jsonChars, out long op7, out long b64);
        Assert.Equal(150, upBytes);
        Assert.Equal(2, upCount);
        Assert.Equal(1000, jsonChars);
        Assert.Equal(2, op7);
        Assert.Equal(200, b64);
        // Drained exactly once.
        PerfTimeline.TakeUploadSerialize(
            out long upBytes2, out int upCount2, out _, out _, out _);
        Assert.Equal(0, upBytes2);
        Assert.Equal(0, upCount2);
        PerfTimeline.Reset();
    }

    [Fact]
    public void Stamp_Monotonic()
    {
        long a = PerfTimeline.Stamp();
        long b = PerfTimeline.Stamp();
        Assert.True(b >= a);
        Assert.True(PerfTimeline.Ms(b - a) >= 0);
    }
}
