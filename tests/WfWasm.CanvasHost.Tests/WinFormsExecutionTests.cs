using WfWasm.CanvasHost;

public sealed class WinFormsExecutionTests
{
    // A desktop context without a running message loop silently strands awaits.
    private sealed class UnpumpedDesktopContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) { }
    }

    [Fact]
    public void Desktop_Context_Cannot_Escape_Into_The_Browser_Load_Continuation()
    {
        var previous = SynchronizationContext.Current;
        var browser = new SynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(browser);
            WinFormsExecution.Run(() => SynchronizationContext.SetSynchronizationContext(new UnpumpedDesktopContext()));
            Assert.Same(browser, SynchronizationContext.Current);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void Failed_Form_Creation_Restores_Context_So_Error_And_Retry_Can_Render()
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            Assert.Throws<InvalidOperationException>(() => WinFormsExecution.Run(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new UnpumpedDesktopContext());
                throw new InvalidOperationException("Form construction failed.");
            }));
            Assert.Null(SynchronizationContext.Current);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
