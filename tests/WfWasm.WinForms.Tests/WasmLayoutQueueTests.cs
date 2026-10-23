using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

public sealed class WasmLayoutQueueTests
{
    [Fact]
    public void Post_Then_Drain_RunsFifo_AndBounded()
    {
        WasmLayoutQueue.Clear();
        var order = new List<int>();
        using var control = new Control();
        for (int i = 0; i < 5; i++)
        {
            int n = i;
            WasmLayoutQueue.Post(control, (Action)(() => order.Add(n)), null);
        }

        Assert.Equal(5, WasmLayoutQueue.PendingCount);
        Assert.Equal(2, WasmLayoutQueue.Drain(2));
        Assert.Equal(new[] { 0, 1 }, order);
        Assert.Equal(3, WasmLayoutQueue.Drain());
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, order);
        Assert.Equal(0, WasmLayoutQueue.PendingCount);
    }

    [Fact]
    public void TakePostCaller_Names_Requester_And_Resets()
    {
        WasmLayoutQueue.Clear();
        WasmFrameCounters.PerfEnabled = true;
        try
        {
            WasmLayoutQueue.TakePostCaller();
            using var control = new Control();
            WasmLayoutQueue.Post(control, (Action)(() => { }), null);
            string? caller = WasmLayoutQueue.TakePostCaller();
            Assert.NotNull(caller);
            Assert.Null(WasmLayoutQueue.TakePostCaller());
            WasmLayoutQueue.Drain();
        }
        finally
        {
            WasmFrameCounters.PerfEnabled = false;
            WasmLayoutQueue.TakePostCaller();
        }
    }

    [Fact]
    public void Post_Coalesces_Identical_Queued_Twins()
    {
        WasmLayoutQueue.Clear();
        int wakes = 0;
        void OnWake() => wakes++;
        WasmRepaintGate.RepaintRequested += OnWake;
        try
        {
            using var control = new Control();
            int runs = 0;
            // Parameter-shaped like the real UpdateLayoutCallback twin
            // (manager arg): a parameterless lambda would fault in
            // DynamicInvoke and never run.
            Action<object?> callback = _ => runs++;
            object manager = new();
            WasmLayoutQueue.Post(control, callback, new object?[] { manager });
            Assert.Equal(1, wakes);
            // Identical twin while queued: coalesced, no extra wake.
            WasmLayoutQueue.Post(control, callback, new object?[] { manager });
            Assert.Equal(1, wakes);
            Assert.Equal(1, WasmLayoutQueue.PendingCount);
            // Distinct manager: separate work, separate wake.
            WasmLayoutQueue.Post(control, callback, new object?[] { new object() });
            Assert.Equal(2, wakes);
            Assert.Equal(2, WasmLayoutQueue.PendingCount);
            Assert.Equal(2, WasmLayoutQueue.Drain());
            Assert.Equal(2, runs);
            // After the drain the twin slot is free again.
            WasmLayoutQueue.Post(control, callback, new object?[] { manager });
            Assert.Equal(1, WasmLayoutQueue.PendingCount);
        }
        finally
        {
            WasmRepaintGate.RepaintRequested -= OnWake;
            WasmLayoutQueue.Clear();
        }
    }

    [Fact]
    public void Drain_Contains_CallbackExceptions()
    {
        WasmLayoutQueue.Clear();
        bool ran = false;
        using var control = new Control();
        WasmLayoutQueue.Post(control, (Action)(() => throw new InvalidOperationException("boom")), null);
        WasmLayoutQueue.Post(control, (Action)(() => ran = true), null);

        Assert.Equal(2, WasmLayoutQueue.Drain());
        Assert.True(ran);
    }

    [Fact]
    public void Counters_TrackPostedDrainedFaulted()
    {
        WasmLayoutQueue.Clear();
        long posted0 = WasmLayoutQueue.TotalPosted;
        long drained0 = WasmLayoutQueue.TotalDrained;
        long faulted0 = WasmLayoutQueue.TotalFaulted;
        using var control = new Control();
        WasmLayoutQueue.Post(control, (Action)(() => { }), null);
        WasmLayoutQueue.Post(control, (Action)(() => throw new InvalidOperationException("counted")), null);
        WasmLayoutQueue.Drain();

        Assert.Equal(posted0 + 2, WasmLayoutQueue.TotalPosted);
        Assert.Equal(drained0 + 2, WasmLayoutQueue.TotalDrained);
        Assert.Equal(faulted0 + 1, WasmLayoutQueue.TotalFaulted);
    }

    [Fact]
    public void Post_Coalesce_Increments_TotalCoalesced()
    {
        WasmLayoutQueue.Clear();
        long before = WasmLayoutQueue.TotalCoalesced;
        using var control = new Control();
        Action<object?> callback = _ => { };
        object manager = new();
        WasmLayoutQueue.Post(control, callback, new object?[] { manager });
        WasmLayoutQueue.Post(control, callback, new object?[] { manager });
        Assert.Equal(before + 1, WasmLayoutQueue.TotalCoalesced);
        // The coalesced twin never reaches the queue.
        Assert.Equal(1, WasmLayoutQueue.PendingCount);
        Assert.Equal(1, WasmLayoutQueue.Drain());
        WasmLayoutQueue.Clear();
    }

    [Fact]
    public void Drain_Attributes_Invalidation_To_Callback()
    {
        WasmLayoutQueue.Clear();
        bool saved = WasmFrameCounters.PerfEnabled;
        WasmLayoutQueue.TakeCallbackInvalidated();
        try
        {
            WasmFrameCounters.PerfEnabled = true;
            using var control = new Control();
            WasmLayoutQueue.Post(control, (Action)(() => WasmRepaintGate.MarkGlobalDirty()), null);
            WasmLayoutQueue.Post(control, (Action)(() => { }), null);
            Assert.Equal(2, WasmLayoutQueue.Drain());
            var inv = WasmLayoutQueue.TakeCallbackInvalidated();
            // The dirtying callback is attributed (marks > 0); the no-op
            // callback issued no marks and is absent.
            Assert.Single(inv);
            Assert.True(inv.Values.Single() >= 1);
            WasmLayoutQueue.Clear();
        }
        finally
        {
            WasmFrameCounters.PerfEnabled = saved;
            WasmLayoutQueue.TakeCallbackInvalidated();
        }
    }

    [Fact]
    public void Post_PassesArgs_AndWakesRepaint()
    {
        WasmLayoutQueue.Clear();
        int wakes = 0;
        void OnWake() => wakes++;
        WasmRepaintGate.RepaintRequested += OnWake;
        try
        {
            object? seen = null;
            using var control = new Control();
            WasmLayoutQueue.Post(control, (Action<object?>)(a => seen = a), new object?[] { "arg" });
            Assert.True(wakes >= 1);
            WasmLayoutQueue.Drain();
            Assert.Equal("arg", seen);
        }
        finally
        {
            WasmRepaintGate.RepaintRequested -= OnWake;
            WasmLayoutQueue.Clear();
        }
    }
}
