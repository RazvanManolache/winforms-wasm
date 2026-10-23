using System.Diagnostics;
using System.Text.Json;

namespace WfWasm.CanvasHost;

// Perf item 4: input-to-visible-response timeline. Session-fixed gate,
// default off (the probe enables it through the PerfEnable JS-invokable;
// AppContext switch WfWasm.PerfTimeline also honors native tests).
// Rings store numbers only; kind names resolve at dump. When off, every
// site costs one static-bool read. Clocks: JS domain
// (performance.now anchors) for end-to-end ordering; Stopwatch ticks
// for intra-managed durations only, never subtracted across domains.
public static class PerfTimeline
{
    public const int InputCap = 1024;
    public const int FrameCap = 512;

    // Input kinds (match the canvas-bridge.js kindCodes).
    public const int KindDown = 1;
    public const int KindUp = 2;
    public const int KindMove = 3;
    public const int KindWheel = 4;
    public const int KindKeyDown = 5;
    public const int KindKeyUp = 6;

    // Frame classes.
    public const int ClassEmpty = 0;
    public const int ClassGlobal = 1;
    public const int ClassRegion = 2;
    public const int ClassFull = 3;
    public const int ClassBlitOnly = 4;

    // Frame flags (bitmask).
    public const int FlagTimerCaused = 1;
    public const int FlagResize = 2;
    public const int FlagHasInput = 4;

    public static bool Enabled;

    public struct InputSample
    {
        public int Seq { get; set; }
        public int Kind { get; set; }
        public int JsSeq { get; set; }
        public double TEvent { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int KeyHash { get; set; }
        public double AnchorIn { get; set; }
        public long DispatchTicks { get; set; }
    }

    public struct FrameSample
    {
        public int Seq { get; set; }
        public int SceneEpoch { get; set; }
        public int CsFrom { get; set; }
        public int CsTo { get; set; }
        public int Class { get; set; }
        public int Flags { get; set; }
        public int NCmd { get; set; }
        public int NUploads { get; set; }
        public long NRasterFull { get; set; }
        public long NRasterRegion { get; set; }
        public long NBlit { get; set; }
        public long NScrollShift { get; set; }
        public long RasterPx { get; set; }
        public long UploadBytes { get; set; }
        public long JsonChars { get; set; }
        public long Op7 { get; set; }
        public long Base64Chars { get; set; }
        public long RecordTicks { get; set; }
        public long DrainTicks { get; set; }
        public long OnPaintTicks { get; set; }
        public long TextMeasureCalls { get; set; }
        public long TextMeasureChars { get; set; }
        public long TextMeasureFallbacks { get; set; }
        public long EnsureLayoutTicks { get; set; }
        public long DrawCalls { get; set; }
        public long DrawChars { get; set; }
        public long RasterCalls { get; set; }
        public long RasterHits { get; set; }
        public long RasterMisses { get; set; }
        public long MeasureWidthCalls { get; set; }
        public long PaintInvokeTicks { get; set; }
        public long ExtractTicks { get; set; }
        public long HashTicks { get; set; }
        public long StoreTicks { get; set; }
        public long DrawPx { get; set; }
        public long BgInvokeTicks { get; set; }
        public long DrawTicks { get; set; }
        public long FirstChanceExceptions { get; set; }
        public long SlowLayerTicks { get; set; }
        public string? SlowLayerType { get; set; }
        public long SlowLayerDraws { get; set; }
        public long SlowLayerPx { get; set; }
        public long FillCalls { get; set; }
        public long FillPx { get; set; }
        public long ImageCalls { get; set; }
        public long ImagePx { get; set; }
        public long LineCalls { get; set; }
        public long GcGen0 { get; set; }
        public long GcGen1 { get; set; }
        public long GcGen2 { get; set; }
        public long MemBytes { get; set; }
        public long BigFillCount { get; set; }
        public long BigFillPx { get; set; }
        public string? BigFillCaller { get; set; }
        public int BigFillArgb { get; set; }
        public int LastClearArgb { get; set; }
        public string? PostCaller { get; set; }
        public string? MarkCaller { get; set; }
        public long MarkSamples { get; set; }
        public string? FirstMarkCaller { get; set; }
        public long LayerHashHit { get; set; }
        public long LayerHashMiss { get; set; }
        public long BytesExtracted { get; set; }
        public long BytesHashed { get; set; }
        public long BytesCloned { get; set; }
        public long MarksGlobal { get; set; }
        public long MarksControl { get; set; }
        public string? FirstMarkType { get; set; }
        public int FirstMarkW { get; set; }
        public int FirstMarkH { get; set; }
        public long UploadTicks { get; set; }
        public long SerializeTicks { get; set; }
        public long InvokeTicks { get; set; }
        public int Fault { get; set; }
    }

    private static readonly InputSample[] s_inputs = new InputSample[InputCap];
    private static readonly FrameSample[] s_frames = new FrameSample[FrameCap];
    private static long s_inputCount;
    private static long s_frameCount;

    public static long Stamp() => Stopwatch.GetTimestamp();

    public static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // Single bridge-anchor source for placing managed events on the JS
    // axis (ordering only; never used for managed durations).
    public static double NowAnchor() => CanvasBridge.PerfNowMs();

    // Sequences are claimed in the fork (WasmFrameCounters) so the
    // host drain and the pump share one space; rings only store.
    public static void RecordInput(InputSample sample)
    {
        long n = System.Threading.Interlocked.Increment(ref s_inputCount);
        s_inputs[(n - 1) % InputCap] = sample;
    }

    public static void RecordFrame(FrameSample sample)
    {
        long n = System.Threading.Interlocked.Increment(ref s_frameCount);
        s_frames[(n - 1) % FrameCap] = sample;
    }

    // In-progress frame upload/serialize accumulators (B-2/B-3). The
    // pump fills them between Paint and the frame-sample write; Home
    // drains them when finalizing the sample.
    private static long s_upBytes;
    private static long s_upCount;
    private static long s_jsonChars;
    private static long s_op7;
    private static long s_b64Chars;

    public static void AddUpload(long bytes)
    {
        System.Threading.Interlocked.Add(ref s_upBytes, bytes);
        System.Threading.Interlocked.Increment(ref s_upCount);
    }

    public static void AddSerialize(long jsonChars, long op7, long b64Chars)
    {
        System.Threading.Interlocked.Add(ref s_jsonChars, jsonChars);
        System.Threading.Interlocked.Add(ref s_op7, op7);
        System.Threading.Interlocked.Add(ref s_b64Chars, b64Chars);
    }

    public static void TakeUploadSerialize(
        out long upBytes, out int upCount, out long jsonChars, out long op7, out long b64Chars)
    {
        upBytes = System.Threading.Interlocked.Exchange(ref s_upBytes, 0);
        upCount = (int)System.Threading.Interlocked.Exchange(ref s_upCount, 0);
        jsonChars = System.Threading.Interlocked.Exchange(ref s_jsonChars, 0);
        op7 = System.Threading.Interlocked.Exchange(ref s_op7, 0);
        b64Chars = System.Threading.Interlocked.Exchange(ref s_b64Chars, 0);
    }

    public static long InputCount => System.Threading.Interlocked.Read(ref s_inputCount);
    public static long FrameCount => System.Threading.Interlocked.Read(ref s_frameCount);
    public static long InputOverflow => Math.Max(0, InputCount - InputCap);
    public static long FrameOverflow => Math.Max(0, FrameCount - FrameCap);

    // Test/maintenance reset (rings are session-fixed at runtime).
    public static void Reset()
    {
        Array.Clear(s_inputs, 0, s_inputs.Length);
        Array.Clear(s_frames, 0, s_frames.Length);
        s_inputCount = 0;
        s_frameCount = 0;
        s_upBytes = 0;
        s_upCount = 0;
        s_jsonChars = 0;
        s_op7 = 0;
        s_b64Chars = 0;
    }

    public static string DumpJson()
    {
        long inCount = InputCount;
        long frCount = FrameCount;
        var inputs = new InputSample[Math.Min(inCount, InputCap)];
        for (long i = 0; i < inputs.Length; i++)
            inputs[i] = s_inputs[(inCount - inputs.Length + i) % InputCap];
        var frames = new FrameSample[Math.Min(frCount, FrameCap)];
        for (long i = 0; i < frames.Length; i++)
            frames[i] = s_frames[(frCount - frames.Length + i) % FrameCap];
        return JsonSerializer.Serialize(new
        {
            header = new
            {
                enabled = Enabled,
                inputCount = inCount,
                inputOverflow = InputOverflow,
                frameCount = frCount,
                frameOverflow = FrameOverflow,
                stopwatchFrequency = (long)Stopwatch.Frequency,
            },
            inputs,
            frames,
        });
    }
}
