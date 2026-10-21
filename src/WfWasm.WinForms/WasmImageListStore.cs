namespace System.Windows.Forms;

// Managed HIMAGELIST backing store. Headless has no comctl32, so
// imagelist contents live here, keyed by minted handle tokens in the
// 0x4000 range. Designer streams hydrate through WasmResourceShim (MSFt
// decode fills the token minted by ImageList_Read); Draw/DrawEx paint
// through the HDC Graphics bridge (same registry as WasmFillBridge),
// which is also how ImageList.Images[i] resolves (upstream GetBitmap
// paints via DrawEx into a scratch bitmap). Bitmaps are shared by
// reference across Duplicate clones and never disposed here (managed
// int[] pixels, GC-collected when the last entry drops).
internal static class WasmImageListStore
{
    private sealed class Entry
    {
        public List<System.Drawing.Bitmap> Images = new();
        public int Cx;
        public int Cy;
        public int Depth;
        public int Version;
        public System.Drawing.Bitmap? CachedStrip;
        public nint CachedStripHandle;
        public int StripBuiltForVersion = -1;
    }

    private static readonly Dictionary<nint, Entry> s_lists = new();
    private static int s_next = 0x4000;
    private static readonly object s_lock = new();

    internal static nint Create(int cx, int cy, int depth)
    {
        lock (s_lock)
        {
            nint token = s_next++;
            s_lists[token] = new Entry { Cx = cx, Cy = cy, Depth = depth };
            return token;
        }
    }

    internal static nint Duplicate(nint token)
    {
        lock (s_lock)
        {
            nint clone = s_next++;
            if (s_lists.TryGetValue(token, out Entry? source) && source is not null)
            {
                s_lists[clone] = new Entry
                {
                    Images = new List<System.Drawing.Bitmap>(source.Images),
                    Cx = source.Cx,
                    Cy = source.Cy,
                    Depth = source.Depth,
                };
            }
            else
            {
                s_lists[clone] = new Entry();
            }
            return clone;
        }
    }

    internal static void Destroy(nint token)
    {
        lock (s_lock)
        {
            if (s_lists.TryGetValue(token, out Entry? doomed) && doomed is not null && doomed.CachedStripHandle != 0)
                global::System.Drawing.BitmapHandleMap.Release(doomed.CachedStripHandle);
            s_lists.Remove(token);
        }
    }

    // Releases the superseded strip registration when an entry is wholesale
    // replaced; returns the list unchanged so SetImages can call it inside
    // the object initializer (which evaluates before the assignment).
    private static List<System.Drawing.Bitmap> AdoptImages(nint token, List<System.Drawing.Bitmap> images)
    {
        if (s_lists.TryGetValue(token, out Entry? prior) && prior is not null && prior.CachedStripHandle != 0)
            global::System.Drawing.BitmapHandleMap.Release(prior.CachedStripHandle);
        return images;
    }

    internal static void SetImages(nint token, System.Drawing.Bitmap[] images, int cx, int cy, int depth)
    {
        lock (s_lock)
        {
            s_lists[token] = new Entry
            {
                Images = AdoptImages(token, new List<System.Drawing.Bitmap>(images)),
                Cx = cx,
                Cy = cy,
                Depth = depth,
            };
        }
    }

    // Managed insertion: the ImageList_Add/Replace shims resolve the
    // source Bitmap from the HBITMAP registry (see BitmapHandleMap) and
    // store the actual pixels. Mask HBITMAPs are ignored: decoded and
    // 32bpp content carries alpha intrinsically; sub-32bpp mask folding
    // is a known gap (documented at the shim). Bounds-checked; unknown
    // tokens or indices fail honestly so callers keep their error paths.
    internal static int AddImage(nint token, System.Drawing.Bitmap image)
    {
        lock (s_lock)
        {
            if (!s_lists.TryGetValue(token, out Entry? entry) || entry is null)
                return -1;
            entry.Images.Add(image);
            entry.Version++;
            return entry.Images.Count - 1;
        }
    }

    internal static bool SetImage(nint token, int index, System.Drawing.Bitmap image)
    {
        lock (s_lock)
        {
            if (!s_lists.TryGetValue(token, out Entry? entry) || entry is null)
                return false;
            if (index < 0 || index >= entry.Images.Count)
                return false;
            entry.Images[index] = image;
            entry.Version++;
            return true;
        }
    }

    internal static bool RemoveAt(nint token, int index)
    {
        lock (s_lock)
        {
            if (!s_lists.TryGetValue(token, out Entry? entry) || entry is null)
                return false;
            if (index == -1)
            {
                entry.Images.Clear();
                entry.Version++;
                return true;
            }
            if (index < 0 || index >= entry.Images.Count)
                return false;
            entry.Images.RemoveAt(index);
            entry.Version++;
            return true;
        }
    }

    // Native image lists expose the whole strip through GetImageInfo and
    // callers (ImageList.GetBitmap) index frames inside it with LockBits +
    // stride offsets, frame i at x = i * cx (native comctl32 strips are
    // horizontal). Replicate that layout at
    // 32bpp (GetImageInfo only feeds the 32bpp GetBitmap path; narrower
    // lists use the DrawEx fallback instead). Null when the token is
    // unknown or empty.
    internal static System.Drawing.Bitmap? BuildStrip(nint token)
    {
        lock (s_lock)
        {
            if (!s_lists.TryGetValue(token, out Entry? entry) || entry is null)
                return null;
            if (entry.Images.Count == 0)
                return null;
            var strip = new System.Drawing.Bitmap(entry.Cx * entry.Images.Count, entry.Cy,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(strip))
            {
                for (int i = 0; i < entry.Images.Count; i++)
                    g.DrawImage(entry.Images[i], i * entry.Cx, 0);
            }
            return strip;
        }
    }

    // Versioned whole-strip cache for the GetImageInfo contract. The live
    // strip stays rooted here: a method-local strip would be GC-eligible
    // before the caller's FromHbitmap resolves its weak handle (the IMAGEINFO
    // carries only the handle value). Mutations bump Version so the next
    // call rebuilds; the superseded handle is released, so stale-handle
    // callers keep the honest failure. Unknown/empty/out-of-range fails.
    internal static bool TryGetStripHandle(nint token, int index, out nint stripHandle)
    {
        lock (s_lock)
        {
            stripHandle = 0;
            if (!s_lists.TryGetValue(token, out Entry? entry) || entry is null)
                return false;
            if (index < 0 || index >= entry.Images.Count)
                return false;
            if (entry.CachedStrip is null || entry.StripBuiltForVersion != entry.Version)
            {
                if (entry.CachedStripHandle != 0)
                    global::System.Drawing.BitmapHandleMap.Release(entry.CachedStripHandle);
                entry.CachedStrip = null;
                entry.CachedStripHandle = 0;
                // Re-entrant lock: BuildStrip takes s_lock on the same thread.
                System.Drawing.Bitmap? fresh = BuildStrip(token);
                if (fresh is null)
                    return false;
                entry.CachedStrip = fresh;
                entry.CachedStripHandle = global::System.Drawing.BitmapHandleMap.Register(fresh);
                entry.StripBuiltForVersion = entry.Version;
            }
            stripHandle = entry.CachedStripHandle;
            return stripHandle != 0;
        }
    }

    internal static int GetCount(nint token)
    {
        lock (s_lock)
        {
            return s_lists.TryGetValue(token, out Entry? entry) && entry is not null
                ? entry.Images.Count
                : 0;
        }
    }

    internal static bool GetIconSize(nint token, out int cx, out int cy)
    {
        lock (s_lock)
        {
            if (s_lists.TryGetValue(token, out Entry? entry) && entry is not null)
            {
                cx = entry.Cx;
                cy = entry.Cy;
                return true;
            }
        }
        cx = 0;
        cy = 0;
        return false;
    }

    internal static int GetDepth(nint token)
    {
        lock (s_lock)
        {
            return s_lists.TryGetValue(token, out Entry? entry) && entry is not null
                ? entry.Depth
                : 0;
        }
    }

    internal static bool TryGetImage(nint token, int index, out System.Drawing.Bitmap? image)
    {
        lock (s_lock)
        {
            image = null;
            if (!s_lists.TryGetValue(token, out Entry? entry) || entry is null)
                return false;
            if (index < 0 || index >= entry.Images.Count)
                return false;
            image = entry.Images[index];
            return image is not null;
        }
    }

    // Paints slot [index] at (x, y), stretched to (dx, dy) when positive
    // (ImageList_DrawEx stretch semantics; non-positive falls back to the
    // slot size). Styles, blend modes, and bk/fg colors are approximated
    // as a plain draw: the mask is already folded into alpha at decode.
    // An unregistered HDC answers success without painting (today's
    // behavior for unmapped device contexts); unknown tokens fail.
    internal static bool Draw(nint token, int index, nint hdcValue, int x, int y, int dx, int dy)
    {
        if (!TryGetImage(token, index, out System.Drawing.Bitmap? image) || image is null)
            return false;
        System.Drawing.Graphics? graphics;
        try
        {
            var hdc = (global::Windows.Win32.Graphics.Gdi.HDC)hdcValue;
            if (!global::Windows.Win32.PInvoke.WasmFillBridge.TryGetTarget(hdc, out graphics) || graphics is null)
                return true;
        }
        catch
        {
            return true;
        }
        try
        {
            int w = dx > 0 ? dx : image.Width;
            int h = dy > 0 ? dy : image.Height;
            graphics.DrawImage(image, x, y, w, h);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
