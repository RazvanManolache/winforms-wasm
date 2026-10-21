using WfWasm.CanvasHost;

public sealed class FirstChanceCensusTests
{
    [Fact]
    public void Hook_Idempotent_Take_Resets()
    {
        // Perf item 4 census contract: double-hook is one subscription,
        // Take drains (per-frame accounting in Home).
        WinFormsAppHost.HookFirstChance();
        WinFormsAppHost.HookFirstChance();
        long n = WinFormsAppHost.TakeFirstChance();
        Assert.True(n >= 0);
        Assert.Equal(0, WinFormsAppHost.TakeFirstChance());
    }

    [Fact]
    public void TakeSlow_Resets()
    {
        WinFormsAppHost.TakeSlow(out long ticks, out string? type, out long draws, out long px);
        Assert.True(ticks >= 0);
        WinFormsAppHost.TakeSlow(out long ticks2, out string? _, out long _, out long _);
        Assert.Equal(0, ticks2);
    }
}
