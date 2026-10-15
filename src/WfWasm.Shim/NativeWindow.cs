namespace WfWasm.Shim;

/// <summary>
/// Window-handle owner without a real HWND (cf. System.Windows.Forms.NativeWindow).
/// The web canvas has no Win32 windows, so handles are synthetic tokens: they
/// are non-zero, unique per assignment, and usable for identity comparisons,
/// but carry no OS window behind them. <c>FromHandle</c> always returns null
/// because no live native window exists on this backend.
/// </summary>
public class NativeWindow : MarshalByRefObject, IWin32Window, IDisposable
{
    private static int _nextHandle = 1;
    private IntPtr _handle;

    /// <summary>Creates an unbound window owner (cf. NativeWindow()).</summary>
    public NativeWindow()
    {
    }

    /// <summary>Synthetic handle token, or zero when unbound (cf. Handle).</summary>
    public IntPtr Handle => _handle;

    /// <summary>Binds an existing handle token (cf. AssignHandle).</summary>
    /// <param name="handle">Handle to own.</param>
    public void AssignHandle(IntPtr handle)
    {
        ReleaseHandle(false);
        _handle = handle;
        OnHandleChange(EventArgs.Empty);
    }

    /// <summary>
    /// Assigns a fresh synthetic handle (cf. CreateHandle). No OS window is
    /// created; the parameters are accepted for source compatibility.
    /// </summary>
    /// <param name="cp">Creation parameters (recorded, not acted on).</param>
    public virtual void CreateHandle(CreateParams cp)
    {
        ReleaseHandle(false);
        _handle = new IntPtr(Interlocked.Increment(ref _nextHandle));
        OnHandleChange(EventArgs.Empty);
    }

    /// <summary>Releases the bound handle (cf. DestroyHandle).</summary>
    public virtual void DestroyHandle()
    {
        ReleaseHandle(true);
    }

    /// <summary>Default message sink (cf. DefWndProc). No-op: no OS messages arrive.</summary>
    /// <param name="m">Message (ignored).</param>
    public virtual void DefWndProc(ref Message m)
    {
    }

    /// <summary>
    /// Looks up the owner of a live native handle (cf. FromHandle). Always
    /// null here: no real HWNDs exist on the web backend.
    /// </summary>
    /// <param name="handle">Handle to look up (ignored).</param>
    /// <returns>Null.</returns>
    public static NativeWindow? FromHandle(IntPtr handle) => null;

    /// <summary>Releases the handle without destroying anything (cf. ReleaseHandle).</summary>
    public virtual void ReleaseHandle()
    {
        ReleaseHandle(true);
    }

    /// <summary>Message entry point (cf. WndProc). Routes to <c>DefWndProc</c>.</summary>
    /// <param name="m">Message.</param>
    protected virtual void WndProc(ref Message m)
    {
        DefWndProc(ref m);
    }

    /// <summary>Raised after the handle changes (cf. OnHandleChange).</summary>
    /// <param name="e">Event data.</param>
    protected virtual void OnHandleChange(EventArgs e)
    {
    }

    /// <summary>Reports thread exceptions (cf. OnThreadException). No-op here.</summary>
    /// <param name="e">Exception (ignored).</param>
    protected virtual void OnThreadException(Exception e)
    {
    }

    private void ReleaseHandle(bool raiseChange)
    {
        if (_handle == IntPtr.Zero)
            return;
        _handle = IntPtr.Zero;
        if (raiseChange)
            OnHandleChange(EventArgs.Empty);
    }

    /// <summary>Releases the handle (cf. Dispose).</summary>
    public void Dispose()
    {
        ReleaseHandle();
        GC.SuppressFinalize(this);
    }
}
