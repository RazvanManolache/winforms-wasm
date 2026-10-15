// Fork-owned: GDI and COM interop extensions for Windows Forms on WebAssembly.

using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Windows.Win32.Graphics.Gdi
{
    internal static class HdcExtensions
    {
        public static SelectPaletteScope HalftonePalette(this GetDcScope hdc, bool forceBackground, bool realizePalette) => default;
        public static SelectPaletteScope HalftonePalette<T>(this T hdc, bool forceBackground, bool realizePalette) where T : IHandle<HDC> => default;
    }
}

namespace Windows.Win32.Graphics.GdiPlus
{
    internal static unsafe class GpRegionExtensions
    {
        public static unsafe void* GetPointer(this Region region) => (void*)1;
        public static RegionScope GetRegionScope(this Region region, global::System.Drawing.Graphics graphics) => new(HRGN.Null);
        public static RegionScope GetRegionScope(this Region region, HWND hwnd) => new(HRGN.Null);
    }
}

namespace Windows.Win32.System.Ole
{
    internal static class PictureExtensions
    {
        public static object? CreateIPictureRCW(this Image image, bool copy = false) => null;
        public static object? CreateIPictureRCW(this Icon icon, bool copy = false) => null;
        public static object? CreateIPictureDispRCW(this Image image) => null;
    }
}

namespace Windows.Win32.System.Ole
{
    internal static class PictDescExtensions
    {
        public static PICTDESC CreatePICTDESC(this global::System.Drawing.Icon icon, bool copy = false) => default;
        public static PICTDESC CreatePICTDESC(this global::System.Drawing.Bitmap bitmap) => default;
    }
}

namespace Windows.Win32.UI.Accessibility
{
    internal static unsafe class IAccessibleInterfaceExtensions
    {
        public static global::Windows.Win32.Foundation.HRESULT accLocation(
            this IAccessible.Interface accessible,
            out int pxLeft,
            out int pyTop,
            out int pcxWidth,
            out int pcyHeight,
            global::Windows.Win32.System.Variant.VARIANT varChild)
        {
            int l, t, w, h;
            var hr = accessible.accLocation(&l, &t, &w, &h, varChild);
            pxLeft = l; pyTop = t; pcxWidth = w; pcyHeight = h;
            return hr;
        }
    }
}

namespace System.Windows.Forms
{
    internal static unsafe class ControlOleExtensions
    {
        public static global::Windows.Win32.Foundation.HRESULT GetMiscStatus(
            this Control control,
            global::Windows.Win32.System.Com.DVASPECT dwAspect,
            out global::Windows.Win32.System.Ole.OLEMISC pdwStatus)
        {
            pdwStatus = default;
            return global::Windows.Win32.Foundation.HRESULT.S_OK;
        }
    }
}

namespace System.Drawing
{
    internal static class ImageStreamExtensions
    {
        public static void Save(this Image image, global::System.IO.Stream stream) => image.Save(stream, global::System.Drawing.Imaging.ImageFormat.Png);
    }
}
