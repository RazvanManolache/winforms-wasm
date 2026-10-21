// Headless-subclass overlay: replaces the runtimes/winforms
// (System/Windows/Forms/WindowSubclassHandler.cs, Compile-Removed in
// the csproj). Upstream marshals the WndProc delegate with
// GetFunctionPointerForDelegate, which aborts the browser runtime
// with a wasm out-of-bounds trap (same class as CommonDialog..ctor).
// Subclassing a stub HWND is meaningless (the host routes input
// directly), so Open/Dispose succeed without installing anything.
// Regression: func96 OOB <- WindowSubclassHandler..ctor <-
// TaskDialog.Show (latent; same trap class as the dialog ctor).
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace System.Windows.Forms;

/// <summary>
///  Allows to subclass individual windows.
/// </summary>
internal class WindowSubclassHandler : IDisposable
{
    private readonly HWND _handle;
    private bool _opened;
    private bool _disposed;
    private unsafe void* _originalWindowProc;

    private readonly WNDPROC _windowProcDelegate;

    private readonly unsafe void* _windowProcDelegatePtr;

    public unsafe WindowSubclassHandler(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            throw new ArgumentNullException(nameof(hwnd));
        }

        _handle = hwnd;

        // Keep the delegate alive; never marshal it (the thunk aborts
        // the browser runtime, and no native window exists to call it).
        _windowProcDelegate = NativeWndProc;
        _windowProcDelegatePtr = null;
        _ = _windowProcDelegate;
    }

    public unsafe void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_opened)
        {
            throw new InvalidOperationException();
        }

        // No native subclass to install headless; record the state so
        // Dispose stays balanced.
        _originalWindowProc = null;
        _opened = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void KeepCallbackDelegateAlive()
    {
        GC.KeepAlive(_windowProcDelegate);
    }

    protected virtual unsafe void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing && _opened)
        {
            KeepCallbackDelegateAlive();
        }

        _disposed = true;
    }

    protected virtual unsafe void WndProc(ref Message m)
    {
        // Call the original window procedure to process the message.
        if (_originalWindowProc is not null)
        {
            m.ResultInternal = PInvokeCore.CallWindowProc(
                _originalWindowProc,
                m.HWND,
                (uint)m.Msg,
                m.WParamInternal,
                m.LParamInternal);
        }
    }

    protected virtual bool CanCatchWndProcException(Exception exception)
    {
        // By default, don't catch exceptions.
        return false;
    }

    protected virtual void HandleWndProcException(Exception exception)
    {
        // Simply rethrow the exception here.
        ExceptionDispatchInfo.Throw(exception);
    }

    private LRESULT NativeWndProc(
        HWND hWnd,
        uint msg,
        WPARAM wParam,
        LPARAM lParam)
    {
        Debug.Assert(hWnd == _handle);

        Message m = Message.Create(hWnd, msg, wParam, lParam);
        try
        {
            WndProc(ref m);
        }
        catch (Exception ex) when (CanCatchWndProcException(ex))
        {
            HandleWndProcException(ex);
        }

        return m.ResultInternal;
    }
}
