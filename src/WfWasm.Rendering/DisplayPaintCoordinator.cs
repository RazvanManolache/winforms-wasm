namespace WfWasm.Rendering;

/// <summary>Proposed Graphics adapter sink. Records commands, never pixels.</summary>
public sealed class DrawingCommandRecorder
{
    private readonly List<DrawingCommand> _commands = [];
    private readonly SurfaceHandle _target;
    private readonly long _version;
    private bool _sealed;
    internal DrawingCommandRecorder(SurfaceSnapshot target) { _target = target.Handle; _version = target.ContentVersion; }
    public void Record(DrawingCommand command)
    {
        if (_sealed) throw new InvalidOperationException("Drawing callback has completed.");
        ArgumentNullException.ThrowIfNull(command);
        _commands.Add(command);
    }
    public void PrepareCpuAccess(CpuAccessReason reason)
    {
        if (_sealed) throw new InvalidOperationException("Drawing callback has completed.");
        // Deliberate gap: whole-callback batches cannot expose pixels to an
        // in-progress GetPixel/LockBits. Implement state-preserving segments
        // before wiring display Graphics CPU access; never return stale data.
        throw new NotSupportedException($"Mid-paint {reason} requires incremental submission; the skeleton does not implement that barrier.");
    }
    internal DrawingBatch Seal()
    {
        if (_sealed) throw new InvalidOperationException("Drawing callback has completed.");
        _sealed = true;
        return new(_target, _version, _commands);
    }
    internal void Abandon() { _sealed = true; _commands.Clear(); }
}

/// <summary>Wiring seam for host OnPaint. Does not replace existing host painting.</summary>
public sealed class DisplayPaintCoordinator(IRenderDevice device)
{
    public bool Paint(SurfaceHandle target, Action<DrawingCommandRecorder> invokePaintOnce)
    {
        ArgumentNullException.ThrowIfNull(invokePaintOnce);
        ResourceGuards.SameDevice(device.Identity, target.Device);
        if (device.State != RenderDeviceState.Ready || !device.Surfaces.IsValid(target))
            throw new InvalidOperationException("Display target is unavailable; recreate it before invoking app painting.");
        var recorder = new DrawingCommandRecorder(device.Surfaces.Describe(target));
        try
        {
            invokePaintOnce(recorder);
            var batch = recorder.Seal();
            if (batch.Commands.Count == 0) return false;
            device.Enqueue(batch);
            return true; // Queued only, never a claim of visible presentation.
        }
        catch { recorder.Abandon(); throw; }
    }
}
