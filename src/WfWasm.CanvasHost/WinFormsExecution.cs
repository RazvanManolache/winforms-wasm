namespace WfWasm.CanvasHost;

/// <summary>
/// WinForms may install its desktop message-loop context when creating a control.
/// The browser has no desktop message loop; restore the caller's context before
/// returning to Blazor or awaiting JS interop.
/// </summary>
internal static class WinFormsExecution
{
    public static void Run(Action action)
    {
        var caller = SynchronizationContext.Current;
        try { action(); }
        finally { SynchronizationContext.SetSynchronizationContext(caller); }
    }
}
