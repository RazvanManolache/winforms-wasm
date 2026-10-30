using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// CpuPaint.RenderAndDrainInline: renders AND drains inline with a
// re-entrancy guard (nested tail calls return immediately; the outer pass
// picks re-queued items up). Proves the contract the queue-tail injection
// relies on: bounded passes, depth 1, queue always empties.
public sealed class RenderDrainInlineTests
{
    private sealed class FakeManager
    {
        public readonly System.Collections.Generic.List<string> Calls = new();
        public readonly System.Collections.Generic.Queue<object> renderQueue = new();
        public bool Reenter;
        private bool _reentered;

        public void RenderThreadLoop()
        {
            Calls.Add("render");
            if (renderQueue.Count > 0)
                renderQueue.Dequeue();
            if (Reenter && !_reentered)
            {
                _reentered = true;
                // A handler re-queueing mid-pass: must not recurse.
                renderQueue.Enqueue(new object());
                CpuPaint.RenderAndDrainInline(this);
                Calls.Add("after-nested");
            }
        }

        public void DrainThumbnailReadyInvokeList()
        {
            Calls.Add("drain");
        }
    }

    [Fact]
    public void Inline_Pass_Renders_And_Drains_Until_Empty()
    {
        var m = new FakeManager();
        m.renderQueue.Enqueue(new object());
        m.renderQueue.Enqueue(new object());
        CpuPaint.RenderAndDrainInline(m);
        Assert.Equal(0, m.renderQueue.Count);
        Assert.Equal(new[] { "render", "drain", "render", "drain" }, m.Calls);
    }

    [Fact]
    public void Nested_Tail_Call_Returns_Without_Recursing()
    {
        var m = new FakeManager { Reenter = true };
        m.renderQueue.Enqueue(new object());
        CpuPaint.RenderAndDrainInline(m);
        Assert.Equal(0, m.renderQueue.Count);
        // Outer pass: render, drain; nested call inside render returns at
        // once (no nested render/drain pair), outer loop picks the
        // re-queued item up.
        Assert.Equal(new[] { "render", "after-nested", "drain", "render", "drain" }, m.Calls);
    }

    [Fact]
    public void Null_Manager_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() => CpuPaint.RenderAndDrainInline(null));
    }
}
