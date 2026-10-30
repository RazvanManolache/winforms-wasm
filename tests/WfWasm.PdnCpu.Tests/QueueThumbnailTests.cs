using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint.QueueThumbnail: builds the ready-handler delegate via reflection
// (no generic tokens in IL) and replicates the manager queue body with
// non-generic operations (the 3-arg manager call itself cannot be
// reflection-Invoked headless). Proves the contract the strip queue-site
// reroute relies on.
public sealed class QueueThumbnailTests
{
    public sealed class FakeManager
    {
        public readonly FakeQueue renderQueue = new();
        public readonly object updateLock = new();

        public sealed class ThumbnailQueueItem
        {
            public object Provider { get; set; }
            public object Callback { get; set; }
            public int SideLength { get; set; }
            public System.DateTime EnqueueTimeUtc { get; set; }
        }

        public sealed class FakeQueue : System.Collections.Generic.List<object>
        {
            public new void Enqueue(object item) => Add(item);
        }

        // Metadata only: provides the closed-delegate parameter type for
        // CreateDelegate. Never invoked (the helper replicates the body).
        public void QueueThumbnailUpdate(object provider, int size, System.EventHandler handler)
        {
            throw new System.NotImplementedException();
        }

        public readonly System.Collections.Generic.List<string> DrainCalls = new();
        public readonly System.Collections.Generic.List<object> Rendered = new();

        public void RenderThreadLoop()
        {
            if (renderQueue.Count > 0)
            {
                Rendered.Add(renderQueue[0]);
                renderQueue.RemoveAt(0);
            }
        }

        public void DrainThumbnailReadyInvokeList()
        {
            DrainCalls.Add("drain");
        }
    }

    public sealed class FakeStrip
    {
        public int Handled;
        public void OnThumbnailUpdated(object sender, System.EventArgs e) { Handled++; }
    }

    [Fact]
    public void QueueThumbnail_Enqueues_Item_With_Handler()
    {
        var m = new FakeManager();
        var s = new FakeStrip();
        var provider = new object();
        var before = System.DateTime.UtcNow;
        CpuPaint.QueueThumbnail(m, provider, 50, s, "OnThumbnailUpdated");
        // The helper drives a deterministic render+drain pass inline, so
        // the queue is empty and the drain ran by the time it returns.
        Assert.Equal(0, m.renderQueue.Count);
        Assert.Equal(new[] { "drain" }, m.DrainCalls);
        var item = Assert.IsType<FakeManager.ThumbnailQueueItem>(Assert.Single(m.Rendered));
        Assert.Same(provider, item.Provider);
        Assert.Equal(50, item.SideLength);
        Assert.True(item.EnqueueTimeUtc >= before);
        Assert.NotNull(item.Callback);
        ((System.EventHandler)item.Callback)(s, System.EventArgs.Empty);
        Assert.Equal(1, s.Handled);
    }

    [Fact]
    public void QueueThumbnail_Nulls_And_Bad_Size_Throw()
    {
        var m = new FakeManager();
        var s = new FakeStrip();
        Assert.Throws<System.ArgumentNullException>(() => CpuPaint.QueueThumbnail(null, new object(), 50, s, "OnThumbnailUpdated"));
        Assert.Throws<System.ArgumentNullException>(() => CpuPaint.QueueThumbnail(m, null, 50, s, "OnThumbnailUpdated"));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => CpuPaint.QueueThumbnail(m, new object(), 0, s, "OnThumbnailUpdated"));
        Assert.Throws<System.ArgumentNullException>(() => CpuPaint.QueueThumbnail(m, new object(), 50, null, "OnThumbnailUpdated"));
        Assert.Throws<System.ArgumentNullException>(() => CpuPaint.QueueThumbnail(m, new object(), 50, s, null));
    }

    [Fact]
    public void QueueThumbnail_Missing_Method_Throws()
    {
        var m = new FakeManager();
        var s = new FakeStrip();
        Assert.Throws<System.InvalidOperationException>(() => CpuPaint.QueueThumbnail(m, new object(), 50, s, "NoSuchHandler"));
    }
}
