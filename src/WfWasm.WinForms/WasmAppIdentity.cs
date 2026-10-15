// App identity handoff: the canvas host's on-demand loader knows which
// rewritten entry it fetched (BookShelf.exe); the fork cannot (assemblies
// load from bytes, so Location is empty and the module-file PInvoke
// answers null). Application.ExecutablePath builds on this so
// path-derived lookups (app data next to the exe) keep working; VFS
// lookups match by tail, so only the directory half matters.
// Regression: ArgumentNullException Path.Combine(null) <-
// BookShelfApp.LocalBackupFileName (GetDirectoryName of an empty
// ExecutablePath) on the data-load thread.
namespace System.Windows.Forms;

public static class WasmAppIdentity
{
    public static string? EntryFileName { get; set; }

    internal static string FromModulePath(string? modulePath)
    {
        if (!string.IsNullOrEmpty(modulePath))
            return modulePath;
        string dir = Application.StartupPath;
        if (string.IsNullOrEmpty(dir))
            dir = "/app";
        return System.IO.Path.Combine(dir, EntryFileName ?? "app.exe");
    }
}
