using WfWasm.CanvasHost;

public sealed class ThreadExceptionLogTests
{
    [Fact]
    public void LogThreadExceptionArgs_Never_Throws()
    {
        // The dialog-attribution hook runs on the pump's failure path;
        // it must not throw for any input, including nulls and foreign
        // shapes (anonymous stand-in: the real args type is unnamed
        // deliberately, BCL/shim twin).
        WinFormsAppHost.LogThreadExceptionArgs(null);
        WinFormsAppHost.LogThreadExceptionArgs(new { Exception = (object)new InvalidOperationException("x") });
        WinFormsAppHost.LogThreadExceptionArgs(new { });
    }
}
