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

    // Browser BaseDirectory: assemblies load from bytes, so the runtime
    // answers null/empty and every Combine(null, ...) dies (ERP.Repository
    // .InitializeData caught it and loaded zero rows). The rewriter points
    // AppDomain + AppContext get_BaseDirectory here. Delegates to Drawing:
    // the small-app migration self-rewrites the fork, so a fork-resident
    // AppContext call would recurse into this shim (stack overflow).
    // Regression: ERP Purchases grid silently empty (line-14 path1 throw).
    public static string BaseDirectory() =>
        WfWasm.Drawing.ResourceHelper.AppBaseDirectory();

    // Instance-getter shape: the callvirt's AppDomain `this` lands here
    // (same stack shape, no Pop needed); the domain is irrelevant because
    // there is exactly one.
    public static string BaseDirectory(object? domain) => BaseDirectory();

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
