// Test fakes for CpuPaint.TryUploadUiDeviceBitmap. The helper
// duck-types the production PaintDotNet.UI.Media.DeviceBitmap and
// DeviceImage by namespace + type name (this project carries no PDN
// references), so the fakes reuse those exact names. This file has no
// file-scoped namespace: the block below must nest under the GLOBAL
// namespace (a file-scoped parent would re-root the full names and
// shadow global PaintDotNet lookup for the whole file).
namespace PaintDotNet.UI.Media
{
    public sealed class DeviceBitmap
    {
        private readonly object? _src;
        public DeviceBitmap(object? src) { _src = src; }
        public object? BitmapSource => _src;
    }

    public sealed class DeviceImage
    {
        private readonly object? _src;
        public DeviceImage(object? src) { _src = src; }
        public object? BitmapSource => _src;
    }
}
