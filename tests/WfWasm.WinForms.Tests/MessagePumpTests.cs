using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using FormsForm = System.Windows.Forms.Form;
using FormsSystemInformation = System.Windows.Forms.SystemInformation;

namespace WfWasm.WinForms.Tests;

// The browser has no message queue: PeekMessage must report "no message".
// The upstream generic wrapper forwards to the PInvokeCore leaf; the
// CsWin32-generated PInvoke sibling is not compiled into the fork, so
// without the leaf the wrapper resolves back to itself and recurses to
// stack overflow on the first modal message pump (New dialog).
public sealed unsafe class MessagePumpTests
{
    [Fact]
    public void PeekMessage_Reports_No_Message()
    {
        BOOL result = PInvokeCore.PeekMessage((MSG*)null, default(HWND), 0, 0, 0);
        Assert.False(result);
    }

    [Fact]
    public void PeekMessage_Generic_Wrapper_Does_Not_Recurse()
    {
        BOOL result = PInvokeCore.PeekMessage((MSG*)null, new FakeWindow(default), 0, 0, 0);
        Assert.False(result);
    }

    private readonly struct FakeWindow(HWND handle) : IHandle<HWND>
    {
        public HWND Handle => handle;
    }

    [Fact]
    public void Modal_Dialogs_Present_Through_Host_Instead_Of_Hanging()
    {
        // No nested pump can receive browser events while blocked, so a
        // modal loop could never observe its dismiss click: present
        // through the canvas host (visible + tracked) and return
        // immediately instead of spinning the UI thread forever.
        // (Supersedes the old fail-fast throw; see WasmDialogHostTests.)
        // The headless branch keys off WasmDialogHost.IsHeadlessSession,
        // NOT UserInteractive: the session is interactive (mouse and
        // keyboard drive the app; MessageBox/CommonDialog require it)
        // but owns no Win32 queue. Keying off UserInteractive broke
        // MessageBox.Show for every app (InvalidOperationException).
        Assert.True(FormsSystemInformation.UserInteractive);
        using var form = new FormsForm();
        try
        {
            Assert.Equal(System.Windows.Forms.DialogResult.None, form.ShowDialog());
            Assert.True(form.Visible);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.Untrack(form);
        }
    }
}
