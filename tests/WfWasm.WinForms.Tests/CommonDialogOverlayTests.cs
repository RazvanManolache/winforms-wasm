namespace WfWasm.WinForms.Tests;

// Headless-dialog overlay contract: CommonDialog-derived construction
// never marshals the hook delegate (GetFunctionPointerForDelegate
// aborts the browser runtime with a func96 OOB trap — no managed
// exception, no first-chance). Reset defaults still apply.
// Regression: func96 OOB <- new FolderBrowserDialog() <-
// FileExplorer.RadRibbonForm1.InitializeComponent (silent boot death).
public sealed class CommonDialogOverlayTests
{
    [Fact]
    public void FolderBrowserDialog_Constructs_With_Reset_Defaults()
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();

        Assert.Equal(System.Environment.SpecialFolder.Desktop, dlg.RootFolder);
        Assert.Equal("", dlg.Description);
        Assert.Equal("", dlg.SelectedPath);
        Assert.True(dlg.ShowNewFolderButton);
    }

    [Fact]
    public void FileDialogs_Construct_Without_Thunk()
    {
        using var open = new System.Windows.Forms.OpenFileDialog();
        using var save = new System.Windows.Forms.SaveFileDialog();
        using var font = new System.Windows.Forms.FontDialog();
        using var color = new System.Windows.Forms.ColorDialog();

        Assert.Equal("", open.FileName);
        Assert.Equal("", save.FileName);
    }
}
