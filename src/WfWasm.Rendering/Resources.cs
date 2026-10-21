using System.Numerics;

namespace WfWasm.Rendering;

public enum PixelChannels { Rgba8, Bgra8 }
public enum PixelAlpha { Straight, Premultiplied }
public enum PixelColorSpace { Srgb, LinearSrgb }
public readonly record struct PixelLayout(PixelChannels Channels, PixelAlpha Alpha, PixelColorSpace ColorSpace);
public enum PixelAuthority { Cpu, Device, Synchronized, Lost }

/// <summary>Pixel dimensions, independent of the logical control's CSS/DIP bounds.</summary>
public sealed record SurfaceDescriptor
{
    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public float DpiScale { get; }
    public PixelLayout Layout { get; }
    public long MinimumPixelBytes { get; }

    public SurfaceDescriptor(int pixelWidth, int pixelHeight, float dpiScale, PixelLayout layout)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        if (!float.IsFinite(dpiScale) || dpiScale <= 0) throw new ArgumentOutOfRangeException(nameof(dpiScale));
        if (!Enum.IsDefined(layout.Channels) || !Enum.IsDefined(layout.Alpha) || !Enum.IsDefined(layout.ColorSpace))
            throw new ArgumentException("Unknown pixel layout.", nameof(layout));
        MinimumPixelBytes = checked((long)pixelWidth * pixelHeight * 4);
        PixelWidth = pixelWidth; PixelHeight = pixelHeight; DpiScale = dpiScale; Layout = layout;
    }
}

// Generation changes invalidate handles. ContentVersion changes do not replace
// identity: moving clean content should reuse the same surface and version.
public readonly record struct DeviceIdentity(Guid SessionId, long Generation);
public readonly record struct SurfaceHandle(DeviceIdentity Device, long Id, long Generation);
public readonly record struct ImageHandle(DeviceIdentity Device, long Id, long Version);
public readonly record struct FontHandle(DeviceIdentity Device, long Id);
public sealed record SurfaceSnapshot(SurfaceHandle Handle, SurfaceDescriptor Descriptor, long ContentVersion, PixelAuthority Authority);

public readonly record struct RenderRect(float X, float Y, float Width, float Height)
{
    public void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Width) || !float.IsFinite(Height) || Width < 0 || Height < 0)
            throw new ArgumentException("Bounds must be finite with nonnegative dimensions.");
    }
}
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
/// <summary>Straight sRGB, 0xRRGGBBAA. Backends convert explicitly to their surface encoding.</summary>
public readonly record struct RenderColor(uint Rgba);

/// <summary>Owned import snapshot; construct once per CPU source version, not per draw.</summary>
public sealed class CpuImageSnapshot
{
    private readonly byte[] _pixels;
    public string SourceId { get; }
    public long SourceVersion { get; }
    public SurfaceDescriptor Descriptor { get; }
    public int RowStride { get; }
    // CopyTo prevents callers from recovering/mutating the owned backing array.
    public int ByteLength => _pixels.Length;
    public void CopyTo(Span<byte> destination) => _pixels.AsSpan().CopyTo(destination);

    public CpuImageSnapshot(string sourceId, long sourceVersion, SurfaceDescriptor descriptor, int rowStride, ReadOnlySpan<byte> pixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceVersion);
        ArgumentNullException.ThrowIfNull(descriptor);
        long rowBytes = (long)descriptor.PixelWidth * 4;
        long required = (long)rowStride * descriptor.PixelHeight;
        if (rowStride < rowBytes || required > int.MaxValue || pixels.Length < required)
            throw new ArgumentException("CPU image buffer/stride does not cover the surface.");
        SourceId = sourceId; SourceVersion = sourceVersion; Descriptor = descriptor; RowStride = rowStride;
        _pixels = pixels[..(int)required].ToArray();
    }
}

public static class ResourceGuards
{
    public static void SameDevice(DeviceIdentity expected, DeviceIdentity actual)
    {
        if (expected.SessionId == Guid.Empty || expected.Generation <= 0 || expected != actual)
            throw new InvalidOperationException("Resource belongs to another session or a lost device generation.");
    }

    public static void Finite(Matrix3x2 matrix)
    {
        if (!float.IsFinite(matrix.M11) || !float.IsFinite(matrix.M12) || !float.IsFinite(matrix.M21) ||
            !float.IsFinite(matrix.M22) || !float.IsFinite(matrix.M31) || !float.IsFinite(matrix.M32))
            throw new ArgumentException("Transform must be finite.", nameof(matrix));
    }
}
