using System.Net;
using System.Text.Json;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

[CollectionDefinition("Session error attribution", DisableParallelization = true)]
public sealed class SessionErrorAttributionCollection { }

[Collection("Session error attribution")]
public sealed class SessionErrorAttributionTests
{
    private sealed class Sink : HttpMessageHandler
    {
        public readonly List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
    [Fact]
    public void Failed_fetch_is_reported_with_app_label_and_session_before_retry()
    {
        var sink = new Sink();
        BrowserErrorReporter.Initialize(new HttpClient(sink) { BaseAddress = new Uri("http://localhost") });
        BrowserErrorReporter.AppProvider = () => "paint";
        BrowserErrorReporter.AppLabelProvider = () => "Paint.NET";
        BrowserErrorReporter.SessionProvider = () => "session-paint";
        try
        {
            BrowserErrorReporter.Report("app-load", new HttpRequestException("manifest unavailable"));
            var report = JsonDocument.Parse(Assert.Single(sink.Bodies)).RootElement[0];
            Assert.Equal("paint", report.GetProperty("app").GetString());
            Assert.Equal("Paint.NET", report.GetProperty("appLabel").GetString());
            Assert.Equal("session-paint", report.GetProperty("sessionId").GetString());
            Assert.Equal("app-load", report.GetProperty("source").GetString());
            BrowserErrorReporter.Report("first-chance", new HttpRequestException("report POST failed"));
            Assert.Single(sink.Bodies);
        }
        finally { Reset(); }
    }
    [Fact]
    public void Repeated_error_in_one_app_does_not_suppress_the_next_session()
    {
        var sink = new Sink();
        BrowserErrorReporter.Initialize(new HttpClient(sink) { BaseAddress = new Uri("http://localhost") });
        string app = "paint", session = "old";
        BrowserErrorReporter.AppProvider = () => app;
        BrowserErrorReporter.SessionProvider = () => session;
        string message = "session limit " + Guid.NewGuid();
        try
        {
            for (int i=0; i<8; i++) BrowserErrorReporter.Report("app-load", new Exception(message));
            app = "smoke"; session = "new";
            BrowserErrorReporter.Report("app-load", new Exception(message));
            Assert.Equal(6, sink.Bodies.Count);
            var report = JsonDocument.Parse(sink.Bodies[^1]).RootElement[0];
            Assert.Equal("smoke",report.GetProperty("app").GetString());
            Assert.Equal("new",report.GetProperty("sessionId").GetString());
        }
        finally { Reset(); }
    }
    private static void Reset()
    {
        BrowserErrorReporter.AppProvider = null;
        BrowserErrorReporter.AppLabelProvider = null;
        BrowserErrorReporter.SessionProvider = null;
        BrowserErrorReporter.Initialize(new HttpClient(new Sink()));
    }
}
