namespace WfWasm.WinForms.Tests;

// Browser-parity invariants: desktop assumptions the fork must hold in
// the canvas session. Each locks a runtimes/ patch or overlay so an
// upstream resync cannot silently revert it.
public sealed class BrowserParityTests
{
    [Fact]
    public void UserInteractive_Is_True()
    {
        // The canvas session is always interactive (mouse/keyboard drive
        // the app); the window-station probe answers null headless, which
        // wrongly disabled ShowDialog/MessageBox/CommonDialog.
        // Regression: InvalidOperationException UserInteractive <-
        // MessageBox.Show <- BookShelf data-load fallback (FailFast).
        Assert.True(System.Windows.Forms.SystemInformation.UserInteractive);
    }

    [Fact]
    public void ExecutablePath_Falls_Back_To_StartupPath()
    {
        // The module-file PInvoke answers null headless; the fallback
        // must stay non-empty with a usable directory half so
        // GetDirectoryName callers (app data next to the exe) survive.
        // Regression: ArgumentNullException Path.Combine(null) <-
        // BookShelfApp.LocalBackupFileName.
        string? saved = System.Windows.Forms.WasmAppIdentity.EntryFileName;
        try
        {
            System.Windows.Forms.WasmAppIdentity.EntryFileName = "BookShelf.exe";
            Assert.Equal(@"C:\app\x.exe", System.Windows.Forms.WasmAppIdentity.FromModulePath(@"C:\app\x.exe"));
            string synth = System.Windows.Forms.WasmAppIdentity.FromModulePath(null);
            Assert.EndsWith("BookShelf.exe", synth);
            Assert.False(string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(synth)));
            string anon = System.Windows.Forms.WasmAppIdentity.FromModulePath(string.Empty);
            System.Windows.Forms.WasmAppIdentity.EntryFileName = null;
            string bare = System.Windows.Forms.WasmAppIdentity.FromModulePath(null);
            Assert.EndsWith("app.exe", bare);
            Assert.False(string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(bare)));
            Assert.False(string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(anon)));
        }
        finally
        {
            System.Windows.Forms.WasmAppIdentity.EntryFileName = saved;
        }
    }
}
