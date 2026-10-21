// Stub of the remaining referenced WinRT types: packaging/storage
// identity, capture/composition graphics, dispatcher queue.
// Package.Current throws like real unpackaged desktop so callers take
// their desktop fallback path; everything else is inert.

using System;

namespace Windows.ApplicationModel
{
    public sealed class Package
    {
        public static Package Current =>
            throw new InvalidOperationException("Package.Current is only available inside an AppX package (WinRtStub).");

        public PackageId Id => new PackageId();
        public Windows.Storage.StorageFolder InstalledLocation => new Windows.Storage.StorageFolder();
    }

    public sealed class PackageId
    {
        public string FamilyName => "";
        public string FullName => "";
        public string Name => "";
    }
}

namespace Windows.Storage
{
    public sealed class StorageFolder
    {
        public string Path => "";
    }
}

namespace Windows.Graphics
{
    public struct SizeInt32
    {
        public int Width;
        public int Height;

        public SizeInt32(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }
}

namespace Windows.Graphics.DirectX
{
    // DXGI_FORMAT values for the common entries.
    public enum DirectXPixelFormat
    {
        Unknown = 0,
        R32G32B32A32Float = 2,
        R16G16B16A16Float = 10,
        R8G8B8A8UIntNormalized = 28,
        R8G8B8A8UIntNormalizedSrgb = 29,
        B8G8R8A8UIntNormalized = 87,
        B8G8R8A8UIntNormalizedSrgb = 91,
    }
}

namespace Windows.Graphics.DirectX.Direct3D11
{
    public interface IDirect3DDevice
    {
    }

    public interface IDirect3DSurface
    {
    }
}

namespace Windows.Graphics.Capture
{
    public sealed class GraphicsCaptureItem
    {
    }

    public sealed class Direct3D11CaptureFrame
    {
    }

    public sealed class Direct3D11CaptureFramePool
    {
    }

    public sealed class GraphicsCaptureSession
    {
    }
}

namespace Windows.UI.Composition
{
    public class CompositionObject
    {
    }

    public sealed class Compositor : CompositionObject
    {
    }

    public class Visual : CompositionObject
    {
    }

    public sealed class ContainerVisual : Visual
    {
    }
}

namespace Windows.System
{
    public sealed class DispatcherQueue
    {
    }
}
