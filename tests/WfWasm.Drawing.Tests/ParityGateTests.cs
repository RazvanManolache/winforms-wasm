// Parity gate: fails the suite until WfWasm.Drawing covers the full public
// surface of the framework's System.Drawing assemblies. This is the
// scan-and-fill loop made durable: any missing type or member breaks the
// build instead of going unnoticed.
using System.Reflection;

public sealed class ParityGateTests
{
    [Fact]
    public void Surface_Parity_With_Framework()
    {
        var (reference, implementation) = LoadSides();
        var (missingTypes, missingMembers, lines) = ApiScan.Diff(reference, implementation, 40);
        Assert.True(missingTypes + missingMembers == 0,
            $"missing {missingTypes} types, {missingMembers} members:\n{string.Join("\n", lines)}");
    }

    [Fact]
    public void Member_Shapes_Match_Framework()
    {
        var (reference, implementation) = LoadSides();
        Assert.Equal(0, ApiScan.ShapeCheck(reference, implementation));
    }

    private static (ApiScan.ApiSet Reference, ApiScan.ApiSet Implementation) LoadSides()
    {
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            root = OperatingSystem.IsWindows() ? @"C:\Program Files\dotnet" : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        string? prim = Newest(root, Path.Combine("shared", "Microsoft.NETCore.App"), "System.Drawing.Primitives.dll");
        string? common = Newest(root, Path.Combine("shared", "Microsoft.WindowsDesktop.App"), "System.Drawing.Common.dll");
        string? design = Newest(root, Path.Combine("shared", "Microsoft.WindowsDesktop.App"), "System.Drawing.Design.dll");
        if (prim is null || common is null || design is null)
            Assert.Fail($"Framework Drawing assemblies not found under {root}; gate cannot run here.");

        var reference = new ApiScan.ApiSet();
        reference.AddAssembly(prim!);
        reference.AddAssembly(common!);
        reference.AddAssembly(design!);
        var implementation = new ApiScan.ApiSet();
        implementation.AddAssembly(typeof(Point).Assembly.Location);
        return (reference, implementation);
    }

    private static string? Newest(string root, string relative, string file)
    {
        string dir = Path.Combine(root, relative);
        if (!Directory.Exists(dir))
            return null;
        return Directory.GetDirectories(dir)
            .OrderByDescending(d => d)
            .Select(d => Path.Combine(d, file))
            .FirstOrDefault(File.Exists);
    }
}
