using System.Net;
using System.Reflection;
using WfWasm.CanvasHost;

namespace WfWasm.CanvasHost.Tests;

public sealed class BrowserErrorReporterTests
{
    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        public string[] SnapshotBodies()
        {
            lock (_gate)
            {
                return Bodies.ToArray();
            }
        }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate)
            {
                Paths.Add(request.RequestUri?.AbsolutePath ?? "");
                Bodies.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Report_Posts_Signature_Batch_To_Client_Errors_Endpoint()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            var ex = new NotSupportedException("GDI handles are not available on web.");
            BrowserErrorReporter.Report("toolstrip-click", ex);

            for (int i = 0; i < 100 && handler.SnapshotBodies().Length == 0; i++)
                await Task.Delay(50);
            // Other tests may raise first-chance exceptions through the
            // shared static probe while this runs: assert ours arrived,
            // not that it arrived alone.
            Assert.Contains("/api/client-errors", handler.Paths);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("toolstrip-click"));
            Assert.Contains("NotSupportedException", ours);
            Assert.Contains("GDI handles are not available on web.", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_With_Explicit_Frame_Uses_It_In_Signature()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            BrowserErrorReporter.Report("first-chance", new NotSupportedException("late stack"), "Icon.FromHandle");
            for (int i = 0; i < 100 && handler.SnapshotBodies().Length == 0; i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), b => b.Contains("late stack"));
            Assert.Contains("NotSupportedException|Icon.FromHandle|late stack", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Suppresses_Transport_Failures_Without_Posting()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            // The report POST itself fails this way when the backend is
            // gone; posting it would amplify one crash into thousands.
            BrowserErrorReporter.Report("first-chance", new HttpRequestException("TypeError: Failed to fetch"));
            BrowserErrorReporter.Report("first-chance",
                new Microsoft.JSInterop.JSException("TypeError: Failed to fetch"));
            await Task.Delay(500);
            Assert.Empty(handler.SnapshotBodies());
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Rate_Limits_Repeated_Signatures()
    {
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            for (int i = 0; i < 8; i++)
                BrowserErrorReporter.Report("first-chance", new Exception("rate storm"), "F.M");
            for (int i = 0; i < 100 && handler.SnapshotBodies().Length < 5; i++)
                await Task.Delay(50);
            await Task.Delay(500);
            Assert.Equal(5, handler.SnapshotBodies().Count(b => b.Contains("rate storm")));
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public async Task Report_Includes_Inner_Exception_Chain_In_Payload()
    {
        // Catch sites (click handlers) catch reflection wrappers whose
        // outer message/stack say nothing (observed: a calendar day-click
        // filed only the TargetInvocationException wrapper row, no inner
        // row). The actionable inner must travel inside the payload while
        // the outer keeps its signature-row identity.
        var handler = new CaptureHandler();
        BrowserErrorReporter.Initialize(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5210") });
        try
        {
            Exception inner;
            try
            {
                throw new InvalidOperationException("inner-boom-chain-marker");
            }
            catch (Exception caught)
            {
                inner = caught;
            }

            BrowserErrorReporter.Report("control-click", new TargetInvocationException("wrapper", inner));
            // Parallel test classes share the static reporter, and the
            // shared first-chance probe reports this test's own marker
            // throw too: wait for OUR wrapper post (marker + wrapper
            // identity together occur only there), not merely the marker.
            static bool IsOurs(string b) =>
                b.Contains("inner-boom-chain-marker") && b.Contains("TargetInvocationException");
            for (int i = 0; i < 200 && !handler.SnapshotBodies().Any(IsOurs); i++)
                await Task.Delay(50);
            string ours = Assert.Single(handler.SnapshotBodies(), IsOurs);
            Assert.Contains("InvalidOperationException", ours);
        }
        finally
        {
            BrowserErrorReporter.Initialize(new HttpClient(new CaptureHandler()));
        }
    }

    [Fact]
    public void ComputeSignature_Starts_With_Exception_Type_Name()
    {
        string sig = BrowserErrorReporter.ComputeSignature(new InvalidOperationException("nope"));
        Assert.StartsWith("InvalidOperationException|", sig);
        Assert.EndsWith("|nope", sig);
    }

    [Fact]
    public void ComputeSignature_Includes_Top_Frame_From_Stack_Text()
    {
        Exception ex;
        try
        {
            ThrowForSignature();
            throw new InvalidOperationException("unreachable");
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        string sig = BrowserErrorReporter.ComputeSignature(ex);
        Assert.Contains(nameof(ThrowForSignature), sig);
    }

    private static void ThrowForSignature() => throw new InvalidOperationException("framed");

    [Fact]
    public void TopFrame_Parses_At_Lines_And_Ignores_Noise()
    {
        Assert.Equal("Icon.FromHandle",
            BrowserErrorReporter.TopFrame("   at System.Drawing.Icon.FromHandle(IntPtr handle)\n   at Other.Call()"));
        Assert.Equal("", BrowserErrorReporter.TopFrame(null));
        Assert.Equal("", BrowserErrorReporter.TopFrame("no frames here"));
    }

    [Fact]
    public void TopFrame_Strips_Assembly_Qualified_Generics()
    {
        // Observed live: the raw site segment was
        // "32325, Culture=neutral, PublicKeyToken=null]].<MaybeGetWicData>b__0".
        string frame = BrowserErrorReporter.TopFrame(
            "   at PaintDotNet.Clipboard.ClipboardTransactionExtensions.<>c__DisplayClass18_0`1[[PaintDotNet.Imaging.IIccBitmap, PaintDotNet.Windows.Core, Version=5.112.9563.32325, Culture=neutral, PublicKeyToken=null]].<MaybeGetWicData>b__0(Stream stream)");
        Assert.Equal("<>c__DisplayClass18_0`1.<MaybeGetWicData>b__0", frame);
        Assert.DoesNotContain("Culture=", frame);
    }
}
