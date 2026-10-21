namespace WfWasm.Rendering;

public sealed class RenderSession : IAsyncDisposable
{
    private bool _disposed;
    public IRenderDevice Device { get; }
    public RetainedScene Scene { get; }
    public string SelectionReason { get; }
    public event Action? RepaintRequired;
    internal RenderSession(IRenderDevice device, string reason)
    {
        Device = device; Scene = new(device.Identity); SelectionReason = reason;
        device.GenerationChanged += OnGenerationChanged;
        device.Surfaces.Invalidated += OnSurfaceInvalidated;
    }
    private void OnSurfaceInvalidated(SurfaceHandle handle)
    {
        if (!_disposed && Scene.InvalidateSurface(handle).Count > 0) RepaintRequired?.Invoke();
    }
    private void OnGenerationChanged(DeviceIdentity identity)
    {
        if (_disposed) return;
        if (identity != Scene.Device) Scene.ResetDevice(identity);
        RepaintRequired?.Invoke();
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        Device.GenerationChanged -= OnGenerationChanged;
        Device.Surfaces.Invalidated -= OnSurfaceInvalidated;
        Scene.Clear();
        // Device disposal owns completion/cancellation of queued work and
        // release of resources. Do not draw or re-enter application callbacks.
        await Device.DisposeAsync();
    }
}

public static class RenderSessionFactory
{
    /// <summary>Select a complete backend before invoking app painting.</summary>
    public static async ValueTask<RenderSession> CreateAsync(RenderPolicy policy, RenderDeviceOptions options,
        IRenderDeviceFactory? gpu, IRenderDeviceFactory? software, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PresentationTargetId);
        ArgumentNullException.ThrowIfNull(options.Presentation);
        ArgumentNullException.ThrowIfNull(options.Budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Budget.MaximumResidentBytes);
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        cancellationToken.ThrowIfCancellationRequested();
        if (policy != RenderPolicy.CpuCompatibility)
        {
            var probe = gpu is null ? null : await gpu.ProbeAsync(cancellationToken);
            if (probe is { Availability: BackendAvailability.Available, Kind: BackendKind.CanvasKitWebGl2 } &&
                (probe.Features & DrawingFeatures.Display) == DrawingFeatures.Display)
                return await CreateChecked(gpu!, probe, options, cancellationToken);
            if (policy == RenderPolicy.RequireGpu)
                throw new NotSupportedException("GPU display backend unavailable: " + (probe?.Reason ?? "no GPU factory registered"));
            return await Software("Explicit pre-paint fallback: " + (probe?.Reason ?? "no GPU factory registered"));
        }
        return await Software("Explicit CPU compatibility mode");

        async ValueTask<RenderSession> Software(string reason)
        {
            if (software is null) throw new NotSupportedException(reason + "; no software adapter registered");
            var probe = await software.ProbeAsync(cancellationToken);
            if (probe is not { Availability: BackendAvailability.Available, Kind: BackendKind.Software } ||
                (probe.Features & DrawingFeatures.Display) != DrawingFeatures.Display)
                throw new NotSupportedException("Software compatibility adapter unavailable: " + probe.Reason);
            return await CreateChecked(software, probe with { Reason = reason }, options, cancellationToken);
        }
    }

    private static async ValueTask<RenderSession> CreateChecked(IRenderDeviceFactory factory, BackendProbe probe,
        RenderDeviceOptions options, CancellationToken cancellationToken)
    {
        var device = await factory.CreateAsync(options, cancellationToken);
        try
        {
            if (device.Kind != probe.Kind || device.State != RenderDeviceState.Ready ||
                (device.Features & DrawingFeatures.Display) != DrawingFeatures.Display)
                throw new NotSupportedException("Created device does not satisfy the selected display backend.");
            ResourceGuards.SameDevice(device.Identity, device.Identity);
            return new(device, probe.Reason);
        }
        catch { await device.DisposeAsync(); throw; }
        // Startup exceptions propagate: do not hide initialization failures by
        // trying another backend, and never retry an application's OnPaint.
    }
}

/// <summary>Explicit placeholder. No library/context is loaded and no GPU work is claimed.</summary>
public sealed class CanvasKitWebGlFactorySkeleton : IRenderDeviceFactory
{
    public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new BackendProbe(BackendKind.CanvasKitWebGl2, BackendAvailability.NotImplemented,
            DrawingFeatures.None, "CanvasKit/WebGL2 display backend has not been implemented or connected"));
    }
    public ValueTask<IRenderDevice> CreateAsync(RenderDeviceOptions options, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IRenderDevice>(new NotSupportedException("GPU rendering skeleton has no device implementation."));
}
