using System.Text.Json;
using WfWasm.AppIntegration;

namespace WfWasm.AppIntegration.Tests;

public sealed class PlatformSessionBridgeTests
{
    private static AppRuntimeIdentity Identity => new("paint", Guid.NewGuid());

    [Fact]
    public async Task Disconnected_services_do_not_return_empty_registry_or_fake_file_success()
    {
        await using var bridge = new PlatformSessionBridge(new DisconnectedPlatformTransport(Identity));
        Assert.False(bridge.IsConnected);
        await Assert.ThrowsAsync<NotSupportedException>(() => bridge.ReadRegistrySnapshotAsync().AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => bridge.WriteFileAsync("workspace:/a.txt", new byte[] { 1 }).AsTask());
    }

    [Fact]
    public async Task Dialog_cancellation_is_distinct_from_a_selected_file()
    {
        var transport = new Transport((_, _, _) => Task.FromResult<object?>(new FileDialogResponse(true, [], 1)));
        await using var bridge = new PlatformSessionBridge(transport);
        var result = await bridge.ShowFileDialogAsync(new("Choose a document"));
        Assert.True(result.Cancelled); Assert.Empty(result.Items);
        Assert.Equal("dialog.open", transport.Operation);
    }

    [Fact]
    public async Task Failed_write_remains_a_failure()
    {
        var transport = new Transport((_, _, _) => Task.FromException<object?>(new IOException("Permission denied witness")));
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<IOException>(() => bridge.WriteFileAsync("workspace:/a.txt", new byte[] { 5 }).AsTask());
    }

    [Fact]
    public async Task Deferred_write_owns_its_bytes_and_does_not_finish_before_acknowledgement()
    {
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport((_, _, _) => pending.Task);
        await using var bridge = new PlatformSessionBridge(transport);
        var original = new byte[] { 7, 8 };
        var write = bridge.WriteFileAsync("workspace:/a.txt", original).AsTask();
        original[0] = 99;
        Assert.False(write.IsCompleted);
        Assert.Equal(7, Assert.IsType<FileWriteRequest>(transport.Arguments).Bytes.Span[0]);
        pending.SetResult(null); await write;
    }

    [Fact]
    public async Task Late_success_after_disposal_is_rejected_even_when_transport_ignores_cancellation()
    {
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport((_, _, _) => pending.Task);
        var bridge = new PlatformSessionBridge(transport);
        var read = bridge.ReadFileAsync("workspace:/a.txt").AsTask();
        await bridge.DisposeAsync(); pending.SetResult(new byte[] { 1 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => bridge.ReadFileAsync("workspace:/a.txt").AsTask());
        Assert.Equal(1, transport.Disposals);
    }

    [Fact]
    public async Task Rebound_session_reply_is_rejected()
    {
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport((_, _, _) => pending.Task);
        await using var bridge = new PlatformSessionBridge(transport);
        var read = bridge.ReadFileAsync("workspace:/a.txt").AsTask();
        transport.Identity = Identity; pending.SetResult(new byte[] { 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
    }

    [Fact]
    public async Task Registry_call_preserves_kind_value_and_waits_for_durable_ack()
    {
        var pending = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport((_, _, _) => pending.Task);
        await using var bridge = new PlatformSessionBridge(transport);
        using var json = JsonDocument.Parse("\"9223372036854775807\"");
        var task = bridge.SetRegistryValueAsync(new("HKCU\\Software\\Example", "Count", "QWord", json.RootElement)).AsTask();
        Assert.Equal("registry.set", transport.Operation);
        Assert.Equal("9223372036854775807", Assert.IsType<RegistrySetRequest>(transport.Arguments).Data.GetString());
        json.Dispose(); Assert.False(task.IsCompleted);
        pending.SetResult(null); await task;
    }

    [Theory]
    [InlineData("C:\\Users\\Someone\\document.txt")]
    [InlineData("workspace:/../secret")]
    [InlineData("/etc/passwd")]
    public async Task Raw_host_paths_and_parent_walks_never_reach_transport(string path)
    {
        var transport = new Transport((_, _, _) => Task.FromResult<object?>(new byte[] { 1 }));
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() => bridge.ReadFileAsync(path).AsTask());
        Assert.Null(transport.Operation);
    }

    [Fact]
    public async Task Preview_is_not_a_physical_print_delivery_receipt()
    {
        var transport = new Transport((_, _, _) => Task.FromResult<object?>(new PrintPreviewResponse(true, false)));
        await using var bridge = new PlatformSessionBridge(transport);
        var result = await bridge.PreparePrintAsync(new("A page", [new(1, 1, 210, 297, new byte[] { 255, 0, 0, 255 })]));
        Assert.True(result.PreviewReady); Assert.False(result.PrinterDeliveryConfirmed);
        Assert.Equal("print.preview", transport.Operation);
    }

    [Fact]
    public async Task Fabricated_printer_delivery_and_invalid_pixels_are_rejected()
    {
        var transport = new Transport((_, _, _) => Task.FromResult<object?>(new PrintPreviewResponse(true, true)));
        await using var bridge = new PlatformSessionBridge(transport);
        await Assert.ThrowsAsync<ArgumentException>(() => bridge.PreparePrintAsync(new("Bad", [new(10, 10, 210, 297, new byte[4])])).AsTask());
        Assert.Null(transport.Operation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.PreparePrintAsync(new("Bad receipt", [new(1, 1, 210, 297, new byte[4])])).AsTask());
    }

    private sealed class Transport(Func<string, object?, CancellationToken, Task<object?>> handler) : IPlatformSessionTransport
    {
        public AppRuntimeIdentity Identity { get; set; } = PlatformSessionBridgeTests.Identity;
        public bool IsConnected => true;
        public string? Operation; public object? Arguments; public int Disposals;
        public async ValueTask<T> InvokeAsync<T>(string operation, object? arguments, CancellationToken cancellationToken)
        {
            Operation = operation; Arguments = arguments;
            return (T)(await handler(operation, arguments, cancellationToken))!;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
