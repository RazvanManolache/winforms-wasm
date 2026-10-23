using System.Threading;

namespace System.Windows.Forms;

// Perf item 4 cross-assembly frame state. The fork (this assembly) owns
// these because WinFormsAppHost writes them during Paint while the
// CanvasHost pump reads them after Paint returns; the fork cannot
// reference CanvasHost types, so the shared state lives here. Plain
// Interlocked writes; reset at each frame open (W-1). The CanvasHost
// PerfTimeline rings keep the durable samples.
public static class WasmFrameCounters
{
    // Perf item 4 session gate mirror (set by the CanvasHost pump when
    // it enables PerfTimeline; keeps the per-frame/per-layer marks to
    // one bool read when off).
    public static bool PerfEnabled;

    // Claimed at frame open (drain start).
    public static int FrameSeq;
    public static int SceneEpoch;
    public static int FrameGlobal;
    public static int FrameDirtyCount;
    public static int CsFrom;
    public static int CsTo;

    // Layout-drain ticks accumulated during the open frame (S5 split:
    // UpdateLayout vs raster). Timed around the Paint-start drain.
    public static long DrainTicks;

    public static void AddDrainTicks(long ticks) => Interlocked.Add(ref DrainTicks, ticks);

    // App-work ticks (S5 split, render side): the host times
    // EnsureElementLayout + OnPaint invokes per raster and adds them
    // here; ResetFrame zeroes. Extract/record/store cost is then
    // recordTicks minus drainTicks minus onPaintTicks.
    public static long OnPaintTicks;

    public static void AddOnPaintTicks(long ticks) => Interlocked.Add(ref OnPaintTicks, ticks);

    // TPF layout-inside-paint split: the host times EnsureElementLayout
    // per raster; paintS minus ensureS is invoke/region/alloc time.
    public static long EnsureLayoutTicks;

    public static void AddEnsureLayoutTicks(long ticks) => Interlocked.Add(ref EnsureLayoutTicks, ticks);

    // Invoke split: the host times the two reflection OnPaint invokes
    // per raster; paintS minus layS minus invokeS is alloc/Clear/region.
    public static long PaintInvokeTicks;

    public static void AddPaintInvokeTicks(long ticks) => Interlocked.Add(ref PaintInvokeTicks, ticks);

    // Background/foreground split: the host times the background
    // invoke alone; foreground = PaintInvokeTicks minus BgInvokeTicks.
    public static long BgInvokeTicks;

    public static void AddBgInvokeTicks(long ticks) => Interlocked.Add(ref BgInvokeTicks, ticks);

    // Text-measure volume (suspect 1): every WasmTextBridge
    // MeasureExtent call notes calls+chars; table misses note a
    // fallback (Bitmap scratch + sync JSImport pair). Snapshotted
    // per frame like the dirty marks (drain-time measures count
    // toward the frame they precede).
    public static long TextMeasureCalls;
    public static long TextMeasureChars;
    public static long TextMeasureFallbacks;

    public static void NoteTextMeasure(int calls, int chars, int fallbacks)
    {
        Interlocked.Add(ref TextMeasureCalls, calls);
        Interlocked.Add(ref TextMeasureChars, chars);
        Interlocked.Add(ref TextMeasureFallbacks, fallbacks);
    }

    // Layer outcomes (W-2) and byte counters (W-3) for the open frame.
    public static long NRasterFull;
    public static long NRasterRegion;
    public static long NBlitClean;
    public static long NScrollShift;
    public static long RasterPx;
    public static long BytesExtracted;
    public static long BytesHashed;
    public static long BytesCloned;

    // Full-layer hash verdicts (change-detection proof for the caret
    // overlay: consecutive full repaints that hash-hit changed zero
    // pixels, so skipping their invoke is pixel-identical).
    public static long NLayerHashHit;
    public static long NLayerHashMiss;

    public static void CountLayerHashHit(bool hit)
    {
        if (hit)
            Interlocked.Increment(ref NLayerHashHit);
        else
            Interlocked.Increment(ref NLayerHashMiss);
    }

    public static int ClaimFrame() => Interlocked.Increment(ref FrameSeq);

    public static int ClaimInput() => Interlocked.Increment(ref InputSeq);

    public static int InputSeq;

    private static readonly object s_pendingGate = new();
    private static int s_pendFrom;
    private static int s_pendTo;
    private static bool s_pendAny;

    public static void NotePending(int seq)
    {
        lock (s_pendingGate)
        {
            if (!s_pendAny)
            {
                s_pendFrom = seq;
                s_pendTo = seq;
                s_pendAny = true;
            }
            else
            {
                if (seq < s_pendFrom) s_pendFrom = seq;
                if (seq > s_pendTo) s_pendTo = seq;
            }
        }
    }

    public static void TakePending(out int from, out int to)
    {
        lock (s_pendingGate)
        {
            from = s_pendAny ? s_pendFrom : 0;
            to = s_pendAny ? s_pendTo : 0;
            s_pendAny = false;
        }
    }

    public static void CountRasterFull(long px)
    {
        Interlocked.Increment(ref NRasterFull);
        Interlocked.Add(ref RasterPx, px);
    }

    public static void CountRasterRegion(long px)
    {
        Interlocked.Increment(ref NRasterRegion);
        Interlocked.Add(ref RasterPx, px);
    }

    public static void CountBlitClean() => Interlocked.Increment(ref NBlitClean);

    // Retained scroll-shift reuse: frames that moved the retained
    // surface in place and repainted only the narrowed union instead
    // of a full raster.
    public static void CountScrollShift() => Interlocked.Increment(ref NScrollShift);

    // Mark-source attribution (who keeps a frame dirty). Counts every
    // mark call; the first marked control + rect per frame is kept
    // (type name + w/h, -1 = whole/null rect, -2 = no mark yet).
    public static long MarksGlobal;
    public static long MarksControl;
    public static string? FirstMarkType;
    public static int FirstMarkW = -2;
    public static int FirstMarkH = -2;

    public static void NoteGlobal() => Interlocked.Increment(ref MarksGlobal);

    public static void NoteMark(Control? control, int w, int h)
    {
        Interlocked.Increment(ref MarksControl);
        if (FirstMarkType is null && control is not null)
        {
            FirstMarkType = control.GetType().Name;
            FirstMarkW = w;
            FirstMarkH = h;
        }
    }

    // Frame-open snapshot (W-1): what was pending when the frame
    // opened. Marks keep accumulating after this for the next frame.
    public static long OpenMarksGlobal;
    public static long OpenMarksControl;
    public static long OpenTextMeasureCalls;
    public static long OpenTextMeasureChars;
    public static long OpenTextMeasureFallbacks;
    public static string? OpenFirstMarkType;
    public static int OpenFirstMarkW = -2;
    public static int OpenFirstMarkH = -2;

    public static void TakeMarks()
    {
        OpenMarksGlobal = Interlocked.Exchange(ref MarksGlobal, 0);
        OpenMarksControl = Interlocked.Exchange(ref MarksControl, 0);
        OpenTextMeasureCalls = Interlocked.Exchange(ref TextMeasureCalls, 0);
        OpenTextMeasureChars = Interlocked.Exchange(ref TextMeasureChars, 0);
        OpenTextMeasureFallbacks = Interlocked.Exchange(ref TextMeasureFallbacks, 0);
        OpenFirstMarkType = FirstMarkType;
        OpenFirstMarkW = FirstMarkW;
        OpenFirstMarkH = FirstMarkH;
        FirstMarkType = null;
        FirstMarkW = -2;
        FirstMarkH = -2;
    }

    public static void AddBytesExtracted(long n) => Interlocked.Add(ref BytesExtracted, n);

    // Extract/hash/store per-frame ticks (profiling P1): closes the
    // record-minus-drain-minus-onpaint remainder bucket. Callers gate
    // on PerfEnabled (one static read when off); methods unconditional.
    public static long ExtractTicks;
    public static long HashTicks;
    public static long StoreTicks;

    public static void AddExtractTicks(long ticks) => Interlocked.Add(ref ExtractTicks, ticks);

    public static void AddHashTicks(long ticks) => Interlocked.Add(ref HashTicks, ticks);

    public static void AddStoreTicks(long ticks) => Interlocked.Add(ref StoreTicks, ticks);

    public static void AddBytesHashed(long n) => Interlocked.Add(ref BytesHashed, n);

    public static void AddBytesCloned(long n) => Interlocked.Add(ref BytesCloned, n);

    public static void ResetFrame()
    {
        DrainTicks = 0;
        OnPaintTicks = 0;
        EnsureLayoutTicks = 0;
        PaintInvokeTicks = 0;
        BgInvokeTicks = 0;
        ExtractTicks = 0;
        HashTicks = 0;
        StoreTicks = 0;
        FrameGlobal = 0;
        FrameDirtyCount = 0;
        CsFrom = 0;
        CsTo = 0;
        NRasterFull = 0;
        NRasterRegion = 0;
        NBlitClean = 0;
        NScrollShift = 0;
        RasterPx = 0;
        BytesExtracted = 0;
        BytesHashed = 0;
        BytesCloned = 0;
        NLayerHashHit = 0;
        NLayerHashMiss = 0;
    }
}
