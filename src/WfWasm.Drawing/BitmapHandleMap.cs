// Headless there are no GDI handles, but managed Bitmaps still cross
// HBITMAP-typed seams: ImageList insertion converts via GetHbitmap, so the
// insertion shim must recover the source pixels. This registry mints
// trackable fake handles (0x62 range, never colliding with the 0x60 image-info
// fakes or the constant CreateBitmap stub) resolving back to the bitmap.
// Two lanes: weak registrations (GetHbitmap aliases a caller-owned bitmap;
// entries die with the bitmap) and owned registrations (CreateTracked
// bitmaps have no other owner, so the map roots them strongly until
// Release, per the GDI DeleteObject pairing contract). Unknown or
// released handles resolve false (callers keep their honest failure paths).

namespace System.Drawing;

internal static class BitmapHandleMap
{
    private static readonly Dictionary<nint, WeakReference<Bitmap>> s_handles = new();
    private static readonly Dictionary<nint, Bitmap> s_owned = new();
    private static int s_next = 0x62000000;
    private static readonly object s_lock = new();

    internal static nint Register(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        lock (s_lock)
        {
            nint fake = s_next++;
            s_handles[fake] = new WeakReference<Bitmap>(bitmap);
            return fake;
        }
    }

    internal static nint RegisterOwned(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        lock (s_lock)
        {
            nint fake = s_next++;
            s_owned[fake] = bitmap;
            return fake;
        }
    }

    internal static bool TryResolve(nint handle, out Bitmap? bitmap)
    {
        lock (s_lock)
        {
            bitmap = null;
            if (s_owned.TryGetValue(handle, out Bitmap? owned) && owned is not null)
            {
                bitmap = owned;
                return true;
            }
            return s_handles.TryGetValue(handle, out WeakReference<Bitmap>? weak)
                && weak is not null
                && weak.TryGetTarget(out bitmap)
                && bitmap is not null;
        }
    }

    internal static void Release(nint handle)
    {
        lock (s_lock)
        {
            s_handles.Remove(handle);
            s_owned.Remove(handle);
        }
    }
}
