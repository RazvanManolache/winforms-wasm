// Fork-owned copy of upstream DeviceContextExtensions.IDeviceContextAdapter
// (Compile-Removed in WfWasm.WinForms.csproj). Identical to upstream except
// it also implements IDeviceContextSource, so DeviceContextHdcScope can find
// the paint Graphics behind the adapter and register it with the GDI
// fill/text bridges (HDCs are zero on WASM, so bridges key off the scope).

using System.Drawing;
using Windows.Win32.Graphics.Gdi;

namespace System.Windows.Forms;

internal static partial class DeviceContextExtensions
{
    private class IDeviceContextAdapter : IHdcContext, IDeviceContextSource
    {
        private readonly IDeviceContext _deviceContext;
        public IDeviceContextAdapter(IDeviceContext deviceContext) => _deviceContext = deviceContext;
        public IDeviceContext DeviceContext => _deviceContext;
        public HDC GetHdc() => (HDC)_deviceContext.GetHdc();
        public void ReleaseHdc() => _deviceContext.ReleaseHdc();
    }
}
