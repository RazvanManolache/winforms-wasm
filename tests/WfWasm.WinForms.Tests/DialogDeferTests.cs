using Xunit;

public sealed class DialogDeferTests
{
    [Fact]
    public void Same_Turn_Dispose_Of_NonEffect_Dialog_Disposes()
    {
        // Contract decision (rev204, evidence in P1 triage): deferral is
        // for effect config dialogs only (the user drives them after the
        // headless abort; WasmDialogHostTests pins that path). Desktop
        // Dispose() always tears down synchronously, and PDN's own
        // non-effect dialogs have always disposed for real without harm,
        // so a plain presented modal disposes in the presenting turn too.
        var f = new System.Windows.Forms.Form { Text = "ProbeDlg" };
        System.Windows.Forms.WasmDialogHost.NotePresented(f);
        f.Dispose();
        Assert.True(f.IsDisposed);
    }

    [Fact]
    public void Later_Turn_Dispose_Really_Disposes()
    {
        var f = new System.Windows.Forms.Form { Text = "ProbeDlg2" };
        System.Windows.Forms.WasmDialogHost.NotePresented(f);
        System.Windows.Forms.WasmDialogHost.AdvanceTurn();
        f.Dispose();
        Assert.True(f.IsDisposed);
    }

    [Fact]
    public void Unmarked_Dialog_Disposes_Normally()
    {
        var f = new System.Windows.Forms.Form { Text = "ProbeDlg3" };
        f.Dispose();
        Assert.True(f.IsDisposed);
    }
}
