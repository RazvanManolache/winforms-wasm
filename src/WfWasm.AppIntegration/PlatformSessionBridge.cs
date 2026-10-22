using System.Text.Json;
using System.Text.RegularExpressions;

namespace WfWasm.AppIntegration;

/// <summary>Runtime identity only. Never a user ID, cookie, server token or authorization grant.</summary>
public sealed record AppRuntimeIdentity
{
    public string AppId { get; }
    public Guid SessionId { get; }
    public AppRuntimeIdentity(string appId, Guid sessionId)
    {
        if (!Regex.IsMatch(appId ?? "", "^[a-zA-Z0-9_-]{1,100}$") || sessionId == Guid.Empty)
            throw new ArgumentException("A valid app ID and fresh runtime session are required.");
        AppId = appId!; SessionId = sessionId;
    }
}

/// <summary>
/// Future .NET-to-PlatformRpcClient adapter. Must bind an exact parent origin,
/// source window and session; map byte memories to JS Uint8Array; reject stale
/// replies and implement bounded request IDs/cancellation. None is installed here.
/// Server identity and permissions remain owned by the shell/server.
/// </summary>
public interface IPlatformSessionTransport : IAsyncDisposable
{
    AppRuntimeIdentity Identity { get; }
    bool IsConnected { get; }
    ValueTask<T> InvokeAsync<T>(string operation, object? arguments, CancellationToken cancellationToken);
}

public sealed class DisconnectedPlatformTransport(AppRuntimeIdentity identity) : IPlatformSessionTransport
{
    public AppRuntimeIdentity Identity { get; } = identity;
    public bool IsConnected => false;
    public ValueTask<T> InvokeAsync<T>(string operation, object? arguments, CancellationToken cancellationToken) =>
        ValueTask.FromException<T>(new NotSupportedException("Platform RPC is not connected to this application runtime."));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record FileDialogRequest(string Title, string Mode = "open", bool Multiple = false,
    string Filter = "All files|*.*", int FilterIndex = 1, string FileName = "");
public sealed record SelectedFile(string Path, string Name, bool IsDirectory, string Source);
public sealed record FileDialogResponse(bool Cancelled, IReadOnlyList<SelectedFile> Items, int FilterIndex);
public sealed record FileReadRequest(string Path);
public sealed record FileWriteRequest(string Path, ReadOnlyMemory<byte> Bytes);
public sealed record RegistrySnapshotValue(string Key, string Name, string Kind, JsonElement Data);
public sealed record RegistrySnapshot(IReadOnlyList<string> Keys, IReadOnlyList<RegistrySnapshotValue> Values);
public sealed record RegistrySetRequest(string Key, string Name, string Kind, JsonElement Data);
public sealed record PrintPage(int Width, int Height, double WidthMm, double HeightMm, ReadOnlyMemory<byte> Rgba);
public sealed record PrintPreviewRequest(string Title, IReadOnlyList<PrintPage> Pages);
public sealed record PrintPreviewResponse(bool PreviewReady, bool PrinterDeliveryConfirmed);

/// <summary>
/// Typed async operation vocabulary matching the existing platform-rpc.mjs.
/// Not a RegistryKey/FileStream/ShowDialog implementation. Synchronous legacy
/// behavior still needs the boot mirror and a resumable execution strategy.
/// </summary>
public sealed class PlatformSessionBridge(IPlatformSessionTransport transport) : IAsyncDisposable
{
    private readonly IPlatformSessionTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly AppRuntimeIdentity _identity = transport?.Identity ?? throw new ArgumentNullException(nameof(transport));
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public AppRuntimeIdentity Identity => _identity;
    public bool IsConnected => !_disposed && _transport.IsConnected && _transport.Identity == _identity;

    public ValueTask<FileDialogResponse> ShowFileDialogAsync(FileDialogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode is not ("open" or "save" or "folder") || request.FilterIndex < 1)
            throw new ArgumentException("Invalid platform dialog options.");
        return InvokeAsync<FileDialogResponse>("dialog.open", request, cancellationToken);
    }

    public ValueTask<byte[]> ReadFileAsync(string virtualPath, CancellationToken cancellationToken = default)
    {
        ValidateVirtualPath(virtualPath);
        return InvokeAsync<byte[]>("files.read", new FileReadRequest(virtualPath), cancellationToken);
    }

    public async ValueTask WriteFileAsync(string virtualPath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        ValidateVirtualPath(virtualPath);
        if (bytes.Length > 16 * 1024 * 1024) throw new ArgumentException("File exceeds the existing platform RPC transfer limit.");
        // Own deferred data; transport must marshal it as Uint8Array, not a JSON base64 string.
        await InvokeAsync<object?>("files.write", new FileWriteRequest(virtualPath, bytes.ToArray()), cancellationToken);
    }

    public ValueTask<RegistrySnapshot> ReadRegistrySnapshotAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<RegistrySnapshot>("registry.snapshot", null, cancellationToken);

    public async ValueTask SetRegistryValueAsync(RegistrySetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Data.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Registry data is required.");
        await InvokeAsync<object?>("registry.set", request with { Data = request.Data.Clone() }, cancellationToken);
        // Existing shell operation waits for RegistryMirror.flush; transport must preserve that acknowledgement.
    }

    public async ValueTask FlushRegistryAsync(CancellationToken cancellationToken = default) =>
        await InvokeAsync<object?>("registry.flush", null, cancellationToken);

    public async ValueTask<PrintPreviewResponse> PreparePrintAsync(PrintPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pages is null || request.Pages.Count is < 1 or > 100)
            throw new ArgumentException("Expected 1–100 print pages.");
        long total = 0;
        var pages = request.Pages.Select(page =>
        {
            ArgumentNullException.ThrowIfNull(page);
            long pixels = (long)page.Width * page.Height;
            if (page.Width <= 0 || page.Height <= 0 || pixels > 16_000_000 ||
                page.Rgba.Length != pixels * 4 || !double.IsFinite(page.WidthMm) || !double.IsFinite(page.HeightMm) ||
                page.WidthMm is < 10 or > 1200 || page.HeightMm is < 10 or > 1200)
                throw new ArgumentException("Invalid print page dimensions or pixels.");
            total += pixels;
            if (total > 64_000_000) throw new ArgumentException("Print job exceeds the existing browser pixel budget.");
            return page with { Rgba = page.Rgba.ToArray() };
        }).ToArray();
        if (total > 64_000_000) throw new ArgumentException("Print job exceeds the existing browser pixel budget.");
        var result = await InvokeAsync<PrintPreviewResponse>("print.preview", request with { Pages = Array.AsReadOnly(pages) }, cancellationToken);
        if (result.PrinterDeliveryConfirmed)
            throw new InvalidOperationException("Browser preview/print dialogs cannot confirm physical printer delivery.");
        return result;
    }

    private async ValueTask<T> InvokeAsync<T>(string operation, object? arguments, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transport.Identity != _identity) throw new InvalidOperationException("Transport was rebound to another runtime session.");
        if (!_transport.IsConnected) throw new NotSupportedException("No platform adapter is connected; no success/default value was fabricated.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        linked.Token.ThrowIfCancellationRequested();
        var result = await _transport.InvokeAsync<T>(operation, arguments, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (_transport.Identity != _identity) throw new InvalidOperationException("Reply belongs to a replaced runtime session.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        return result;
    }

    private static void ValidateVirtualPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Regex.IsMatch(path, "^[a-zA-Z0-9_-]{1,100}:/") ||
            path.Contains('\\') || path.Split('/').Skip(1).Any(part => part is "." or ".."))
            throw new ArgumentException("Use a declared virtual mount path; the platform server still authorizes and validates it.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        try { await _transport.DisposeAsync(); }
        finally { _lifetime.Dispose(); }
        // Shell-owned bindPlatformHost disposal performs durable registry flush/revoke.
        // A failure there must remain observable; this class does not claim saved data.
    }
}
