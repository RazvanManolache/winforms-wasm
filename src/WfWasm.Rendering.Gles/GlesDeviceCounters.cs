namespace WfWasm.Rendering.Gles;

/// <summary>Cumulative measured sinks/transfers. Every value counts real driver calls.</summary>
public sealed record GlesDeviceCounters(
    long Submissions,
    long ExecutedDraws,
    long DiscardedBatches,
    long UploadedBytes,
    long ReadbackBytes,
    long DeferredReleases,
    long ContextLosses,
    long ResidentBytes);
