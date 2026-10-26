using WfWasm.ErrorLog;

namespace WfWasm.ErrorLog.Tests;

// Aggregation edge net for the checker-facing inventory: signature
// computation corners, per-app attribution caps, fixed-then-firing
// visibility, and malformed-index resilience. Each test owns a temp
// dir; the static gate serializes file access.
public sealed class ErrorLogAggregationTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "errlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ErrorReport Report(string sig, string app = "paint", string source = "first-chance") =>
        new(sig, "boom", "stack", source, DateTimeOffset.UtcNow, null, app, "sess-1", "Paint");

    [Theory]
    [InlineData("System.Exception", "M()", "line", "System.Exception|M()|line")]
    [InlineData("", "", "", "unknown")]
    [InlineData("   ", "  ", " ", "unknown")]
    [InlineData(null, null, null, "unknown")]
    public void ComputeSignature_Corners(string? type, string? frame, string? line, string expected)
    {
        Assert.Equal(expected, ErrorLog.ComputeSignature(type!, frame, line));
    }

    [Fact]
    public void ComputeSignature_Truncates_Each_Part()
    {
        string sig = ErrorLog.ComputeSignature(new string('T', 200), new string('F', 300), new string('L', 400));
        string[] parts = sig.Split('|');
        Assert.Equal(3, parts.Length);
        Assert.Equal(120, parts[0].Length);
        Assert.Equal(200, parts[1].Length);
        Assert.Equal(300, parts[2].Length);
    }

    [Fact]
    public void Log_Empty_Signature_Falls_Back_To_Message()
    {
        string dir = NewDir();
        var entry = ErrorLog.Log(dir, Report("   ", "paint").WithMessage("NullReferenceException: deref\nsecond line"));
        Assert.StartsWith("|", entry.Signature);
        Assert.Contains("NullReferenceException", entry.Signature);
        Assert.Equal("NullReferenceException: deref", entry.Message);
    }

    [Fact]
    public void Log_Caps_App_List_At_Eight_Sorted()
    {
        string dir = NewDir();
        for (int i = 9; i >= 0; i--)
            ErrorLog.Log(dir, Report("sig-cap", $"app{i:00}"));
        var entry = ErrorLog.ReadIndex(dir).Single(e => e.Signature == "sig-cap");
        Assert.Equal(10, entry.Count);
        Assert.Equal(8, entry.Apps!.Length);
        Assert.Equal(entry.Apps!.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), entry.Apps!);
    }

    [Fact]
    public void Log_Null_App_Not_Added_To_Apps()
    {
        string dir = NewDir();
        var entry = ErrorLog.Log(dir, Report("sig-null", null!));
        Assert.NotNull(entry.Apps);
        Assert.Empty(entry.Apps!);
    }

    [Fact]
    public void MarkFixed_Then_More_Reports_Bump_PostFix_Only()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report("sig-fix", "paint"));
        Assert.True(ErrorLog.MarkFixed(dir, "sig-fix", "repaired in rev90"));
        ErrorLog.Log(dir, Report("sig-fix", "paint"));
        ErrorLog.Log(dir, Report("sig-fix", "rte"));
        var entry = ErrorLog.ReadIndex(dir).Single(e => e.Signature == "sig-fix");
        Assert.Equal("fixed", entry.Status);
        Assert.Equal(3, entry.Count);
        Assert.Equal(2, entry.PostFixCount);
        Assert.Equal("repaired in rev90", entry.FixedNote);
        Assert.Contains("rte", entry.Apps!);
    }

    [Fact]
    public void MarkFixed_Truncates_Long_Note()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report("sig-note", "paint"));
        Assert.True(ErrorLog.MarkFixed(dir, "sig-note", new string('n', 900)));
        var entry = ErrorLog.ReadIndex(dir).Single(e => e.Signature == "sig-note");
        Assert.Equal(500, entry.FixedNote!.Length);
    }

    [Fact]
    public void ReadIndex_Filter_Is_Case_Insensitive()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report("sig-a", "Paint"));
        ErrorLog.Log(dir, Report("sig-b", "rte"));
        Assert.Single(ErrorLog.ReadIndex(dir, "PAINT"));
        Assert.Single(ErrorLog.ReadIndex(dir, " paint ".Trim()));
        Assert.Equal(2, ErrorLog.ReadIndex(dir).Count);
    }

    [Fact]
    public void Corrupt_Index_Returns_Empty_Not_Throw()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report("sig-ok", "paint"));
        File.WriteAllText(Path.Combine(dir, ErrorLog.IndexFileName), "{not json");
        Assert.Empty(ErrorLog.ReadIndex(dir));
        // Logging after corruption rebuilds the index from scratch.
        var entry = ErrorLog.Log(dir, Report("sig-new", "paint"));
        Assert.Equal(1, entry.Count);
    }

    [Fact]
    public void Log_Rejects_Bad_Dir_And_Null_Report()
    {
        Assert.Throws<ArgumentException>(() => ErrorLog.Log("   ", Report("x")));
        Assert.Throws<ArgumentNullException>(() => ErrorLog.Log(NewDir(), null!));
    }
}

file static class ErrorReportExtensions
{
    public static ErrorReport WithMessage(this ErrorReport report, string message) =>
        report with { Message = message };
}
