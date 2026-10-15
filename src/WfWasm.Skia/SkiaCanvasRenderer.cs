using SkiaSharp;
using WfWasm.Renderer;
using WfWasm.Shim;

namespace WfWasm.Skia;

/// <summary>
/// Real-pixel canvas backend for desktop use (tests, PNG snapshots):
/// the same paint commands as the SVG backend, rasterized with Skia.
/// Browser delivery uses the Canvas2D bridge instead, so this assembly
/// (and its native assets) never ships to the browser.
/// </summary>
public sealed class SkiaCanvasRenderer : ICanvasRenderer, IDisposable
{
    private SKSurface? _surface;
    private SKCanvas? _canvas;
    private bool _open;
    private bool _disposed;
    private bool _ownsCanvas = true;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Owned-surface mode: frames create their own canvas.</summary>
    public SkiaCanvasRenderer()
    {
    }

    /// <summary>
    /// Attach to a caller-owned canvas. The caller drives frames via
    /// <see cref="BeginFrame"/>/<see cref="EndFrame"/> and retains ownership;
    /// <see cref="GetPixelRgba"/> and <see cref="ToPng"/> are unavailable here.
    /// </summary>
    public SkiaCanvasRenderer(SKCanvas canvas, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame must be positive.");
        _canvas = canvas;
        Width = width;
        Height = height;
        _ownsCanvas = false;
    }

    public void BeginFrame(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame must be positive.");
        if (!_ownsCanvas)
        {
            if (width != Width || height != Height)
                throw new ArgumentException("External canvas has fixed dimensions.", nameof(width));
            _open = true;
            return;
        }

        EndCurrent();
        _surface = SKSurface.Create(new SKImageInfo(width, height));
        if (_surface is null)
            throw new InvalidOperationException("Could not create Skia surface.");
        _canvas = _surface.Canvas;
        Width = width;
        Height = height;
        _open = true;
    }

    public void FillRectangle(float x, float y, float width, float height, uint rgba)
    {
        RequireOpen();
        using var paint = FillPaint(rgba);
        Canvas.DrawRect(x, y, width, height, paint);
    }

    public void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba)
    {
        RequireOpen();
        using var paint = FillPaint(rgba);
        Canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + width, y + height), radius, radius), paint);
    }

    public void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba)
    {
        RequireOpen();
        using var paint = FillPaint(0xFFFFFFFFu);
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, y), new SKPoint(0, y + height),
            [ToColor(topRgba), ToColor(bottomRgba)], null, SKShaderTileMode.Clamp);
        Canvas.DrawRect(x, y, width, height, paint);
    }

    public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba)
    {
        RequireOpen();
        using var paint = StrokePaint(rgba, width);
        Canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    public void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null)
    {
        RequireOpen();
        var rect = new SKRect(x, y, x + width, y + height);
        if (fillRgba.HasValue)
        {
            using var fill = FillPaint(fillRgba.Value);
            Canvas.DrawOval(rect, fill);
        }

        using var stroke = StrokePaint(outlineRgba, lineWidth);
        Canvas.DrawOval(rect, stroke);
    }

    public void DrawText(string text, float x, float y, string fontCss, uint rgba) =>
        DrawTextAligned(text, x, y, fontCss, rgba, TextHAlign.Left, TextVAlign.Baseline);

    public void DrawTextAligned(
        string text, float x, float y, string fontCss, uint rgba,
        TextHAlign hAlign, TextVAlign vAlign, int mnemonicIndex = -1)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fontCss);
        using var font = ParseFont(fontCss, out bool fakeBold);
        using var paint = FillPaint(rgba);
        // The requested family may not ship a bold face (or the family may be
        // unknown to Skia); embolden with a thin stroke like browsers and
        // GDI+ do rather than silently rendering regular.
        if (fakeBold)
        {
            paint.Style = SKPaintStyle.StrokeAndFill;
            paint.StrokeWidth = Math.Max(0.75f, font.Size * 0.035f);
        }
        SKTextAlign align = hAlign switch
        {
            TextHAlign.Center => SKTextAlign.Center,
            TextHAlign.Right => SKTextAlign.Right,
            _ => SKTextAlign.Left,
        };
        float baseline = y;
        if (vAlign != TextVAlign.Baseline)
        {
            font.GetFontMetrics(out SKFontMetrics metrics);
            float ascent = -metrics.Ascent;
            float descent = metrics.Descent;
            baseline = vAlign switch
            {
                TextVAlign.Top => y + ascent,
                TextVAlign.Middle => y + (ascent - descent) / 2f,
                _ => y - descent,
            };
        }

        Canvas.DrawText(text, x, baseline, align, font, paint);

        if ((uint)mnemonicIndex < (uint)text.Length)
        {
            // Underline the mnemonic char: measure the prefix with the same font,
            // adjusting for horizontal alignment.
            float prefix = font.MeasureText(text[..mnemonicIndex]);
            float glyph = Math.Max(1, font.MeasureText(text.Substring(mnemonicIndex, 1)) - 1);
            float start = hAlign switch
            {
                TextHAlign.Center => x - font.MeasureText(text) / 2f + prefix,
                TextHAlign.Right => x - font.MeasureText(text) + prefix,
                _ => x + prefix,
            };
            Canvas.DrawRect(start, baseline + 1, glyph, 1, paint);
        }
    }

    public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh)
    {
        ThrowIfDisposed();
        RequireOpen();
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || dw <= 0 || dh <= 0)
            return;
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("RGBA buffer is smaller than width*height*4.", nameof(rgba));
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        int i = 0;
        for (int py = 0; py < height; py++)
            for (int px = 0; px < width; px++)
            {
                bitmap.SetPixel(px, py, new SKColor(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]));
                i += 4;
            }
        Canvas.DrawBitmap(bitmap, new SKRect(x, y, x + dw, y + dh));
    }

    public void EndFrame()
    {
        ThrowIfDisposed();
        RequireOpen();
        Canvas.Flush();
        _open = false;
    }

    /// <summary>Pixel as 0xRRGGBBAA. Requires a begun (possibly ended) frame.</summary>
    public uint GetPixelRgba(int x, int y)
    {
        ThrowIfDisposed();
        if (_surface is null)
            throw new InvalidOperationException("BeginFrame must be called first.");
        using SKImage image = _surface.Snapshot();
        using SKBitmap bitmap = SKBitmap.FromImage(image);
        SKColor color = bitmap.GetPixel(x, y);
        return ((uint)color.Red << 24) | ((uint)color.Green << 16) | ((uint)color.Blue << 8) | color.Alpha;
    }

    /// <summary>Current frame encoded as PNG.</summary>
    public byte[] ToPng()
    {
        ThrowIfDisposed();
        if (_surface is null)
            throw new InvalidOperationException("BeginFrame must be called first.");
        using SKImage image = _surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        EndCurrent();
    }

    public override string ToString() => $"SkiaCanvasRenderer({Width}x{Height})";

    private SKCanvas Canvas
    {
        get
        {
            ThrowIfDisposed();
            return _canvas ?? throw new InvalidOperationException("BeginFrame must be called first.");
        }
    }

    private void RequireOpen()
    {
        if (!_open)
            throw new InvalidOperationException("BeginFrame must be called first.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void EndCurrent()
    {
        if (_ownsCanvas)
        {
            _canvas?.Dispose();
            _canvas = null;
        }

        _surface?.Dispose();
        _surface = null;
        _open = false;
    }

    private static SKPaint FillPaint(uint rgba) => new()
    {
        Color = ToColor(rgba),
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private static SKPaint StrokePaint(uint rgba, float width) => new()
    {
        Color = ToColor(rgba),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1, width),
        StrokeCap = SKStrokeCap.Round,
        IsAntialias = true,
    };

    private static SKColor ToColor(uint rgba) => new(
        (byte)(rgba >> 24),
        (byte)((rgba >> 16) & 0xFF),
        (byte)((rgba >> 8) & 0xFF),
        (byte)(rgba & 0xFF));

    private static SKFont ParseFont(string fontCss, out bool fakeBold)
    {
        fakeBold = false;
        string rest = fontCss;
        bool bold = StripPrefix(ref rest, "bold ");
        bool italic = StripPrefix(ref rest, "italic ");
        string[] parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float size = 13;
        string family = "sans-serif";
        if (parts.Length > 0 && parts[0].EndsWith("px", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(parts[0][..^2], out float parsed))
        {
            size = parsed;
            family = parts.Length > 1 ? CleanFamily(string.Join(' ', parts[1..])) : "sans-serif";
        }

        SKTypeface? face = SKTypeface.FromFamilyName(
            family, bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        face ??= SKTypeface.Default;
        fakeBold = bold && (int)face.FontStyle.Weight < (int)SKFontStyleWeight.SemiBold;
        return new SKFont(face, size);
    }

    private static bool StripPrefix(ref string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        text = text[prefix.Length..];
        return true;
    }

    /// <summary>First comma-separated family with CSS quotes trimmed.</summary>
    private static string CleanFamily(string families)
    {
        string first = families.Split(',')[0].Trim();
        return first.Trim('\'', '"', ' ');
    }
}
