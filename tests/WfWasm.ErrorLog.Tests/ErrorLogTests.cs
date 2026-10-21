using WfWasm.ErrorLog;

namespace WfWasm.ErrorLog.Tests;

public sealed class ErrorLogTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wferrlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ErrorReport Report(string sig = "NotSupportedException|Icon.FromHandle|GDI handles are not available on web.")
        => new(sig, "GDI handles are not available on web.\nsecond line", "stack", "toolstrip-click", DateTimeOffset.UtcNow);

    [Fact]
    public void Log_Appends_Jsonl_And_Creates_Open_Index_Entry()
    {
        string dir = NewDir();
        var entry = ErrorLog.Log(dir, Report());

        Assert.Equal(1, entry.Count);
        Assert.Equal(ErrorLog.StatusOpen, entry.Status);
        Assert.Equal(0, entry.PostFixCount);
        Assert.True(File.Exists(Path.Combine(dir, ErrorLog.LogFileName)));
        Assert.True(File.Exists(Path.Combine(dir, ErrorLog.IndexFileName)));
        Assert.Single(ErrorLog.ReadIndex(dir));
    }

    [Fact]
    public void Log_Same_Signature_Aggregates_Count()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report());
        var second = ErrorLog.Log(dir, Report());

        Assert.Equal(2, second.Count);
        Assert.Single(ErrorLog.ReadIndex(dir));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(dir, ErrorLog.LogFileName)).Length);
    }

    [Fact]
    public void Log_After_MarkFixed_Bumps_PostFixCount_Not_Count_Reset()
    {
        string dir = NewDir();
        var first = ErrorLog.Log(dir, Report());
        Assert.True(ErrorLog.MarkFixed(dir, first.Signature, "ToIcon->null rule"));

        var after = ErrorLog.Log(dir, Report());

        Assert.Equal(ErrorLog.StatusFixed, after.Status);
        Assert.Equal(2, after.Count);
        Assert.Equal(1, after.PostFixCount);
        Assert.Equal("ToIcon->null rule", after.FixedNote);
    }

    [Fact]
    public void Log_Persists_Truncated_Flight_Recorder_Context()
    {
        string dir = NewDir();
        var entry = ErrorLog.Log(dir, Report() with { Context = new string('c', 20000) });

        Assert.Equal(1, entry.Count);
        string line = File.ReadAllLines(Path.Combine(dir, ErrorLog.LogFileName)).Single();
        Assert.Contains("context", line);
        Assert.True(line.Length < 20000);
    }

    [Fact]
    public void MarkFixed_Unknown_Signature_Returns_False()
    {
        Assert.False(ErrorLog.MarkFixed(NewDir(), "nope", "note"));
    }

    [Fact]
    public void Log_Merges_Apps_And_ReadIndex_Filters_By_App()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report() with { App = "calculator" });
        ErrorLog.Log(dir, Report() with { App = "calendar" });
        var entry = ErrorLog.Log(dir, Report());

        Assert.Equal(["calculator", "calendar"], entry.Apps ?? []);
        Assert.Single(ErrorLog.ReadIndex(dir, "calculator"));
        Assert.Single(ErrorLog.ReadIndex(dir, "CALCULATOR"));
        Assert.Empty(ErrorLog.ReadIndex(dir, "nope"));
        Assert.Single(ErrorLog.ReadIndex(dir));
        Assert.Single(ErrorLog.ReadIndex(dir, null));
    }

    [Fact]
    public void ReadIndex_Legacy_Rows_Without_Apps_Deserialize_With_Empty_Apps()
    {
        string dir = NewDir();
        File.WriteAllText(Path.Combine(dir, ErrorLog.IndexFileName),
            """[{"signature":"s","message":"m","source":"src","firstSeen":"2026-10-15T00:00:00Z","lastSeen":"2026-10-15T00:00:00Z","count":1,"status":"open","fixedNote":null,"postFixCount":0}]""");

        var entry = Assert.Single(ErrorLog.ReadIndex(dir));
        Assert.Empty(entry.Apps ?? []);
        Assert.Empty(ErrorLog.ReadIndex(dir, "calculator"));
    }

    [Fact]
    public void InventoryPage_Renders_Apps_Column_Escaped()
    {
        string dir = NewDir();
        ErrorLog.Log(dir, Report() with { App = "<b>calculator</b>" });

        string html = ErrorInventoryPage.Render(ErrorLog.ReadIndex(dir));
        Assert.Contains("Apps</th>", html);
        Assert.DoesNotContain("<b>calculator</b>", html);
        Assert.Contains("&lt;b&gt;calculator&lt;/b&gt;", html);
    }

    [Fact]
    public void ComputeSignature_Is_Stable_And_Truncates()
    {
        string a = ErrorLog.ComputeSignature("Ex", "T.M", "line");
        string b = ErrorLog.ComputeSignature("Ex", "T.M", "line");
        Assert.Equal(a, b);
        Assert.Equal("Ex|T.M|line", a);
        Assert.True(ErrorLog.ComputeSignature("Ex", "T.M", new string('x', 9999)).Length < 9999);
        Assert.Equal("unknown", ErrorLog.ComputeSignature("", null, ""));
    }

    [Fact]
    public void InventoryPage_Escapes_Html()
    {
        var entries = ErrorLog.ReadIndex(NewDir());
        string html = ErrorInventoryPage.Render(entries);
        Assert.Contains("0 known signatures", html);

        string dir = NewDir();
        ErrorLog.Log(dir, Report() with { Signature = "<script>alert(1)</script>" });
        string evil = ErrorInventoryPage.Render(ErrorLog.ReadIndex(dir));
        Assert.DoesNotContain("<script>alert(1)</script>", evil);
        Assert.Contains("&lt;script&gt;", evil);
    }
}
