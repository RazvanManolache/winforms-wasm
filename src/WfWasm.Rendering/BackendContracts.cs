namespace WfWasm.Rendering;

public enum RenderPolicy { CpuCompatibility, PreferGpu, RequireGpu }
public enum BackendKind { Software, CanvasKitWebGl2 }
public enum BackendAvailability { Available, Unsupported, NotImplemented }
public enum RenderDeviceState { Ready, Lost, Disposed }
[Flags]
public enum DrawingFeatures
{
    None = 0, Paths = 1, ComplexClips = 2, Gradients = 4, Images = 8,
    Text = 16, SourceCopy = 32, CpuPixelAccess = 64, RetainedComposition = 128,
    Display = Paths | ComplexClips | Gradients | Images | Text | SourceCopy | CpuPixelAccess | RetainedComposition
}
public sealed record BackendProbe(BackendKind Kind, BackendAvailability Availability, DrawingFeatures Features, string Reason);
public sealed record ResourceBudget(long MaximumResidentBytes);
public sealed record RenderDeviceOptions(string PresentationTargetId, SurfaceDescriptor Presentation, ResourceBudget Budget);
/// <summary>Submission acknowledgement only; not proof that pixels are visible.</summary>
public readonly record struct SubmissionReceipt(long Sequence);

public interface IRenderDeviceFactory
{
    // Probe must not bind a 2D context to the eventual GPU presentation canvas.
    ValueTask<BackendProbe> ProbeAsync(CancellationToken cancellationToken = default);
    ValueTask<IRenderDevice> CreateAsync(RenderDeviceOptions options, CancellationToken cancellationToken = default);
}

public interface ISurfaceStore
{
    // Release, eviction and resize invalidate retained users immediately.
    event Action<SurfaceHandle>? Invalidated;
    SurfaceSnapshot Create(SurfaceDescriptor descriptor);
    SurfaceSnapshot Describe(SurfaceHandle handle);
    bool IsValid(SurfaceHandle handle);
    // Resize returns a new generation and invalidates the old handle.
    SurfaceSnapshot Resize(SurfaceHandle handle, SurfaceDescriptor descriptor);
    void Release(SurfaceHandle handle);
    long ResidentBytes { get; }
    // Return invalidated handles; the host must repaint before reusing them.
    IReadOnlyList<SurfaceHandle> TrimToBudget(ResourceBudget budget);
}

public interface IImageResources
{
    // Retain/cache by source identity + version; consume the owned snapshot
    // before returning. Image handles belong to this device generation.
    ImageHandle Import(CpuImageSnapshot image);
    void Release(ImageHandle image);
}

public enum CpuPixelAccess { Read, Write, ReadWrite }
public enum CpuAccessReason { GetPixel, LockBits, Export, Clone, CompatibilityFallback, ExternalPointer }
public interface ICpuPixelLease : IDisposable
{
    PixelRect Region { get; }
    PixelLayout Layout { get; }
    int RowStride { get; }
    ReadOnlyMemory<byte> ReadablePixels { get; }
    Memory<byte> WritablePixels { get; }
    // One explicit commit for writes: increment content version and queue the
    // changed region. Dispose without Commit discards staged writes.
    void Commit();
}
public interface ICpuSurfaceAccess
{
    // Synchronous by design: flush pending drawing before returning an app-
    // observable read. Access, bounds and lifetime are checked by the backend.
    ICpuPixelLease Acquire(SurfaceHandle surface, PixelRect region, CpuPixelAccess access, CpuAccessReason reason);
}

public sealed record TextRequest(string Text, FontHandle Font, float FontSize, float MaximumWidth, bool RightToLeft);
public sealed record ShapedText(GlyphRunSnapshot Glyphs, RenderRect InkBounds, float AdvanceWidth, float LineHeight);
public interface ITextService
{
    // Consume/copy font bytes before completing. Font handles are device-owned.
    ValueTask<FontHandle> RegisterFontAsync(string family, ReadOnlyMemory<byte> fontBytes, CancellationToken cancellationToken = default);
    void Release(FontHandle font);
    // Font registration/readiness precedes synchronous layout and painting.
    ShapedText Shape(TextRequest request);
}

public interface IRetainedCompositor
{
    ValueTask<SubmissionReceipt> PresentAsync(SceneCommit scene, CancellationToken cancellationToken = default);
}

/// <summary>One session/device owns drawing, text, images, surfaces and composition.</summary>
public interface IRenderDevice : IAsyncDisposable
{
    DeviceIdentity Identity { get; }
    BackendKind Kind { get; }
    DrawingFeatures Features { get; }
    RenderDeviceState State { get; }
    ISurfaceStore Surfaces { get; }
    IImageResources Images { get; }
    ICpuSurfaceAccess CpuAccess { get; }
    ITextService Text { get; }
    IRetainedCompositor Compositor { get; }
    event Action<DeviceIdentity>? GenerationChanged;
    // This synchronous call validates/resources/snapshots and queues the
    // ordered batch. It must not re-enter app OnPaint or silently rasterize.
    void Enqueue(DrawingBatch batch);
    ValueTask<SubmissionReceipt> SubmitAsync(CancellationToken cancellationToken = default);
    ValueTask DrainDisposalsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Count actual sinks/transfers, never infer counters from a backend label.</summary>
public sealed record RenderingCounters(long ControlPaints, long CpuRasterPixels, long UploadedBytes,
    long ReadbackBytes, long Fallbacks, long ResidentBytes);
