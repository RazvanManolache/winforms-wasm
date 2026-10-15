using FormsTimer = System.Windows.Forms.Timer;

namespace WfWasm.WinForms.Tests;

// The browser has no message queue, so the native-window Timer never
// ticks headless (Telerik animations freeze at start values, clocks
// stall). The fork's Timer overlay raises Tick from a Threading.Timer.
// Regression: photo-album items stuck at animation start positions.
public sealed class TimerTests
{
    [Fact]
    public async Task Tick_Fires_Headless()
    {
        using var timer = new FormsTimer { Interval = 15 };
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        timer.Tick += (_, _) => tcs.TrySetResult(true);
        timer.Start();
        try
        {
            Task winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(tcs.Task, winner);
            Assert.True(await tcs.Task);
        }
        finally
        {
            timer.Stop();
        }
    }

    [Fact]
    public async Task Stop_Halts_Ticks()
    {
        using var timer = new FormsTimer { Interval = 15 };
        int count = 0;
        timer.Tick += (_, _) => Interlocked.Increment(ref count);
        timer.Start();
        try
        {
            await Task.Delay(150);
            Assert.True(Volatile.Read(ref count) > 0);
            timer.Stop();
            int frozen = Volatile.Read(ref count);
            await Task.Delay(150);
            Assert.Equal(frozen, Volatile.Read(ref count));
        }
        finally
        {
            timer.Stop();
        }
    }

    [Fact]
    public void Interval_Rejects_NonPositive()
    {
        using var timer = new FormsTimer();
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Interval = 0);
        timer.Interval = 50;
        Assert.Equal(50, timer.Interval);
    }

    [Fact]
    public void Tag_Roundtrips()
    {
        // Telerik's animation settings stash their element in Tag.
        using var timer = new FormsTimer();
        object tag = new();
        timer.Tag = tag;
        Assert.Same(tag, timer.Tag);
    }
}
