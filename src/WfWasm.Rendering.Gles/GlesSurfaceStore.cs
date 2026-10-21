namespace WfWasm.Rendering.Gles;

internal sealed class GlesSurfaceStore : ISurfaceStore
{
    internal readonly record struct GlTarget(int Texture, int Fbo, SurfaceDescriptor Descriptor, long ContentVersion);

    private sealed class Record
    {
        public long Generation;
        public SurfaceDescriptor Descriptor = null!;
        public long ContentVersion;
        public int Texture;
        public int Fbo;
        public long LastUse;
    }

    private readonly GlesRenderDevice _device;
    private readonly Dictionary<long, Record> _live = new();
    private long _nextId;

    public event Action<SurfaceHandle>? Invalidated;

    internal GlesSurfaceStore(GlesRenderDevice device) => _device = device;

    internal long LiveBytes
    {
        get
        {
            long total = 0;
            foreach (var record in _live.Values) total += record.Descriptor.MinimumPixelBytes;
            return total;
        }
    }

    public long ResidentBytes => LiveBytes;

    public SurfaceSnapshot Create(SurfaceDescriptor descriptor)
    {
        _device.ThrowIfUsable();
        ArgumentNullException.ThrowIfNull(descriptor);
        GlesRenderDevice.RequireSurfaceLayout(descriptor, "GLES surface");
        _device.Context.MakeCurrent();
        int texture = _device.Context.CreateTexture(descriptor.PixelWidth, descriptor.PixelHeight);
        try
        {
            int fbo = _device.Context.CreateFramebuffer(texture);
            long id = ++_nextId;
            _live.Add(id, new Record
            {
                Generation = 1,
                Descriptor = descriptor,
                Texture = texture,
                Fbo = fbo,
                LastUse = _device.NextUse()
            });
            return new SurfaceSnapshot(new SurfaceHandle(_device.Identity, id, 1), descriptor, 0, PixelAuthority.Device);
        }
        catch
        {
            _device.Context.DeleteTexture(texture);
            throw;
        }
    }

    public SurfaceSnapshot Describe(SurfaceHandle handle)
    {
        _device.ThrowIfUsable();
        ResourceGuards.SameDevice(_device.Identity, handle.Device);
        if (handle.Id <= 0 || !_live.TryGetValue(handle.Id, out var record) || record.Generation != handle.Generation)
            throw new InvalidOperationException("Unknown or stale GLES surface handle. Recreate the surface and repaint.");
        return new SurfaceSnapshot(handle, record.Descriptor, record.ContentVersion, PixelAuthority.Device);
    }

    public bool IsValid(SurfaceHandle handle)
    {
        if (_device.State != RenderDeviceState.Ready) return false;
        if (handle.Device != _device.Identity || handle.Id <= 0) return false;
        return _live.TryGetValue(handle.Id, out var record) && record.Generation == handle.Generation;
    }

    public SurfaceSnapshot Resize(SurfaceHandle handle, SurfaceDescriptor descriptor)
    {
        _device.ThrowIfUsable();
        ArgumentNullException.ThrowIfNull(descriptor);
        ResourceGuards.SameDevice(_device.Identity, handle.Device);
        GlesRenderDevice.RequireSurfaceLayout(descriptor, "GLES surface");
        if (handle.Id <= 0 || !_live.TryGetValue(handle.Id, out var record) || record.Generation != handle.Generation)
            throw new InvalidOperationException("Unknown or stale GLES surface handle. Recreate the surface and repaint.");
        _device.Context.MakeCurrent();
        int texture = _device.Context.CreateTexture(descriptor.PixelWidth, descriptor.PixelHeight);
        int fbo;
        try
        {
            fbo = _device.Context.CreateFramebuffer(texture);
        }
        catch
        {
            _device.Context.DeleteTexture(texture);
            throw;
        }
        _device.Context.DeleteFramebuffer(record.Fbo);
        _device.Context.DeleteTexture(record.Texture);
        record.Texture = texture;
        record.Fbo = fbo;
        record.Descriptor = descriptor;
        record.Generation++;
        record.ContentVersion = 0; // Resize discards pixels; the host must repaint.
        record.LastUse = _device.NextUse();
        Invalidated?.Invoke(handle);
        return new SurfaceSnapshot(new SurfaceHandle(_device.Identity, handle.Id, record.Generation), descriptor, 0, PixelAuthority.Device);
    }

    public void Release(SurfaceHandle handle)
    {
        _device.ThrowIfUsable();
        ResourceGuards.SameDevice(_device.Identity, handle.Device);
        if (handle.Id <= 0 || !_live.TryGetValue(handle.Id, out var record) || record.Generation != handle.Generation)
            throw new InvalidOperationException("Unknown or stale GLES surface handle. Release each surface exactly once.");
        // Queued batches targeting this surface are discarded at submit without
        // touching the driver, so the GL delete is safe immediately.
        _device.Context.MakeCurrent();
        _device.Context.DeleteFramebuffer(record.Fbo);
        _device.Context.DeleteTexture(record.Texture);
        _live.Remove(handle.Id);
        Invalidated?.Invoke(handle);
    }

    public IReadOnlyList<SurfaceHandle> TrimToBudget(ResourceBudget budget)
    {
        _device.ThrowIfUsable();
        ArgumentNullException.ThrowIfNull(budget);
        var evicted = new List<SurfaceHandle>();
        while (TotalBytes() > budget.MaximumResidentBytes)
        {
            if (_device.ImageStore.TryEvictOldestUnpinned()) continue;
            if (!TryEvictOldestUnprotected(evicted)) break;
        }
        return evicted;
    }

    private long TotalBytes() => LiveBytes + _device.ImageStore.LiveBytes + _device.PresentationBytes;

    private bool TryEvictOldestUnprotected(List<SurfaceHandle> evicted)
    {
        long victim = 0;
        long oldest = long.MaxValue;
        foreach (var (id, record) in _live)
        {
            if (_device.IsQueueTarget(new SurfaceHandle(_device.Identity, id, record.Generation))) continue;
            if (record.LastUse < oldest) { oldest = record.LastUse; victim = id; }
        }
        if (victim == 0) return false;
        var victimRecord = _live[victim];
        _device.Context.MakeCurrent();
        _device.Context.DeleteFramebuffer(victimRecord.Fbo);
        _device.Context.DeleteTexture(victimRecord.Texture);
        _live.Remove(victim);
        var handle = new SurfaceHandle(_device.Identity, victim, victimRecord.Generation);
        evicted.Add(handle);
        Invalidated?.Invoke(handle);
        return true;
    }

    internal bool TryGetTarget(SurfaceHandle handle, out GlTarget target)
    {
        if (handle.Device == _device.Identity && _live.TryGetValue(handle.Id, out var record) &&
            record.Generation == handle.Generation)
        {
            target = new GlTarget(record.Texture, record.Fbo, record.Descriptor, record.ContentVersion);
            return true;
        }
        target = default;
        return false;
    }

    internal GlTarget GetTarget(SurfaceHandle handle)
    {
        if (!TryGetTarget(handle, out var target))
            throw new InvalidOperationException("Unknown or stale GLES surface handle. Recreate the surface and repaint.");
        return target;
    }

    internal void Touch(SurfaceHandle handle)
    {
        if (_live.TryGetValue(handle.Id, out var record) && record.Generation == handle.Generation)
            record.LastUse = _device.NextUse();
    }

    internal void NoteEnqueued(SurfaceHandle handle)
    {
        var record = _live[handle.Id];
        record.ContentVersion++;
        record.LastUse = _device.NextUse();
    }

    internal void NoteCpuWrite(SurfaceHandle handle)
    {
        var record = _live[handle.Id];
        record.ContentVersion++;
        record.LastUse = _device.NextUse();
    }

    internal void DropAllOnLoss()
    {
        foreach (var (id, record) in _live)
            Invalidated?.Invoke(new SurfaceHandle(_device.Identity, id, record.Generation));
        _live.Clear();
    }

    internal void DeleteAllOnDispose()
    {
        foreach (var record in _live.Values)
        {
            _device.Context.DeleteFramebuffer(record.Fbo);
            _device.Context.DeleteTexture(record.Texture);
        }
        _live.Clear();
    }
}
