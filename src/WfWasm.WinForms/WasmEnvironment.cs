namespace System.Windows.Forms;

// Browser-side Environment.GetFolderPath answers (the rewriter retargets
// app-assembly call sites here): stock WASM returns "" for every special
// folder, which kills Framework-era path builders (Telerik PDF
// FontsManager..cctor: GetParent(GetFolderPath(System)) throws on the
// empty path and poisons RTE boot with a ThreadExceptionDialog).
// Answers live under a virtual /vfs layout mirroring Windows: System
// answers /vfs/windows/system32, so the GetParent + "Fonts" discovery
// pattern lands on /vfs/windows/Fonts -- the same dir a direct Fonts
// query answers. Every answer is two levels deep, so GetParent never
// nulls. Dirs are created on first answer (browser only) so probing,
// combining, enumerating and writing all behave; headless passes
// through to the real API (desktop truth, nothing virtualized).
public static class WasmEnvironment
{
    public static string GetFolderPath(System.Environment.SpecialFolder folder) =>
        GetFolderPath(folder, System.Environment.SpecialFolderOption.None);

    public static string GetFolderPath(
        System.Environment.SpecialFolder folder,
        System.Environment.SpecialFolderOption option)
    {
        if (!OperatingSystem.IsBrowser())
            return System.Environment.GetFolderPath(folder, option);
        string path = GetVirtualPath(folder);
        try
        {
            System.IO.Directory.CreateDirectory(path);
        }
        catch
        {
        }

        return path;
    }

    public static string GetVirtualPath(System.Environment.SpecialFolder folder) => folder switch
    {
        System.Environment.SpecialFolder.Windows => "/vfs/windows",
        System.Environment.SpecialFolder.System => "/vfs/windows/system32",
        System.Environment.SpecialFolder.SystemX86 => "/vfs/windows/syswow64",
        System.Environment.SpecialFolder.Fonts => "/vfs/windows/Fonts",
        System.Environment.SpecialFolder.ProgramFiles => "/vfs/program-files",
        System.Environment.SpecialFolder.ProgramFilesX86 => "/vfs/program-files-x86",
        System.Environment.SpecialFolder.CommonApplicationData => "/vfs/program-data",
        System.Environment.SpecialFolder.ApplicationData => "/vfs/app-data",
        System.Environment.SpecialFolder.LocalApplicationData => "/vfs/local-app-data",
        System.Environment.SpecialFolder.MyDocuments => "/vfs/documents",
        System.Environment.SpecialFolder.Desktop => "/vfs/desktop",
        _ => "/vfs/" + folder.ToString().ToLowerInvariant(),
    };
}
