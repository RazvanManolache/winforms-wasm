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
    [JSImport("globalThis.wfwasmTextMeasure")]
    internal static partial double TextMeasureWidth(string text, string fontCss, int fontPx, int maxW);

    // Private-font bridge (Syncfusion/Telerik icon fonts): AddMemoryFont
    // bytes are registered with the browser via FontFace so canvas
    // fillText resolves them. Check is synchronous (document.fonts.check);
    // registration is fire-and-forget (FontFace.load promise); the tofu
    // purge below re-rasters once the family confirms loaded.
    [JSImport("globalThis.wfwasmFontCheck")]
    internal static partial bool FontCheck(string family);
    [JSImport("globalThis.wfwasmRegisterFont")]
    internal static partial void RegisterFont(string family, string base64);

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

    // Paint-path twin of MeasureOverride: unit tests inject a fixed
    // raster to prove Graphics.DrawString placement/clipping headless
    // (clip math vs int.MaxValue layout rects). Production leaves it
    // null; checked before IsAvailable so headless tests can paint.
    internal static Func<string, Font, float, int, Raster?>? RasterOverride;

    // Headless-test seam for fractional advances (mirrors MeasureOverride):
    // production leaves it null and TryMeasureWidth falls through to the
    // browser canvas measure. Never throws.
    internal static Func<string, Font, float, int, float?>? MeasureWidthOverride;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_fontConfirmed =
        new(System.StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> s_fontKickStarted =
        new(System.StringComparer.Ordinal);

    // Fire-and-forget browser registration for a private family. Called
    // eagerly from AddMemoryFont (wins the boot race) and as a backstop
    // from EnsurePrivateFont. Never throws.
    internal static void KickFontRegistration(string family, byte[] bytes)
    {
        try
        {
            if (s_fontKickStarted.TryAdd(family, 1))
            {
                try { RegisterFont(family, System.Convert.ToBase64String(bytes)); }
                catch { }
            }
        }
        catch { }
    }

    // Ensures a private (AddMemoryFont) family is registered with the
    // browser before raster/measure. System families miss the registry
    // (one dictionary lookup). FontCheck scans document.fonts membership
    // (document.fonts.check reports unknown families as loaded, so
    // membership is scanned instead). Never throws:
    // any bridge failure simply retries next paint.
    internal static void EnsurePrivateFont(Font font)
    {
        try
        {
            string name = font.Name;
            if (string.IsNullOrWhiteSpace(name) || s_fontConfirmed.ContainsKey(name))
                return;
            if (!System.Drawing.Text.PrivateFontCollection.TryGetPrivateFontBytes(name, out byte[]? bytes) || bytes is null)
                return;
            bool loaded;
            try { loaded = FontCheck(name); }
            catch { return; }
            if (loaded)
            {
                // Family arrived (eager kick won the race, or a previous
                // raster kicked it): purge any tofu cached before confirm.
                if (s_fontConfirmed.TryAdd(name, 1))
                {
                    s_cache.Clear();
                    s_widthCache.Clear();
                }
                return;
            }
            KickFontRegistration(name, bytes);
        }
        catch { }
    }

    internal static Raster? TryMeasure(string text, Font font, float dpi, int maxW)
    {
        EnsurePrivateFont(font);
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

    // ToCss value cache: the css depends only on (name, size, style,
    // dpi), but every span rebuilt it (Round + Replace + interp) plus
    // full-text key interpolations per DrawString. Struct key, no
    // key-string alloc; bit-identical output (same builder on miss).
    private readonly record struct CssKey(string Name, int SizeBits, bool Italic, bool Bold, int DpiBits);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<CssKey, string> s_cssCache = new();

    internal static string ToCss(Font font, float dpi)
    {
        ArgumentNullException.ThrowIfNull(font);
        var key = new CssKey(font.Name, BitConverter.SingleToInt32Bits(font.SizeInPoints), font.Italic, font.Bold, BitConverter.SingleToInt32Bits(dpi));
        if (s_cssCache.TryGetValue(key, out string? css))
            return css;
        css = BuildCss(font, dpi);
        if (s_cssCache.Count > 1024)
            s_cssCache.Clear();
        s_cssCache[key] = css;
        return css;
    }

    private static string BuildCss(Font font, float dpi)
    {
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
        EnsurePrivateFont(font);
        if (TextRasterCounters.PerfEnabled)
            TextRasterCounters.NoteRasterCall();
        var paint = RasterOverride;
        if (paint is not null)
        {
            try
            {
                return paint(text, font, dpi, maxW);
            }
            catch
            {
                return null;
            }
        }

        if (string.IsNullOrEmpty(text) || !IsAvailable)
            return null;
        string css = ToCss(font, dpi);
        int px = ToPx(font, dpi);
        // Single-line fast lane first: the 2048 full-width raster serves
        // every maxW it fits (no wrap can differ when it fits: the JS
        // wrapper only wraps below the natural width, so the bytes are
        // identical to an exact-maxW fetch and the caller still clips
        // to its rect). One key build + one lookup replaces the
        // exact-then-tier2 pair for the common span; multi-line and
        // overflow keep the exact path below.
        if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0)
        {
            string fullKey = $"{css}\n2048\n{text}";
            if (s_cache.TryGetValue(fullKey, out Raster? full))
            {
                if (TextRasterCounters.PerfEnabled)
                    TextRasterCounters.NoteRasterHit();
                if (full.Height == FullLineHeight(px) && full.Width <= maxW)
                    return full;
            }
            else
            {
                full = FetchRaster(text, css, px, 2048, fullKey);
                if (full is null)
                    return null;
                if (full.Height == FullLineHeight(px) && full.Width <= maxW)
                    return full;
            }
        }
        string key = $"{css}\n{maxW}\n{text}";
        if (s_cache.TryGetValue(key, out Raster? hit))
        {
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteRasterHit();
            return hit;
        }
        return FetchRaster(text, css, px, maxW, key);
    }

    // Single JSImport plus validation plus exact-key store. Counts the
    // miss. Null when the bridge fails (callers keep honest behavior).
    private static Raster? FetchRaster(string text, string css, int px, int maxW, string key)
    {
        try
        {
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteRasterMiss();
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

    // ceil(px * 1.25), the JS scratch-canvas line height. Integer-exact
    // for px >= 1: ceil(5px/4) == (5px+3)/4.
    internal static int FullLineHeight(int px) => (px * 5 + 3) / 4;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, float> s_widthCache = new();

    internal static float? TryMeasureWidth(string text, Font font, float dpi, int maxW)
    {
        EnsurePrivateFont(font);
        if (TextRasterCounters.PerfEnabled)
            TextRasterCounters.NoteMeasureWidthCall();
        var probe = MeasureWidthOverride;
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

        if (string.IsNullOrEmpty(text) || !IsAvailable)
            return null;
        string css = ToCss(font, dpi);
        int px = ToPx(font, dpi);
        string key = $"{css}\n{maxW}\n{text}";
        if (s_widthCache.TryGetValue(key, out float hit))
            return hit;
        // Tier-2 single-word path (raster twin above): the JS measure
        // wraps only below the natural width, and a float carries no
        // line count, so reuse needs a stronger shape — one word, no
        // newlines, full measure under 2048 (no wrap at full width),
        // within maxW (no wrap at the requested width either), and
        // maxW itself capped (unbounded Telerik measures must take
        // the exact path: a wrapped full measure could slip the
        // under-2048 check while differing from the true width).
        // Then the exact measure equals the full measure.
        if (maxW <= 2048 && text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0 && text.IndexOf(' ') < 0)
        {
            string fullKey = $"{css}\n2048\n{text}";
            if (!s_widthCache.TryGetValue(fullKey, out float fw))
            {
                float? fetched = FetchWidth(text, css, px, 2048, fullKey);
                if (fetched is null)
                    return null;
                fw = fetched.Value;
            }
            if (fw < 2048f && fw <= maxW)
                return fw;
        }
        return FetchWidth(text, css, px, maxW, key);
    }

    // Single JSImport plus validation plus exact-key store. Width
    // imports cost like raster imports, so they share RasterMisses.
    private static float? FetchWidth(string text, string css, int px, int maxW, string key)
    {
        try
        {
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteRasterMiss();
            double w = TextMeasureWidth(text, css, px, Math.Max(1, maxW));
            if (w <= 0 || double.IsNaN(w) || double.IsInfinity(w))
                return null;
            float f = (float)w;
            if (s_widthCache.Count > 512)
                s_widthCache.Clear();
            s_widthCache[key] = f;
            return f;
        }
        catch
        {
            return null;
        }
    }
}
