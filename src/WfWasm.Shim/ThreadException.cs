namespace System.Threading;

/// <summary>Thread-exception data (cf. System.Threading.ThreadExceptionEventArgs).</summary>
public class ThreadExceptionEventArgs : EventArgs
{
    /// <summary>Creates exception data (cf. ThreadExceptionEventArgs(Exception)).</summary>
    public ThreadExceptionEventArgs(Exception exception)
    {
        Exception = exception;
    }

    /// <summary>Unhandled exception (cf. Exception).</summary>
    public Exception Exception { get; }
}

/// <summary>Thread-exception handler (cf. System.Threading.ThreadExceptionEventHandler).</summary>
public delegate void ThreadExceptionEventHandler(object? sender, ThreadExceptionEventArgs e);
