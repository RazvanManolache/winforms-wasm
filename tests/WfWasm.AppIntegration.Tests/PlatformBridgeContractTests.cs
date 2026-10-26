using System.Text.Json;
using WfWasm.AppIntegration;

namespace WfWasm.AppIntegration.Tests;

// Contract edges around the session bridge: identity shape, dialog and
// path validation, print budgets, and disposal semantics. A connected
// stub transport isolates bridge validation from transport behavior.
public sealed class PlatformBridgeContractTests
{
    private static AppRuntimeIdentity Id(string app = "paint") => new(app, Guid.NewGuid());

    private sealed class StubTransport(AppRuntimeIdentity identity) : IPlatformSessionTransport
    {
        public AppRuntimeIdentity Identity { get; } = identity;
        public bool IsConnected => true;
        public string? LastOperation;
        public ValueTask<T> InvokeAsync<T>(string operation, object? arguments, CancellationToken cancellationToken)
        {
            LastOperation = operation;
            return ValueTask.FromResult<T>(default!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("paint")]
    [InlineData("a")]
    [InlineData("syncfusion-word_2")]
    [InlineData("BENCHMARK-ADGV")]
    [InlineData("x-9_Z")]
    public void Identity_Accepts_Well_Formed(string app)
    {
        var id = Id(app);
        Assert.Equal(app, id.AppId);
        Assert.NotEqual(Guid.Empty, id.SessionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("dot.name")]
    [InlineData("slash/x")]
    public void Identity_Rejects_Bad_AppId(string app)
    {
        Assert.Throws<ArgumentException>(() => Id(app));
    }

    [Fact]
    public void Identity_Rejects_Empty_Session()
    {
        Assert.Throws<ArgumentException>(() => new AppRuntimeIdentity("paint", Guid.Empty));
    }

    [Fact]
    public void Disconnected_Transport_Never_Connects()
    {
        var t = new DisconnectedPlatformTransport(Id());
        Assert.False(t.IsConnected);
        Assert.Throws<NotSupportedException>(() =>
        {
            t.InvokeAsync<object>("dialog.open", null, default).AsTask().GetAwaiter().GetResult();
        });
    }

    [Fact]
    public async Task Bridge_Null_Transport_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            var b = new PlatformSessionBridge(null!);
            await b.DisposeAsync();
        });
    }

    [Theory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("folder")]
    public async Task Dialog_Accepts_Known_Modes(string mode)
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await bridge.ShowFileDialogAsync(new FileDialogRequest("Pick", mode));
        Assert.Equal("dialog.open", transport.LastOperation);
    }

    [Theory]
    [InlineData("OPEN")]
    [InlineData("print")]
    [InlineData("")]
    public async Task Dialog_Rejects_Unknown_Modes(string mode)
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.ShowFileDialogAsync(new FileDialogRequest("Pick", mode)).AsTask());
        Assert.Null(transport.LastOperation);
    }

    [Fact]
    public async Task Dialog_FilterIndex_Below_One_Rejects()
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.ShowFileDialogAsync(new FileDialogRequest("Pick", "open", FilterIndex: 0)).AsTask());
    }

    [Theory]
    [InlineData("workspace:/doc.txt")]
    [InlineData("mount-1:/a/b/c.bin")]
    [InlineData("A:/x")]
    public async Task ReadFile_Accepts_Mount_Paths(string path)
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await bridge.ReadFileAsync(path);
        Assert.Equal("files.read", transport.LastOperation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\win\\path")]
    [InlineData("mount:/a/../b")]
    [InlineData("mount:/a/./b")]
    [InlineData("no-mount-separator")]
    [InlineData("/abs/path")]
    public async Task ReadFile_Rejects_NonMount_And_Traversal(string path)
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() => bridge.ReadFileAsync(path).AsTask());
        Assert.Null(transport.LastOperation);
    }

    [Fact]
    public async Task WriteFile_Over_Limit_Rejects_Before_Transport()
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.WriteFileAsync("workspace:/big.bin", new byte[16 * 1024 * 1024 + 1]).AsTask());
        Assert.Null(transport.LastOperation);
    }

    [Fact]
    public async Task Print_Zero_Or_Too_Many_Pages_Reject()
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.PreparePrintAsync(new PrintPreviewRequest("t", [])).AsTask());
        var pages = Enumerable.Range(0, 101)
            .Select(_ => new PrintPage(10, 10, 100, 100, new byte[400])).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.PreparePrintAsync(new PrintPreviewRequest("t", pages)).AsTask());
        Assert.Null(transport.LastOperation);
    }

    [Fact]
    public async Task Print_Pixel_Buffer_Mismatch_Rejects()
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() => bridge.PreparePrintAsync(
            new PrintPreviewRequest("t", [new PrintPage(10, 10, 100, 100, new byte[399])])).AsTask());
    }

    [Fact]
    public async Task Registry_Undefined_Data_Rejects()
    {
        var transport = new StubTransport(Id());
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            bridge.SetRegistryValueAsync(new RegistrySetRequest("K", "N", "String", default)).AsTask());
    }

    [Fact]
    public async Task Dispose_Twice_Is_Safe_And_Calls_Are_Rejected_After()
    {
        var transport = new StubTransport(Id());
        var bridge = new PlatformSessionBridge(transport);
        Assert.True(bridge.IsConnected);
        await bridge.DisposeAsync();
        await bridge.DisposeAsync();
        Assert.False(bridge.IsConnected);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            bridge.ReadFileAsync("workspace:/x").AsTask());
    }
}
