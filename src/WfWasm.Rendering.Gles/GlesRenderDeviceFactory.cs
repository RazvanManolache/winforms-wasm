namespace WfWasm.Rendering.Gles;

/// <summary>
/// Session-scoped factory for milestone-1 GLES devices. One factory instance serves
/// one isolated app runtime; each CreateAsync call mints the next device generation
/// for that session, so post-loss re-creation continues the generation sequence.
/// </summary>
public sealed class GlesRenderDeviceFactory : IRenderDeviceFactory
{
    // Honest milestone subset. Paths, complex clips, gradients and text are NOT
    // advertised and are reported as unsupported at enqueue time. This device
    // therefore never satisfies RenderPolicy.RequireGpu; that is intentional.
    public const DrawingFeatures MilestoneFeatures =
        DrawingFeatures.Images | DrawingFeatures.SourceCopy |
        DrawingFeatures.CpuPixelAccess | DrawingFeatures.RetainedComposition;

    public const string MilestoneScope =
        "GLES milestone 1: solid rect fills, rectangular clips, image blits, CPU leases, " +
        "retained composition. Paths, strokes, gradients, text and complex clips are reported as unsupported.";

    private readonly IGlesContextFactory _drivers;
    private long _generation;

    public Guid SessionId { get; }

    public GlesRenderDeviceFactory(IGlesContextFactory drivers) : this(drivers, Guid.NewGuid())
    {
    }

    public GlesRenderDeviceFactory(IGlesContextFactory drivers, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        if (sessionId == Guid.Empty) throw new ArgumentException("Session id must not be empty.", nameof(sessionId));
        _drivers = drivers;
        SessionId = sessionId;
    }

    public ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_drivers.IsSupported)
            return ValueTask.FromResult(new BackendProbe(BackendKind.CanvasKitWebGl2,
                BackendAvailability.Unsupported, DrawingFeatures.None,
                "GLES driver unavailable: " + _drivers.SupportDetail));
        return ValueTask.FromResult(new BackendProbe(BackendKind.CanvasKitWebGl2,
            BackendAvailability.Available, MilestoneFeatures,
            "Explicit GLES milestone scope. " + MilestoneScope + " Driver: " + _drivers.SupportDetail));
    }

    public ValueTask<IRenderDevice> CreateAsync(RenderDeviceOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PresentationTargetId);
        ArgumentNullException.ThrowIfNull(options.Presentation);
        ArgumentNullException.ThrowIfNull(options.Budget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Budget.MaximumResidentBytes);
        cancellationToken.ThrowIfCancellationRequested();
        GlesRenderDevice.RequireSurfaceLayout(options.Presentation, "GLES presentation target");
        if (!_drivers.IsSupported)
            return ValueTask.FromException<IRenderDevice>(
                new NotSupportedException("GLES driver unavailable: " + _drivers.SupportDetail));
        // Driver/creation failures propagate. No fallback, no retry, no second OnPaint.
        var context = _drivers.Create(options.PresentationTargetId, options.Presentation);
        try
        {
            var identity = new DeviceIdentity(SessionId, Interlocked.Increment(ref _generation));
            return ValueTask.FromResult<IRenderDevice>(new GlesRenderDevice(identity, options, context));
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
}
