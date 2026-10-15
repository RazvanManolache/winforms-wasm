using System.Runtime.InteropServices.JavaScript;

namespace System.Drawing;

// CPU text raster for Graphics.DrawString: the managed backend has no
// font engine, so glyph pixels come from a scratch browser canvas
// (canvas-bridge.js wfwasmTextRaster, synchronous via [JSImport]).
// The raster is white-with-alpha; the caller tints by the brush color,
// so one cached raster serves every color. Total: null/false on any
// failure (no browser, bad pixels), and the caller keeps throwing its
// honest NotSupportedException headless.
internal static partial class BrowserText
{
    [JSImport("globalThis.wfwasmTextCaps")]
    internal static partial string TextCaps();

    [JSImport("globalThis.wfwasmTextRaster")]
    internal static partial byte[]? TextRaster(string text, string fontCss, int fontPx, int maxW);

    private static int s_caps; // 0 = unprobed, 1 = available, -1 = unavailable

    internal static bool IsAvailable
    {
        get
        {
            if (s_caps != 0) return s_caps > 0;
            try
            {
                string caps = TextCaps();
                s_caps = caps is not null && caps.StartsWith("ok:", StringComparison.Ordinal) ? 1 : -1;
            }
            catch
            {
                s_caps = -1;
            }

            return s_caps > 0;
        }
    }

    internal sealed record Raster(int Width, int Height, byte[] Rgba);

    // Test seam for Graphics.MeasureString: headless test runs have no
    // browser canvas, so unit tests inject a fixed raster here to prove the
    // raster-measurement path without a browser. Production leaves it null
    // and TryMeasure falls through to TryRaster. Never throws.
    internal static Func<string, Font, float, int, Raster?>? MeasureOverride;

    internal static Raster? TryMeasure(string text, Font font, float dpi, int maxW)
    {
        var probe = MeasureOverride;
        if (probe is not null)
        {
            try
            {
                return probe(text, font, dpi, maxW);
            }
            catch
            {
                return null;
            }
        }

        return TryRaster(text, font, dpi, maxW);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Raster> s_cache = new();

    internal static string ToCss(Font font, float dpi)
    {
        ArgumentNullException.ThrowIfNull(font);
        int px = Math.Max(1, (int)MathF.Round(font.SizeInPoints * dpi / 72f));
        string style = font.Italic ? "italic " : string.Empty;
        string weight = font.Bold ? "bold " : string.Empty;
        string name = font.Name.Replace("'", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            name = "sans-serif";
        return $"{style}{weight}{px}px '{name}', sans-serif";
    }

    internal static int ToPx(Font font, float dpi) =>
        Math.Max(1, (int)MathF.Round(font.SizeInPoints * dpi / 72f));

    internal static Raster? TryRaster(string text, Font font, float dpi, int maxW)
    {
        if (string.IsNullOrEmpty(text) || !IsAvailable)
            return null;
        string css = ToCss(font, dpi);
        int px = ToPx(font, dpi);
        string key = $"{css}\n{maxW}\n{text}";
        if (s_cache.TryGetValue(key, out Raster? hit))
            return hit;
        try
        {
            byte[]? back = TextRaster(text, css, px, Math.Max(1, maxW));
            if (back is null || back.Length < 8)
                return null;
            int w = BitConverter.ToInt32(back, 0);
            int h = BitConverter.ToInt32(back, 4);
            if (w <= 0 || h <= 0 || back.Length != 8 + w * h * 4)
                return null;
            byte[] rgba = new byte[w * h * 4];
            Buffer.BlockCopy(back, 8, rgba, 0, rgba.Length);
            var raster = new Raster(w, h, rgba);
            if (s_cache.Count > 512)
                s_cache.Clear();
            s_cache[key] = raster;
            return raster;
        }
        catch
        {
            return null;
        }
    }
}
