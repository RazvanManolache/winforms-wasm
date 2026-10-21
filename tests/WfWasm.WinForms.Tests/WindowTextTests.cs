using System.Windows.Forms;

namespace WfWasm.WinForms.Tests;

// Window-text round-trip: dock/tool window captions (Projects/Bugs/
// Details) are set via Control.Text in InitializeComponent, often
// before handle creation. If the emulated create path or the
// post-creation SetWindowText path drops the text, captions read
// back empty (BugTracker native parity).
public sealed class WindowTextTests
{
    [Fact]
    public void Text_Survives_Handle_Creation()
    {
        using var control = new Control();
        control.Text = "Projects";
        Assert.Equal("Projects", control.Text);
        _ = control.Handle;
        Assert.True(control.IsHandleCreated);
        Assert.Equal("Projects", control.Text);
    }

    [Fact]
    public void Text_Set_After_Handle_Creation_Roundtrips()
    {
        using var control = new Control();
        _ = control.Handle;
        Assert.True(control.IsHandleCreated);
        control.Text = "Details";
        Assert.Equal("Details", control.Text);
    }

    [Fact]
    public void Text_Survives_Handle_Recreation()
    {
        // Native DestroyWindow sends WM_DESTROY synchronously and the fork
        // stashes Text into _text there; re-seeding reads _text. Without the
        // emulated message, recreation silently blanks the caption
        // (BugTracker dock windows are reparented after handle creation).
        using var control = new Control();
        control.Text = "Projects";
        _ = control.Handle;
        Assert.True(control.IsHandleCreated);
        typeof(Control).GetMethod("RecreateHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, null);
        Assert.True(control.IsHandleCreated);
        Assert.Equal("Projects", control.Text);
    }
}
