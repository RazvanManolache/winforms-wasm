namespace System.Drawing;

// Perf item 4 raster-traffic counters (WfWasm.Drawing side). The fork
// assembly cannot be referenced from here (it references Drawing, so
// the reverse edge would be circular), which is why this is a
// standalone gate plus Interlocked counters instead of fork notes. The
// CanvasHost pump enables it alongside PerfTimeline and drains it per
// frame with Take (snapshot + reset): layout drain and paint both run
// inside the pump's Paint call, so read-and-reset at sample time is
// exact per-frame accounting with no fork snapshot needed.
public static class TextRasterCounters
{
    public static bool PerfEnabled;

    public static long DrawCalls;
    public static long DrawChars;
    public static long DrawPx;
    public static long DrawTicks;
    public static long RasterCalls;
    public static long RasterHits;
    public static long RasterMisses;
    public static long MeasureWidthCalls;

    public static void NoteDraw(int chars)
    {
        System.Threading.Interlocked.Increment(ref DrawCalls);
        System.Threading.Interlocked.Add(ref DrawChars, chars);
    }

    public static void NoteRasterCall() => System.Threading.Interlocked.Increment(ref RasterCalls);

    public static void NoteRasterHit() => System.Threading.Interlocked.Increment(ref RasterHits);

    public static void NoteRasterMiss() => System.Threading.Interlocked.Increment(ref RasterMisses);

    public static void NoteMeasureWidthCall() => System.Threading.Interlocked.Increment(ref MeasureWidthCalls);

    // Op census (uncounted-GDI-volume question): fills, image blits,
    // strokes. Same gate, same Take drain as the text traffic.
    public static long FillCalls;
    public static long FillPx;
    public static long ImageCalls;
    public static long ImagePx;
    public static long LineCalls;

    public static void NoteFill() => System.Threading.Interlocked.Increment(ref FillCalls);

    public static void NoteFillPx(long px) => System.Threading.Interlocked.Add(ref FillPx, px);

    public static void NoteImage() => System.Threading.Interlocked.Increment(ref ImageCalls);

    public static void NoteImagePx(long px) => System.Threading.Interlocked.Add(ref ImagePx, px);

    public static void NoteLine() => System.Threading.Interlocked.Increment(ref LineCalls);

    // Big-fill attribution (46M fill-px/frame question): fills over
    // 50K px capture their size plus the first Telerik/app caller on
    // the stack. TakeBigFills drains per frame alongside Take.
    public static long BigFillCount;
    public static long BigFillPx;
    public static string? BigFillCaller;
    public static int BigFillArgb;
    public static int LastClearArgb;

    public static void NoteBigFill(long px, string? caller, int argb)
    {
        System.Threading.Interlocked.Increment(ref BigFillCount);
        System.Threading.Interlocked.Add(ref BigFillPx, px);
        System.Threading.Volatile.Write(ref BigFillArgb, argb);
        if (caller is not null)
            System.Threading.Volatile.Write(ref BigFillCaller, caller);
    }

    public static void TakeBigFills(out long count, out long px, out string? caller, out int argb)
    {
        count = System.Threading.Interlocked.Exchange(ref BigFillCount, 0);
        px = System.Threading.Interlocked.Exchange(ref BigFillPx, 0);
        caller = System.Threading.Interlocked.Exchange(ref BigFillCaller, null);
        argb = System.Threading.Volatile.Read(ref BigFillArgb);
    }

    public static void NoteClear(int argb) => System.Threading.Volatile.Write(ref LastClearArgb, argb);

    // Per-surface clear freshness for the redundant-fill skip, owned by
    // the bitmap itself (a ConditionalWeakTable rides the surface's
    // lifetime: no leaks, no cross-talk, interleaved clears stay
    // independent). Clear records (version-after-bump, argb) on its
    // target; a later flat fill skips iff THAT surface still holds
    // that version with that argb. Every mutation path bumps
    // PaintVersion (see the Bitmap.PaintVersion doc: all Graphics
    // sinks, SetPixel, UnlockBits write-back, image draws), so any
    // intervening write to that surface voids its record. A global
    // bool could not do this: it skipped across bitmaps and after
    // sinks that never staled it. Unconditional by design
    // (correctness-affecting, not measurement). Version wrap (2^32
    // mutations on one bitmap) is accepted: it needs an exact wrap
    // between record and check within one surface life.
    private sealed class FreshInfo
    {
        public int Version;
        public int Argb;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Bitmap, FreshInfo> s_fresh = new();

    public static void NoteClearSurface(Bitmap? target, int version, int argb)
    {
        if (target is null)
            return;
        s_fresh.Remove(target);
        s_fresh.Add(target, new FreshInfo { Version = version, Argb = argb });
    }

    public static bool TrySkipClearFill(Bitmap? target, int argb)
    {
        if (target is null)
            return false;
        if (!s_fresh.TryGetValue(target, out FreshInfo? info) || info is null)
            return false;
        return info.Argb == argb && target.PaintVersion == info.Version;
    }

    internal static string? CaptureTelerikCaller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(1, false);
            foreach (var frame in trace.GetFrames())
            {
                var m = frame.GetMethod();
                string? type = m?.DeclaringType?.FullName;
                if (type is null)
                    continue;
                if (type.Contains("Telerik") || type.Contains("WfWasm") || type.Contains("System.Windows.Forms"))
                    return type + "." + m?.Name;
            }
            return "non-telerik";
        }
        catch { return null; }
    }

    public static void NoteDrawPx(long px) => System.Threading.Interlocked.Add(ref DrawPx, px);

    public static void NoteDrawTicks(long ticks) => System.Threading.Interlocked.Add(ref DrawTicks, ticks);

    public readonly record struct Snapshot(
        long DrawCalls,
        long DrawChars,
        long DrawPx,
        long DrawTicks,
        long RasterCalls,
        long RasterHits,
        long RasterMisses,
        long MeasureWidthCalls,
        long FillCalls,
        long FillPx,
        long ImageCalls,
        long ImagePx,
        long LineCalls);

    // Non-destructive read for per-raster attribution (the host
    // diffs Peek around each raster; the per-frame Take in Home keeps
    // the authoritative totals). Plain volatile reads: single-threaded
    // browser, no reset races mid-frame.
    public static Snapshot Peek() => new(
        System.Threading.Volatile.Read(ref DrawCalls),
        System.Threading.Volatile.Read(ref DrawChars),
        System.Threading.Volatile.Read(ref DrawPx),
        System.Threading.Volatile.Read(ref DrawTicks),
        System.Threading.Volatile.Read(ref RasterCalls),
        System.Threading.Volatile.Read(ref RasterHits),
        System.Threading.Volatile.Read(ref RasterMisses),
        System.Threading.Volatile.Read(ref MeasureWidthCalls),
        System.Threading.Volatile.Read(ref FillCalls),
        System.Threading.Volatile.Read(ref FillPx),
        System.Threading.Volatile.Read(ref ImageCalls),
        System.Threading.Volatile.Read(ref ImagePx),
        System.Threading.Volatile.Read(ref LineCalls));

    public static Snapshot Take() => new(
        System.Threading.Interlocked.Exchange(ref DrawCalls, 0),
        System.Threading.Interlocked.Exchange(ref DrawChars, 0),
        System.Threading.Interlocked.Exchange(ref DrawPx, 0),
        System.Threading.Interlocked.Exchange(ref DrawTicks, 0),
        System.Threading.Interlocked.Exchange(ref RasterCalls, 0),
        System.Threading.Interlocked.Exchange(ref RasterHits, 0),
        System.Threading.Interlocked.Exchange(ref RasterMisses, 0),
        System.Threading.Interlocked.Exchange(ref MeasureWidthCalls, 0),
        System.Threading.Interlocked.Exchange(ref FillCalls, 0),
        System.Threading.Interlocked.Exchange(ref FillPx, 0),
        System.Threading.Interlocked.Exchange(ref ImageCalls, 0),
        System.Threading.Interlocked.Exchange(ref ImagePx, 0),
        System.Threading.Interlocked.Exchange(ref LineCalls, 0));
}
