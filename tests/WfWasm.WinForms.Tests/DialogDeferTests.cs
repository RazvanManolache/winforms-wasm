using Xunit;

public sealed class DialogDeferTests
{
    [Fact]
    public void Same_Turn_Dispose_Of_Presented_Dialog_Survives()
    {
        // `using + ShowDialog` disposes synchronously in the presenting turn
        // (ShowDialog returns None immediately headless). A third-party
        // modal must survive until a real user close; PDN's own abort flows
        // keep the old behavior via the assembly gate.
        var f = new System.Windows.Forms.Form { Text = "ProbeDlg" };
        System.Windows.Forms.WasmDialogHost.NotePresented(f);
        f.Dispose();
        try
        {
            Assert.False(f.IsDisposed);
        }
        finally
        {
            System.Windows.Forms.WasmDialogHost.AdvanceTurn();
            f.Dispose();
        }
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
