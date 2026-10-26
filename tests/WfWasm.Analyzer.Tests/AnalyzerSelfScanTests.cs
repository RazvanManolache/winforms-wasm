using WfWasm.Analyzer;

// Baseline-free scanner net: the scanner reads its own test assembly,
// so these run everywhere without BASELINES.md checkouts.
public class AnalyzerSelfScanTests
{
    private static string SelfDll() =>
        System.Reflection.Assembly.GetExecutingAssembly().Location;

    [Fact]
    public void Scan_Self_Reports_Path_And_References()
    {
        ScanResult result = AssemblyScanner.Scan(SelfDll());
        Assert.Equal(SelfDll(), result.Path);
        Assert.NotEmpty(result.AssemblyReferences);
        Assert.All(result.AssemblyReferences, r => Assert.Matches(@"^\S+ \d+\.\d+", r));
        Assert.Contains(result.AssemblyReferences, r => r.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Self_TypeRefs_Sorted_Descending()
    {
        ScanResult result = AssemblyScanner.Scan(SelfDll());
        Assert.NotEmpty(result.TypeReferences);
        for (int i = 1; i < result.TypeReferences.Count; i++)
            Assert.True(result.TypeReferences[i - 1].Count >= result.TypeReferences[i].Count,
                $"type refs not sorted at {i}");
        Assert.Contains(result.TypeReferences, t => t.FullName.Contains("Xunit", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_Self_PInvokeModules_Sorted_Case_Insensitive()
    {
        ScanResult result = AssemblyScanner.Scan(SelfDll());
        var mods = result.PInvokeModules;
        Assert.Equal(mods.OrderBy(m => m, StringComparer.OrdinalIgnoreCase), mods);
    }

    [Fact]
    public void Scan_Missing_Throws_FileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            AssemblyScanner.Scan(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll")));
    }

    [Fact]
    public void Scan_NonManaged_Throws_InvalidData()
    {
        string junk = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
        File.WriteAllBytes(junk, [0x4D, 0x5A, 0x00, 0x00]);
        try
        {
            Assert.Throws<InvalidDataException>(() => AssemblyScanner.Scan(junk));
        }
        finally
        {
            File.Delete(junk);
        }
    }
}
