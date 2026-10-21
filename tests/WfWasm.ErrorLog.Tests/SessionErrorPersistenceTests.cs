using System.Text.Json;
using WfWasm.ErrorLog;

namespace WfWasm.ErrorLog.Tests;

public sealed class SessionErrorPersistenceTests
{
    [Fact]
    public void Late_app_errors_keep_original_app_and_session_in_persisted_log()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wf-isolation-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            ErrorLog.Log(dir, new("probe", "failed", null, "app-load", DateTimeOffset.UtcNow, App:"paint", SessionId:"paint-1", AppLabel:"Paint.NET"));
            ErrorLog.Log(dir, new("probe", "failed", null, "app-load", DateTimeOffset.UtcNow, App:"smoke", SessionId:"smoke-2", AppLabel:"Smoke form"));
            string[] lines = File.ReadAllLines(Path.Combine(dir, ErrorLog.LogFileName));
            var first = JsonDocument.Parse(lines[0]).RootElement;
            Assert.Equal("paint", first.GetProperty("app").GetString());
            Assert.Equal("paint-1", first.GetProperty("sessionId").GetString());
            Assert.Equal("Paint.NET", first.GetProperty("appLabel").GetString());
            Assert.Equal(new[] {"paint", "smoke"}, Assert.Single(ErrorLog.ReadIndex(dir)).Apps);
            Assert.Single(ErrorLog.ReadIndex(dir, "paint"));
            Assert.Empty(ErrorLog.ReadIndex(dir, "calendar"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
