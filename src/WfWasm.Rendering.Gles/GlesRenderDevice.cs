namespace WfWasm.Rendering.Gles;

internal sealed record QueuedBatch(SurfaceHandle Target, IReadOnlyList<GlesOp> Ops, IReadOnlyList<ImageHandle> Pinned);

/// <summary>
/// GLES milestone-1 device. Translates drawing batches into explicit driver
/// primitives; it never rasterizes on the CPU. Single-threaded by design: the
/// host must serialize submissions, as <see cref="RetainedFramePresenter"/> requires.
/// </summary>
public sealed class GlesRenderDevice : IRenderDevice
{
    private readonly RenderDeviceOptions _options;
    private readonly IGlesContext _context;
    private readonly List<QueuedBatch> _queue = new();
    private readonly GlesSurfaceStore _surfaces;
    private readonly GlesImageResources _images;
    private readonly GlesCpuSurfaceAccess _cpuAccess;
    private readonly GlesTextService _text = new();
    private readonly GlesRetainedCompositor _compositor;
    private bool _disposed;
    private long _sequence;
    private long _useClock;
    private long _submissions;
    private long _executedDraws;
    private long _discardedBatches;
    private long _uploadedBytes;
    private long _readbackBytes;
    private long _deferredReleases;
    private long _contextLosses;

    internal GlesRenderDevice(DeviceIdentity identity, RenderDeviceOptions options, IGlesContext context)
    {
        Identity = identity;
        _options = options;
        _context = context;
        State = RenderDeviceState.Ready;
        _surfaces = new GlesSurfaceStore(this);
        _images = new GlesImageResources(this);
        _cpuAccess = new GlesCpuSurfaceAccess(this);
        _compositor = new GlesRetainedCompositor(this);
        _context.ContextLost += OnContextLost;
    }

    public DeviceIdentity Identity { get; }
    public BackendKind Kind => BackendKind.CanvasKitWebGl2;
    public DrawingFeatures Features => GlesRenderDeviceFactory.MilestoneFeatures;
    public RenderDeviceState State { get; private set; }
    public ISurfaceStore Surfaces => _surfaces;
    public IImageResources Images => _images;
    public ICpuSurfaceAccess CpuAccess => _cpuAccess;
    public ITextService Text => _text;
    public IRetainedCompositor Compositor => _compositor;
    // Probe/verification composition target (see GlesRetainedCompositor):
    // recompose the retained scene into an offscreen stage with no repaint.
    public ValueTask<SubmissionReceipt> PresentToSurfaceAsync(SceneCommit scene, SurfaceHandle stage, CancellationToken cancellationToken = default) =>
        _compositor.PresentToSurfaceAsync(scene, stage, cancellationToken);
    public event Action<DeviceIdentity>? GenerationChanged;

    public GlesDeviceCounters Counters => new(_submissions, _executedDraws, _discardedBatches,
        _uploadedBytes, _readbackBytes, _deferredReleases, _contextLosses,
        _surfaces.LiveBytes + _images.LiveBytes + PresentationBytes);

    internal IGlesContext Context => _context;
    internal GlesSurfaceStore Store => _surfaces;
    internal GlesImageResources ImageStore => _images;
    internal SurfaceDescriptor Presentation => _options.Presentation;
    internal long PresentationBytes => _options.Presentation.MinimumPixelBytes;

    internal static void RequireSurfaceLayout(SurfaceDescriptor descriptor, string what)
    {
        if (descriptor.Layout.Channels != PixelChannels.Rgba8)
            throw new NotSupportedException($"{what} requires Rgba8 in GLES milestone 1 (got {descriptor.Layout.Channels}).");
        if (descriptor.Layout.Alpha != PixelAlpha.Straight)
            throw new NotSupportedException($"{what} requires straight alpha in GLES milestone 1 (got {descriptor.Layout.Alpha}).");
        if (descriptor.Layout.ColorSpace != PixelColorSpace.Srgb)
            throw new NotSupportedException($"{what} requires sRGB in GLES milestone 1 (got {descriptor.Layout.ColorSpace}).");
    }

    internal void ThrowIfUsable()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlesRenderDevice));
        if (State == RenderDeviceState.Lost)
            throw new InvalidOperationException("The GLES device is lost. Recreate it through GlesRenderDeviceFactory (same session, next generation) and repaint.");
    }

    internal void NoteDraw() => _executedDraws++;
    internal void NoteUploaded(long bytes) => _uploadedBytes += bytes;
    internal void NoteReadback(long bytes) => _readbackBytes += bytes;
    internal void NoteDeferredRelease() => _deferredReleases++;
    internal long NextUse() => ++_useClock;

    internal bool IsQueueTarget(SurfaceHandle handle)
    {
        foreach (var queued in _queue)
            if (queued.Target == handle) return true;
        return false;
    }

    internal SubmissionReceipt NextReceipt()
    {
        _submissions++;
        return new SubmissionReceipt(unchecked(++_sequence));
    }

    internal void CollectReleases() => _images.CollectTombstones();

    public void Enqueue(DrawingBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ThrowIfUsable();
        ResourceGuards.SameDevice(Identity, batch.Target.Device);
        var target = _surfaces.GetTarget(batch.Target);
        if (target.ContentVersion != batch.ExpectedContentVersion)
            throw new InvalidOperationException($"Stale GLES display target: batch expects content v{batch.ExpectedContentVersion} but the surface is at v{target.ContentVersion}. Describe the surface and repaint.");
        var (ops, images) = GlesCommandTranslator.Translate(batch, target.Descriptor, _images);
        _queue.Add(new QueuedBatch(batch.Target, ops, images));
        foreach (var image in images) _images.Pin(image);
        _surfaces.NoteEnqueued(batch.Target);
    }

    public ValueTask<SubmissionReceipt> SubmitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUsable();
        _context.MakeCurrent();
        ExecuteQueueDrained();
        CollectReleases();
        _context.Flush();
        return ValueTask.FromResult(NextReceipt());
    }

    // Executes every queued batch in order. Batches whose target died after
    // enqueue are discarded (the host was already notified via Invalidated and
    // holds the repaint responsibility); they never touch the driver.
    internal void ExecuteQueueDrained()
    {
        foreach (var batch in _queue)
        {
            if (!_surfaces.TryGetTarget(batch.Target, out var target))
            {
                _discardedBatches++;
                _images.UnpinAll(batch.Pinned);
                continue;
            }
            _context.BindFramebuffer(target.Fbo);
            _context.SetViewport(target.Descriptor.PixelWidth, target.Descriptor.PixelHeight);
            _context.SetSourceOver(true);
            _context.SetScissor(null);
            foreach (var op in batch.Ops)
            {
                switch (op)
                {
                    case GlesSetBlend blend:
                        _context.SetSourceOver(blend.SourceOver);
                        break;
                    case GlesSetScissor scissor:
                        _context.SetScissor(scissor.Scissor);
                        break;
                    case GlesClear clear:
                        _context.Clear(clear.R, clear.G, clear.B, clear.A);
                        _executedDraws++;
                        break;
                    case GlesFillQuad fill:
                        _context.FillQuad(fill.Quad.P0, fill.Quad.P1, fill.Quad.P2, fill.Quad.P3, fill.Color);
                        _executedDraws++;
                        break;
                    case GlesBlit blit:
                        _context.DrawTextureQuad(blit.Quad.P0, blit.Quad.P1, blit.Quad.P2, blit.Quad.P3,
                            blit.Texture, blit.Source, blit.Sampling, blit.Opacity);
                        _executedDraws++;
                        break;
                }
            }
            _surfaces.Touch(batch.Target);
            _images.TouchAll(batch.Pinned);
            _images.UnpinAll(batch.Pinned);
        }
        _queue.Clear();
    }

    public ValueTask DrainDisposalsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return ValueTask.CompletedTask;
        if (State == RenderDeviceState.Lost) return ValueTask.CompletedTask;
        _context.MakeCurrent();
        CollectReleases();
        return ValueTask.CompletedTask;
    }

    private void OnContextLost()
    {
        if (State != RenderDeviceState.Ready) return;
        State = RenderDeviceState.Lost;
        _contextLosses++;
        foreach (var batch in _queue) _images.UnpinAll(batch.Pinned);
        _queue.Clear();
        // No driver deletes: a lost context already dropped its resources.
        _surfaces.DropAllOnLoss();
        _images.DropAllOnLoss();
        GenerationChanged?.Invoke(Identity with { Generation = Identity.Generation + 1 });
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _context.ContextLost -= OnContextLost;
        try
        {
            foreach (var batch in _queue) _images.UnpinAll(batch.Pinned);
            _queue.Clear();
            if (State == RenderDeviceState.Ready)
            {
                _context.MakeCurrent();
                _images.DeleteAllOnDispose();
                _surfaces.DeleteAllOnDispose();
            }
            // A lost device already dropped its tables in OnContextLost.
        }
        finally
        {
            State = RenderDeviceState.Disposed;
            _context.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
