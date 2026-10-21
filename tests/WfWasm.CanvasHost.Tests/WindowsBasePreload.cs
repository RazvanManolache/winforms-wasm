using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace WfWasm.CanvasHost.Tests;

// Testhost-only: the shared framework ships WindowsBase as a 16KB facade
// WITHOUT System.Windows.Freezable. Any test that (transitively) loads
// WindowsBase before PDN boots binds the facade first-loaded-wins; PDN's
// Tool-init chain then throws TypeLoadException inside the class-init lock
// and wedges it (the worker unwinds to park, PaintHostFixture waits
// forever: the PaintSceneTests hang, hangrun2/3/4). Pre-loading the full
// app-local copy at ASSEMBLY LOAD (before any test runs) guarantees the
// full copy wins. (EnsurePaintDotNetInitialized also preloads, but it runs
// at fixture time, after the facade already won -> FileLoadException.)
internal static class WindowsBasePreload
{
    [ModuleInitializer]
    internal static void PreloadFullWindowsBase()
    {
        try
        {
            string full = Path.Combine(AppContext.BaseDirectory, "WindowsBase.dll");
            if (File.Exists(full))
                AssemblyLoadContext.Default.LoadFromAssemblyPath(full);
        }
        catch
        {
            // Best-effort: the Freezable regression test fails loudly if
            // the facade wins anyway.
        }
    }
}
