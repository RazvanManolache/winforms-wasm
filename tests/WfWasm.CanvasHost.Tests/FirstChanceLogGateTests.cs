using WfWasm.CanvasHost;

public sealed class FirstChanceLogGateTests
{
    [Fact]
    public void Distinct_Signatures_Log_After_Same_Key_Exhausted()
    {
        // One noisy site (BinaryFormatter image reads) must not mask a
        // distinct error behind a ThreadExceptionDialog (RTE): per-key
        // budgets, not one global counter.
        FirstChanceLogGate.Reset();
        for (int i = 0; i < FirstChanceLogGate.PerSignatureCap; i++)
            Assert.True(FirstChanceLogGate.ShouldLog("noisy"));
        Assert.False(FirstChanceLogGate.ShouldLog("noisy"));
        Assert.True(FirstChanceLogGate.ShouldLog("distinct"));
        FirstChanceLogGate.Reset();
    }

    [Fact]
    public void Global_Cap_Bounds_Pathological_Sessions()
    {
        FirstChanceLogGate.Reset();
        for (int i = 0; i < FirstChanceLogGate.GlobalCap; i++)
            Assert.True(FirstChanceLogGate.ShouldLog("k" + i));
        Assert.False(FirstChanceLogGate.ShouldLog("overflow"));
        FirstChanceLogGate.Reset();
    }
}
