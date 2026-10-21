using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WfWasm.WinForms.Tests;

// The headless SendMessage shim has no native tooltip behind a handle, so
// TTM_GETDELAYTIME / TTM_SETDELAYTIME are answered from managed state.
// Desktop returns the system defaults (initial 500, reshow 100, autopop
// 5000); the shim used to fall through to 0, which made ToolTip.GetDelayTime
// answer 0 and threw (timer interval 0ms) out of
// KeyboardToolTipStateMachine on every focus change.
public class ToolTipDelayShimTests
{
    private static HWND Hwnd(Control c) => (HWND)c.Handle;

    [Fact]
    public void GetDelayTimeDefaultsMatchDesktop()
    {
        using var c = new Control();
        Assert.Equal(500, (int)PInvokeCore.SendMessage(Hwnd(c), PInvoke.TTM_GETDELAYTIME, (WPARAM)PInvoke.TTDT_INITIAL));
        Assert.Equal(100, (int)PInvokeCore.SendMessage(Hwnd(c), PInvoke.TTM_GETDELAYTIME, (WPARAM)PInvoke.TTDT_RESHOW));
        Assert.Equal(5000, (int)PInvokeCore.SendMessage(Hwnd(c), PInvoke.TTM_GETDELAYTIME, (WPARAM)PInvoke.TTDT_AUTOPOP));
    }

    [Fact]
    public void SetDelayTimeRoundTrips()
    {
        using var c = new Control();
        HWND h = Hwnd(c);
        PInvokeCore.SendMessage(h, PInvoke.TTM_SETDELAYTIME, (WPARAM)PInvoke.TTDT_RESHOW, (LPARAM)250);
        Assert.Equal(250, (int)PInvokeCore.SendMessage(h, PInvoke.TTM_GETDELAYTIME, (WPARAM)PInvoke.TTDT_RESHOW));
        // Untouched types keep desktop defaults.
        Assert.Equal(500, (int)PInvokeCore.SendMessage(h, PInvoke.TTM_GETDELAYTIME, (WPARAM)PInvoke.TTDT_INITIAL));
    }

    [Fact]
    public void DeadHwndAnswersSafeDefaults()
    {
        var dead = (HWND)0x1234;
        Assert.Equal(500, (int)PInvokeCore.SendMessage(dead, PInvoke.TTM_GETDELAYTIME, (WPARAM)PInvoke.TTDT_INITIAL));
    }
}
