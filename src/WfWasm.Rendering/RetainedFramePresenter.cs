namespace WfWasm.Rendering;

/// <summary>Single-owner presentation seam; unchanged scene revisions do no backend work.</summary>
public sealed class RetainedFramePresenter(IRenderDevice device, RetainedScene scene)
{
    private long _submittedRevision = -1;
    private bool _presenting;
    public async ValueTask<SubmissionReceipt?> PresentIfChangedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_presenting) throw new InvalidOperationException("Serialize frame submission within a render session.");
        ResourceGuards.SameDevice(device.Identity, scene.Device);
        if (device.State != RenderDeviceState.Ready) throw new InvalidOperationException("Rendering device is unavailable.");
        if (_submittedRevision == scene.Revision) return null;
        var commit = scene.Capture();
        foreach (var layer in commit.Layers)
        {
            if (!device.Surfaces.IsValid(layer.Surface.Handle) ||
                device.Surfaces.Describe(layer.Surface.Handle).ContentVersion != layer.Surface.ContentVersion)
                throw new InvalidOperationException("Refresh/repaint stale retained surface content before composition.");
        }
        _presenting = true;
        try
        {
            // The compositor must drain pending drawing and compose it in one
            // ordered submission. A receipt is not an input-to-visible timing.
            var receipt = await device.Compositor.PresentAsync(commit, cancellationToken);
            _submittedRevision = commit.Revision;
            return receipt;
        }
        finally { _presenting = false; }
    }
}
