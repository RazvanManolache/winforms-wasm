// Paths for optional integration fixtures remain local to this checkout.
internal static class TestPaths
{
    private static readonly string Root = FindRoot();
    private static string FindRoot([System.Runtime.CompilerServices.CallerFilePath] string file = "")
    {
        for (var directory = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(file)!);
             directory is not null; directory = directory.Parent)
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "WfWasm.sln")))
                return directory.FullName;
        throw new System.InvalidOperationException("Run tests from a source checkout.");
    }
    internal static string Repo(string relative) => System.IO.Path.Combine(
        Root, relative.Replace('\\', System.IO.Path.DirectorySeparatorChar));
}
