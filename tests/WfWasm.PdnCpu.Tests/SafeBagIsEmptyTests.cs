using System.Collections.Concurrent;
using WfWasm.PdnCpu;

namespace WfWasm.PdnCpu.Tests;

// Fail-closed emptiness probe for finalizer-torn concurrent bags (PDN
// SafeProducerConsumerCollection finalizer reads IsEmpty on a disposed
// framework ThreadLocal -> unhandled ODE -> process death). The probe must
// answer "empty" for anything it cannot read instead of throwing.
public sealed class SafeBagIsEmptyTests
{
    private sealed class ThrowingBag
    {
        public bool IsEmpty => throw new ObjectDisposedException("torn");
    }

    [Fact]
    public void Null_Reads_Empty()
    {
        Assert.True(CpuPaint.SafeBagIsEmpty(null));
    }

    [Fact]
    public void Empty_Bag_Reads_Empty()
    {
        Assert.True(CpuPaint.SafeBagIsEmpty(new ConcurrentBag<int>()));
    }

    [Fact]
    public void NonEmpty_Bag_Reads_NonEmpty()
    {
        var bag = new ConcurrentBag<int>();
        bag.Add(7);
        Assert.False(CpuPaint.SafeBagIsEmpty(bag));
    }

    [Fact]
    public void Missing_Getter_Reads_Empty()
    {
        Assert.True(CpuPaint.SafeBagIsEmpty(new object()));
    }

    [Fact]
    public void Throwing_Getter_Reads_Empty()
    {
        Assert.True(CpuPaint.SafeBagIsEmpty(new ThrowingBag()));
    }
}
