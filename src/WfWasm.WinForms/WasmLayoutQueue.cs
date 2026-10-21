using System.Collections.Concurrent;
using System.Linq;

namespace System.Windows.Forms;

// Posted layout callbacks drained by the host once per frame. Desktop
// drains BeginInvoke posts on the Win32 message loop, which does not
// exist headless; Telerik's TPF layout manager posts its measure/arrange
// pass this way (RadControl.InvokeLayoutCallback, rewritten by rev60 to
// land here). Enqueue wakes the paint pump. Drain runs callbacks FIFO,
// each contained: one faulting callback must not strand the rest or the
// frame.
public static class WasmLayoutQueue
{
    private static readonly ConcurrentQueue<(Delegate Callback, object?[]? Args)> s_queue = new();
    private static long s_totalPosted;
    private static long s_totalDrained;
    private static long s_totalFaulted;

    // Lifetime counters (host diagnostics/tests): a layout callback
    // that faults on every drain (Telerik's UpdateLayout reposts
    // itself from its finally after an exception) is otherwise
    // invisible - the drain must contain it, but not silently.
    public static long TotalPosted => Interlocked.Read(ref s_totalPosted);
    public static long TotalDrained => Interlocked.Read(ref s_totalDrained);
    public static long TotalFaulted => Interlocked.Read(ref s_totalFaulted);

    // Post dedupe: an identical (callback, manager-arg) twin already
    // queued covers this dirt (a drained UpdateLayout pass converges
    // all pending layout, so the dup adds nothing but backlog under
    // the 64-drain cap). Arg0 compares by reference: manager identity
    // is what the drain invokes on. Wake fires only on actual enqueue
    // (the twin's Wake already guarantees a pump frame).
    private static readonly HashSet<(Delegate Callback, object? Arg0)> s_queued = new(PostKeyComparer.Instance);

    private sealed class PostKeyComparer : IEqualityComparer<(Delegate Callback, object? Arg0)>
    {
        public static readonly PostKeyComparer Instance = new();

        public bool Equals((Delegate Callback, object? Arg0) x, (Delegate Callback, object? Arg0) y) =>
            x.Callback.Equals(y.Callback) && ReferenceEquals(x.Arg0, y.Arg0);

        public int GetHashCode((Delegate Callback, object? Arg0) o) =>
            HashCode.Combine(o.Callback, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o.Arg0));
    }

    // Stack-shape twin of Control.BeginInvoke(Delegate, params object[]):
    // the control argument is accepted and ignored (single UI thread
    // headless) and the IAsyncResult slot is answered with null (the
    // rewritten call site pops it).
    public static IAsyncResult? Post(Control control, Delegate callback, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(callback);
        object? arg0 = args is { Length: > 0 } ? args[0] : null;
        lock (s_queued)
        {
            if (!s_queued.Add((callback, arg0)))
                return null;
        }
        s_queue.Enqueue((callback, args));
        Interlocked.Increment(ref s_totalPosted);
        // Perf item 4 dirt-loop attribution: who requests layout.
        // Posts are rare (1-4/frame), so a stack walk here is cheap.
        if (WasmFrameCounters.PerfEnabled)
            NotePostCaller();
        WasmRepaintGate.Wake();
        return null;
    }

    // Last Post requester (type.method), drained per frame in Home
    // via TakePostCaller. Names the perpetual-dirt poster.
    private static string? s_postCaller;

    private static void NotePostCaller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(1, false);
            string? first = null;
            foreach (var frame in trace.GetFrames())
            {
                var m = frame.GetMethod();
                string? type = m?.DeclaringType?.FullName;
                if (type is null || type.StartsWith("System.Windows.Forms.Wasm", StringComparison.Ordinal))
                    continue;
                // Two frames: the rewritten post site (InvokeLayoutCallback)
                // plus its caller (the true dirt source).
                string name = type + "." + m?.Name;
                if (first is null)
                    first = name;
                else
                {
                    Volatile.Write(ref s_postCaller, first + " < " + name);
                    return;
                }
            }
            Volatile.Write(ref s_postCaller, first ?? "non-telerik");
        }
        catch { }
    }

    public static string? TakePostCaller() =>
        Interlocked.Exchange(ref s_postCaller, null);

    public static int PendingCount => s_queue.Count;

    // Perf item 4 dirt-loop attribution: who reposts layout every
    // frame. Counts callback full names while the timeline is on and
    // dumps the top 5 to the console every 25 drained callbacks
    // (the probe saves console lines). Same-assembly gate, no host
    // plumbing needed.
    private static readonly Dictionary<string, long> s_cbCounts = new(StringComparer.Ordinal);

    private static void NoteCallbackName(Delegate callback)
    {
        string name;
        try
        {
            var m = callback.Method;
            name = (m.DeclaringType is null ? "?" : m.DeclaringType.FullName) + "." + m.Name;
        }
        catch { name = "?"; }
        lock (s_cbCounts)
        {
            s_cbCounts.TryGetValue(name, out long n);
            s_cbCounts[name] = n + 1;
        }
        if (TotalDrained % 25 == 0)
            DumpCallbackNames();
    }

    private static void DumpCallbackNames()
    {
        try
        {
            KeyValuePair<string, long>[] top;
            lock (s_cbCounts)
            {
                top = s_cbCounts.OrderByDescending(kv => kv.Value).Take(5).ToArray();
                s_cbCounts.Clear();
            }
            Console.WriteLine("[WasmLayoutQueue] callbacks: " + string.Join(" | ", top.Select(kv => kv.Key + "x" + kv.Value)));
        }
        catch { }
    }

    // Runs at most maxCallbacks; returns how many ran. Posts made during
    // the drain wait for the next frame (desktop posts likewise take a
    // later loop turn), which also bounds the frame.
    public static int Drain(int maxCallbacks = 64)
    {
        int ran = 0;
        while (ran < maxCallbacks && s_queue.TryDequeue(out var work))
        {
            object? warg0 = work.Args is { Length: > 0 } ? work.Args[0] : null;
            lock (s_queued)
            {
                s_queued.Remove((work.Callback, warg0));
            }
            ran++;
            Interlocked.Increment(ref s_totalDrained);
            // Perf item 4 dirt-loop attribution (callback identity).
            if (WasmFrameCounters.PerfEnabled)
                NoteCallbackName(work.Callback);
            try { work.Callback.DynamicInvoke(work.Args); }
            catch (Exception ex)
            {
                // Contained: keep draining. The first faults are logged
                // (capped so a per-frame fault loop cannot flood); the
                // counter keeps the true total.
                long faulted = Interlocked.Increment(ref s_totalFaulted);
                if (faulted <= 10)
                {
                    Exception shown = ex is System.Reflection.TargetInvocationException { InnerException: not null } tie
                        ? tie.InnerException
                        : ex;
                    try { Console.WriteLine($"[WasmLayoutQueue] callback fault #{faulted}: {shown.GetType().Name}: {shown.Message.Split('\n')[0]}"); }
                    catch { }
                }
            }
        }
        return ran;
    }

    public static void Clear()
    {
        while (s_queue.TryDequeue(out _))
        {
        }
        // A cleared twin must not strand later identical posts.
        lock (s_queued)
        {
            s_queued.Clear();
        }
    }
}
