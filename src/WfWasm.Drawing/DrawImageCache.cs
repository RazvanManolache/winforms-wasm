using System.Drawing.Drawing2D;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace System.Drawing;

// DrawImage result memo (per-frame temp-render eliminator): transformed
// owner paint (Telerik photo tiles, rotated items) redraws the same
// static source pixels with bit-identical inputs every frame
// (~500ms/frame, clip-independent). The Blit and parallelogram loops
// recall the sampled layer instead of re-running the transform math.
// Entries are pure functions of the key (source identity + paint
// version + src rect + dst size/transform) covering the full output
// (dw*dh, or the full inked bbox); position, clip, dst bounds and blend
// mode apply fresh at write time, so a hit writes bit-identical pixels
// to a direct draw (pinned by Memo_Hit_Equals_Direct) and recalls
// across moves and clip changes. Bounds: 8 entries per source, 256K
// pixels per layer, sources held weakly (entries die with the bitmap).
// Exactness guards: PaintVersion (bumped by every mutation path, so a
// changed source misses), no self-draw memo (src == dst would stash
// mid-draw mutations), no complex clips (the visited set must be a
// hole-free rect; null/infinite/single-int-rect only), empty draws
// bypass. Threading matches GDI+ (instance-unsafe); only the bucket
// lists lock (against list corruption, never for pixel correctness).
internal readonly struct DrawImageKey : IEquatable<DrawImageKey>
{
    public readonly byte Kind; // 0 = axis-aligned Blit, 1 = parallelogram
    public readonly int SrcVersion;
    public readonly float Sx, Sy, Sw, Sh;
    public readonly int Dw, Dh; // Blit output size (parallelogram: unused)
    public readonly Matrix3x2 WorldToLocal; // parallelogram sampling transform
    public readonly Matrix3x2 FitToUnit; // parallelogram unit-square fit
    public readonly int Interp; // InterpolationMode bits (nearest today; future-proof)

    public DrawImageKey(byte kind, int srcVersion, float sx, float sy, float sw, float sh,
        int dw, int dh, Matrix3x2 worldToLocal, Matrix3x2 fitToUnit, InterpolationMode interp)
    {
        Kind = kind;
        SrcVersion = srcVersion;
        Sx = sx;
        Sy = sy;
        Sw = sw;
        Sh = sh;
        Dw = dw;
        Dh = dh;
        WorldToLocal = worldToLocal;
        FitToUnit = fitToUnit;
        Interp = (int)interp;
    }

    public bool Equals(DrawImageKey other) =>
        Kind == other.Kind && SrcVersion == other.SrcVersion &&
        Sx == other.Sx && Sy == other.Sy && Sw == other.Sw && Sh == other.Sh &&
        Dw == other.Dw && Dh == other.Dh &&
        WorldToLocal == other.WorldToLocal && FitToUnit == other.FitToUnit &&
        Interp == other.Interp;

    public override bool Equals(object? obj) => obj is DrawImageKey other && Equals(other);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Kind);
        h.Add(SrcVersion);
        h.Add(Sx);
        h.Add(Sy);
        h.Add(Sw);
        h.Add(Sh);
        h.Add(Dw);
        h.Add(Dh);
        h.Add(WorldToLocal);
        h.Add(FitToUnit);
        h.Add(Interp);
        return h.ToHashCode();
    }
}

internal sealed class DrawImageEntry
{
    public DrawImageKey Key;
    public int[] Pixels = Array.Empty<int>(); // visited-bounds raster, row-major
    public int Ox, Oy, W, H; // visited bounds in dst coords
    public bool AllOpaque; // every stored pixel has a=255
    public byte[]? Inked; // parallelogram inked bitmask ((W*H+7)/8); null = all inked
}

internal static class DrawImageCache
{
    internal const int MaxEntriesPerSource = 8;
    internal const int MaxLayerPixels = 256 * 1024;

    private sealed class Bucket
    {
        public readonly List<DrawImageEntry> Entries = new();
        public readonly List<DrawImageKey> Seen = new();
    }

    private static readonly ConditionalWeakTable<Bitmap, Bucket> s_buckets = new();

    // Blit recall: the layer covers the full dw*dh output, so the key
    // alone decides (position/clip/dst apply fresh at write time).
    public static bool TryRecallBlit(Bitmap src, in DrawImageKey key, out DrawImageEntry? entry)
    {
        entry = null;
        if (!s_buckets.TryGetValue(src, out Bucket? bucket))
            return false;
        lock (bucket)
        {
            foreach (DrawImageEntry e in bucket.Entries)
            {
                if (e.Key.Equals(key))
                {
                    entry = e;
                    return true;
                }
            }
        }

        return false;
    }

    // Parallelogram recall: the layer covers the full inked bbox, so the
    // caller additionally validates the recomputed bbox (deterministic
    // from the key; guards float wobble).
    public static bool TryRecallPara(Bitmap src, in DrawImageKey key, int ox, int oy, int w, int h,
        out DrawImageEntry? entry)
    {
        entry = null;
        if (!s_buckets.TryGetValue(src, out Bucket? bucket))
            return false;
        lock (bucket)
        {
            foreach (DrawImageEntry e in bucket.Entries)
            {
                if (e.Ox == ox && e.Oy == oy && e.W == w && e.H == h && e.Key.Equals(key))
                {
                    entry = e;
                    return true;
                }
            }
        }

        return false;
    }

    // Shared write-set computation (miss and hit loops must visit exactly
    // the same pixels): intersects the candidate bounds with a rect clip
    // (null/infinite clips accept everything) and reports whether
    // per-pixel checks remain (complex clips only).
    public static bool IntersectWrite(Region? clip, ref int x0, ref int y0, ref int x1, ref int y1)
    {
        if (clip is null || clip.IsInfinite(null))
            return false;
        if (clip.TryGetIntRect(out int cx0, out int cy0, out int cx1, out int cy1))
        {
            x0 = Math.Max(x0, cx0);
            y0 = Math.Max(y0, cy0);
            x1 = Math.Min(x1, cx1);
            y1 = Math.Min(y1, cy1);
            return false;
        }

        return true;
    }

    // Repeat gate: one-shot draws (a dragged photo's ever-new positions)
    // must not pay stash allocation + eviction churn every frame. The first
    // sighting records the key and runs direct; the repeat stashes, so a
    // steady draw still recalls from its third paint on. Cache policy only:
    // pixels are identical either way.
    public static bool NoteRepeat(Bitmap src, in DrawImageKey key)
    {
        Bucket bucket = s_buckets.GetValue(src, _ => new Bucket());
        lock (bucket)
        {
            foreach (DrawImageKey seen in bucket.Seen)
            {
                if (seen.Equals(key))
                    return true;
            }
            if (bucket.Seen.Count >= MaxEntriesPerSource)
                bucket.Seen.RemoveAt(0);
            bucket.Seen.Add(key);
            return false;
        }
    }

    public static void Store(Bitmap src, DrawImageEntry entry)
    {
        Bucket bucket = s_buckets.GetValue(src, _ => new Bucket());
        lock (bucket)
        {
            for (int i = bucket.Entries.Count - 1; i >= 0; i--)
            {
                DrawImageEntry e = bucket.Entries[i];
                if (e.Ox == entry.Ox && e.Oy == entry.Oy && e.W == entry.W && e.H == entry.H &&
                    e.Key.Equals(entry.Key))
                    bucket.Entries.RemoveAt(i);
            }

            while (bucket.Entries.Count >= MaxEntriesPerSource)
                bucket.Entries.RemoveAt(0);
            bucket.Entries.Add(entry);
        }
    }

    // Memo gate for the visited set: null/infinite/single-int-rect clips
    // visit hole-free rects; anything else runs direct (unmemoized).
    public static bool MemoizableClip(Region? clip) =>
        clip is null || clip.IsInfinite(null) || clip.TryGetIntRect(out _, out _, out _, out _);

    public static bool KeyForBlit(Bitmap src, Bitmap? target, float sx, float sy, float sw, float sh,
        int dw, int dh, InterpolationMode interp, Region? clip, out DrawImageKey key)
    {
        key = default;
        if (ReferenceEquals(src, target) || !MemoizableClip(clip))
            return false;
        if ((long)dw * dh > MaxLayerPixels)
            return false;
        key = new DrawImageKey(0, src.PaintVersion, sx, sy, sw, sh, dw, dh,
            default, default, interp);
        return true;
    }

    public static bool KeyForPara(Bitmap src, Bitmap? target, RectangleF srcRect,
        Matrix3x2 worldToLocal, Matrix3x2 fitToUnit, int bw, int bh,
        InterpolationMode interp, Region? clip, out DrawImageKey key)
    {
        key = default;
        if (ReferenceEquals(src, target) || !MemoizableClip(clip))
            return false;
        if ((long)bw * bh > MaxLayerPixels)
            return false;
        key = new DrawImageKey(1, src.PaintVersion, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height,
            0, 0, worldToLocal, fitToUnit, interp);
        return true;
    }
}
