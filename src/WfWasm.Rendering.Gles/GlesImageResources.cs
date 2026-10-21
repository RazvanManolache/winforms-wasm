using System.Buffers;

namespace WfWasm.Rendering.Gles;

internal sealed class GlesImageResources : IImageResources
{
    private sealed class Record
    {
        public string SourceId = "";
        public long SourceVersion;
        public SurfaceDescriptor Descriptor = null!;
        public int Texture;
        public long LastUse;
        public int Pins;
    }

    private readonly GlesRenderDevice _device;
    private readonly Dictionary<long, Record> _live = new();
    private readonly Dictionary<(string SourceId, long Version), long> _cache = new();
    private readonly Dictionary<long, Record> _tombstones = new();
    private long _nextId;

    internal GlesImageResources(GlesRenderDevice device) => _device = device;

    internal long LiveBytes
    {
        get
        {
            long total = 0;
            foreach (var record in _live.Values) total += record.Descriptor.MinimumPixelBytes;
            foreach (var record in _tombstones.Values) total += record.Descriptor.MinimumPixelBytes;
            return total;
        }
    }

    public ImageHandle Import(CpuImageSnapshot image)
    {
        _device.ThrowIfUsable();
        ArgumentNullException.ThrowIfNull(image);
        bool convertBgra = image.Descriptor.Layout.Channels switch
        {
            PixelChannels.Rgba8 => false,
            PixelChannels.Bgra8 => true,
            _ => throw new NotSupportedException($"GLES milestone 1 imports Rgba8/Bgra8 images only (got {image.Descriptor.Layout.Channels}).")
        };
        if (image.Descriptor.Layout.Alpha != PixelAlpha.Straight)
            throw new NotSupportedException($"GLES milestone 1 imports straight-alpha images only (got {image.Descriptor.Layout.Alpha}). Explicit premultiplied conversion is not implemented.");
        if (image.Descriptor.Layout.ColorSpace != PixelColorSpace.Srgb)
            throw new NotSupportedException($"GLES milestone 1 imports sRGB images only (got {image.Descriptor.Layout.ColorSpace}).");
        var key = (image.SourceId, image.SourceVersion);
        if (_cache.TryGetValue(key, out var cachedId) && _live.TryGetValue(cachedId, out var hit))
        {
            hit.LastUse = _device.NextUse();
            return new ImageHandle(_device.Identity, cachedId, image.SourceVersion);
        }
        int width = image.Descriptor.PixelWidth;
        int height = image.Descriptor.PixelHeight;
        _device.Context.MakeCurrent();
        int texture = _device.Context.CreateTexture(width, height);
        byte[]? scratch = null;
        byte[]? tight = null;
        try
        {
            scratch = ArrayPool<byte>.Shared.Rent(image.ByteLength);
            image.CopyTo(scratch.AsSpan(0, image.ByteLength));
            int rowBytes = checked(width * 4);
            int length = checked(rowBytes * height);
            tight = ArrayPool<byte>.Shared.Rent(length);
            for (int y = 0; y < height; y++)
            {
                var source = scratch.AsSpan(y * image.RowStride, rowBytes);
                var destination = tight.AsSpan(y * rowBytes, rowBytes);
                if (!convertBgra)
                {
                    source.CopyTo(destination);
                }
                else
                {
                    for (int x = 0; x < rowBytes; x += 4)
                    {
                        destination[x] = source[x + 2];
                        destination[x + 1] = source[x + 1];
                        destination[x + 2] = source[x];
                        destination[x + 3] = source[x + 3];
                    }
                }
            }
            _device.Context.UploadSubImage(texture, new PixelRect(0, 0, width, height), tight.AsSpan(0, length), rowBytes);
            _device.NoteUploaded(length);
        }
        catch
        {
            _device.Context.DeleteTexture(texture);
            throw;
        }
        finally
        {
            if (scratch is not null) ArrayPool<byte>.Shared.Return(scratch);
            if (tight is not null) ArrayPool<byte>.Shared.Return(tight);
        }
        long id = ++_nextId;
        _live.Add(id, new Record
        {
            SourceId = image.SourceId,
            SourceVersion = image.SourceVersion,
            Descriptor = image.Descriptor,
            Texture = texture,
            LastUse = _device.NextUse()
        });
        _cache[key] = id;
        return new ImageHandle(_device.Identity, id, image.SourceVersion);
    }

    public void Release(ImageHandle image)
    {
        _device.ThrowIfUsable();
        ResourceGuards.SameDevice(_device.Identity, image.Device);
        if (image.Id <= 0 || !_live.TryGetValue(image.Id, out var record) || record.SourceVersion != image.Version)
            throw new InvalidOperationException("Unknown, released, or evicted GLES image handle. Re-import the CPU source version to draw it again.");
        _live.Remove(image.Id);
        _cache.Remove((record.SourceId, record.SourceVersion));
        if (record.Pins == 0)
        {
            _device.Context.MakeCurrent();
            _device.Context.DeleteTexture(record.Texture);
        }
        else
        {
            // Pinned by a queued draw: the handle dies now, the texture dies
            // once the queue drains. Submit/Present/Drain perform the delete.
            _tombstones.Add(image.Id, record);
            _device.NoteDeferredRelease();
        }
    }

    internal int ResolveTexture(ImageHandle image)
    {
        if (image.Device != _device.Identity || !_live.TryGetValue(image.Id, out var record) ||
            record.SourceVersion != image.Version)
            throw new InvalidOperationException("GLES image handle is unknown, released, or evicted. Re-import the CPU source version before drawing.");
        return record.Texture;
    }

    internal SurfaceDescriptor ResolveDescriptor(ImageHandle image)
    {
        if (image.Device != _device.Identity || !_live.TryGetValue(image.Id, out var record) ||
            record.SourceVersion != image.Version)
            throw new InvalidOperationException("GLES image handle is unknown, released, or evicted. Re-import the CPU source version before drawing.");
        return record.Descriptor;
    }

    internal void Pin(ImageHandle image)
    {
        if (!_live.TryGetValue(image.Id, out var record) || record.SourceVersion != image.Version)
            throw new InvalidOperationException("GLES image handle died between validation and pinning.");
        record.Pins++;
    }

    internal void UnpinAll(IReadOnlyList<ImageHandle> handles)
    {
        foreach (var handle in handles)
        {
            if (_live.TryGetValue(handle.Id, out var record)) record.Pins--;
            else if (_tombstones.TryGetValue(handle.Id, out var tombstoned)) tombstoned.Pins--;
        }
    }

    internal void TouchAll(IReadOnlyList<ImageHandle> handles)
    {
        foreach (var handle in handles)
            if (_live.TryGetValue(handle.Id, out var record)) record.LastUse = _device.NextUse();
    }

    internal void CollectTombstones()
    {
        foreach (var id in _tombstones.Keys.ToArray())
        {
            var record = _tombstones[id];
            if (record.Pins == 0)
            {
                _device.Context.DeleteTexture(record.Texture);
                _tombstones.Remove(id);
            }
        }
    }

    internal bool TryEvictOldestUnpinned()
    {
        long victim = 0;
        long oldest = long.MaxValue;
        foreach (var (id, record) in _live)
            if (record.Pins == 0 && record.LastUse < oldest) { oldest = record.LastUse; victim = id; }
        if (victim == 0) return false;
        var evicted = _live[victim];
        _device.Context.MakeCurrent();
        _device.Context.DeleteTexture(evicted.Texture);
        _live.Remove(victim);
        _cache.Remove((evicted.SourceId, evicted.SourceVersion));
        return true;
    }

    internal void DropAllOnLoss()
    {
        _live.Clear();
        _tombstones.Clear();
        _cache.Clear();
    }

    internal void DeleteAllOnDispose()
    {
        foreach (var record in _live.Values) _device.Context.DeleteTexture(record.Texture);
        foreach (var record in _tombstones.Values) _device.Context.DeleteTexture(record.Texture);
        _live.Clear();
        _tombstones.Clear();
        _cache.Clear();
    }
}
