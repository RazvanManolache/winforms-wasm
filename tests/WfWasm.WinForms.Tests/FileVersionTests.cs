using Xunit;

public sealed class FileVersionTests
{
    [Fact]
    public void Empty_Path_Answers_Entry_Metadata_Not_Throw()
    {
        // Browser FileVersionInfo is a field-less throwing stub (every
        // member PNSEs), so state lives in the shim's record table and
        // readers (not the raw property) carry the values. Assembly.Location
        // is "" headless, so the shim falls back to the entry assembly.
        // Must never throw (the Cyotek OnLoad chain depends on it) and must
        // carry real version parts.
        var info = System.Windows.Forms.WasmFileVersion.GetVersionInfo("");

        Assert.NotNull(info);
        Assert.False(string.IsNullOrEmpty(System.Windows.Forms.WasmFileVersion.ReadFileVersion(info)));
    }

    [Fact]
    public void Missing_File_Falls_Back_Without_Throw()
    {
        var info = System.Windows.Forms.WasmFileVersion.GetVersionInfo("no-such-assembly-xyz.dll");

        Assert.NotNull(info);
    }

    [Fact]
    public void Unknown_File_Falls_Back_To_Entry_Without_Throw()
    {
        // No loaded assembly matches: the shim falls back to the entry
        // assembly (same rule as Resolve). Every redirected getter + ToString
        // must answer without throwing. Attribute-backed values are
        // cross-checked against the desktop FileVersionInfo as oracle.
        var info = System.Windows.Forms.WasmFileVersion.GetVersionInfo("no-such-assembly-xyz.dll");
        Assert.NotNull(info);
        // The requested name is echoed (desktop FileName parity).
        Assert.Equal("no-such-assembly-xyz.dll", System.Windows.Forms.WasmFileVersion.ReadFileName(info));

        var entry = System.Reflection.Assembly.GetEntryAssembly()!;
        var real = System.Diagnostics.FileVersionInfo.GetVersionInfo(entry.Location);
        Assert.Equal(real.ProductName ?? "", System.Windows.Forms.WasmFileVersion.ReadProductName(info));
        Assert.Equal(real.CompanyName ?? "", System.Windows.Forms.WasmFileVersion.ReadCompanyName(info));
        Assert.Equal(real.FileDescription ?? "", System.Windows.Forms.WasmFileVersion.ReadFileDescription(info));
        Assert.Equal(real.LegalCopyright ?? "", System.Windows.Forms.WasmFileVersion.ReadLegalCopyright(info));
        Assert.Equal(real.FileVersion ?? "", System.Windows.Forms.WasmFileVersion.ReadFileVersion(info));

        // Remaining string readers match the desktop oracle exactly.
        var wv = typeof(System.Windows.Forms.WasmFileVersion);
        var pairs = new (string Reader, string? Real)[]
        {
            ("ReadComments", real.Comments), ("ReadInternalName", real.InternalName),
            ("ReadLegalTrademarks", real.LegalTrademarks),
            ("ReadOriginalFilename", real.OriginalFilename),
            ("ReadPrivateBuild", real.PrivateBuild),
            ("ReadProductVersion", real.ProductVersion),
            ("ReadSpecialBuild", real.SpecialBuild), ("ReadLanguage", real.Language),
        };
        foreach (var (reader, expected) in pairs)
        {
            string actual = (string)wv.GetMethod(reader)!.Invoke(null, new object[] { info })!;
            Assert.True((expected ?? "") == actual, $"{reader}: expected=[{expected}] actual=[{actual}]");
        }
        foreach (string name in new[] { "ReadFileBuildPart", "ReadFileMajorPart",
            "ReadFileMinorPart", "ReadFilePrivatePart", "ReadProductBuildPart",
            "ReadProductMajorPart", "ReadProductMinorPart", "ReadProductPrivatePart" })
            Assert.IsType<int>(wv.GetMethod(name)!.Invoke(null, new object[] { info }));
        foreach (string name in new[] { "ReadIsDebug", "ReadIsPatched", "ReadIsPreRelease",
            "ReadIsPrivateBuild", "ReadIsSpecialBuild" })
            Assert.IsType<bool>(wv.GetMethod(name)!.Invoke(null, new object[] { info }));
        string desc = (string)wv.GetMethod("Describe")!.Invoke(null, new object[] { info })!;
        Assert.Contains("FileVersion:", desc);
    }

    [Fact]
    public void Two_Boxes_Keep_Independent_FileNames()
    {
        // The record table is keyed per returned instance: two boxes must
        // not share state.
        var a = System.Windows.Forms.WasmFileVersion.GetVersionInfo("alpha.dll");
        var b = System.Windows.Forms.WasmFileVersion.GetVersionInfo("beta.dll");

        Assert.Equal("alpha.dll", System.Windows.Forms.WasmFileVersion.ReadFileName(a));
        Assert.Equal("beta.dll", System.Windows.Forms.WasmFileVersion.ReadFileName(b));
    }

    [Fact]
    public void Null_Instance_Readers_Default_Without_Throw()
    {
        Assert.Equal("", System.Windows.Forms.WasmFileVersion.ReadProductName(null!));
        Assert.Equal(0, System.Windows.Forms.WasmFileVersion.ReadFileMajorPart(null!));
        Assert.Equal(false, System.Windows.Forms.WasmFileVersion.ReadIsDebug(null!));
        Assert.Equal("", System.Windows.Forms.WasmFileVersion.Describe(null!));
    }
}
