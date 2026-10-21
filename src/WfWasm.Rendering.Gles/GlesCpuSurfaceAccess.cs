using System.Buffers;

namespace WfWasm.Rendering.Gles;

internal sealed class GlesCpuSurfaceAccess : ICpuSurfaceAccess
{
    private readonly GlesRenderDevice _device;

    internal GlesCpuSurfaceAccess(GlesRenderDevice device) => _device = device;

    public ICpuPixelLease Acquire(SurfaceHandle surface, PixelRect region, CpuPixelAccess access, CpuAccessReason reason)
    {
        _device.ThrowIfUsable();
        ResourceGuards.SameDevice(_device.Identity, surface.Device);
        if (!Enum.IsDefined(access)) throw new ArgumentOutOfRangeException(nameof(access));
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        var target = _device.Store.GetTarget(surface);
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.X > target.Descriptor.PixelWidth - region.Width ||
            region.Y > target.Descriptor.PixelHeight - region.Height)
            throw new ArgumentOutOfRangeException(nameof(region), "CPU lease region is outside the surface.");
        int stride = checked(region.Width * 4);
        int length = checked(stride * region.Height);
        if (access is CpuPixelAccess.Read or CpuPixelAccess.ReadWrite)
        {
            // Synchronous by design: drain queued drawing first so the read
            // observes program-order pixels (a read requires ordered
            // flush/readback when device content is authoritative), then flush
            // and read back eagerly so the lease never depends on later state.
            _device.Context.MakeCurrent();
            _device.ExecuteQueueDrained();
            _device.CollectReleases();
            _device.Context.Flush();
            var buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                _device.Context.ReadPixels(target.Fbo, region, buffer.AsSpan(0, length), stride);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
            _device.NoteReadback(length);
            return new GlesCpuPixelLease(_device, surface, region, target.Descriptor.Layout,
                stride, buffer, length, readable: true, writable: access == CpuPixelAccess.ReadWrite);
        }
        var staged = ArrayPool<byte>.Shared.Rent(length);
        Array.Clear(staged, 0, length);
        return new GlesCpuPixelLease(_device, surface, region, target.Descriptor.Layout,
            stride, staged, length, readable: false, writable: true);
    }
}

internal sealed class GlesCpuPixelLease : ICpuPixelLease
{
    private readonly GlesRenderDevice _device;
    private readonly SurfaceHandle _surface;
    private readonly int _length;
    private readonly bool _readable;
    private readonly bool _writable;
    private byte[]? _buffer;
    private bool _committed;
    private bool _disposed;

    public PixelRect Region { get; }
    public PixelLayout Layout { get; }
    public int RowStride { get; }

    public ReadOnlyMemory<byte> ReadablePixels
    {
        get
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GlesCpuPixelLease));
            if (!_readable) throw new InvalidOperationException("This CPU lease is write-only.");
            return new ReadOnlyMemory<byte>(_buffer!, 0, _length);
        }
    }

    public Memory<byte> WritablePixels
    {
        get
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GlesCpuPixelLease));
            if (!_writable) throw new InvalidOperationException("This CPU lease is read-only.");
            return new Memory<byte>(_buffer!, 0, _length);
        }
    }

    internal GlesCpuPixelLease(GlesRenderDevice device, SurfaceHandle surface, PixelRect region,
        PixelLayout layout, int rowStride, byte[] buffer, int length, bool readable, bool writable)
    {
        _device = device;
        _surface = surface;
        Region = region;
        Layout = layout;
        RowStride = rowStride;
        _buffer = buffer;
        _length = length;
        _readable = readable;
        _writable = writable;
    }

    public void Commit()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlesCpuPixelLease));
        if (!_writable) throw new InvalidOperationException("This CPU lease is read-only; acquire with Write or ReadWrite to commit pixels.");
        if (_committed) throw new InvalidOperationException("This CPU lease is already committed.");
        _device.ThrowIfUsable();
        var target = _device.Store.GetTarget(_surface);
        _device.Context.MakeCurrent();
        // Program order: drawing enqueued before this commit executes first,
        // so the committed pixels land on top of it.
        _device.ExecuteQueueDrained();
        _device.CollectReleases();
        _device.Context.UploadSubImage(target.Texture, Region, _buffer!.AsSpan(0, _length), RowStride);
        _device.NoteUploaded(_length);
        _device.Store.NoteCpuWrite(_surface);
        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_buffer is not null)
        {
            // Uncommitted staged writes are discarded by construction: nothing
            // reaches the driver without Commit.
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }
    }
}
