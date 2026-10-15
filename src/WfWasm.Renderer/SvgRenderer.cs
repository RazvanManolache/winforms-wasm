using System.Globalization;
using System.Text;
using WfWasm.Shim;

namespace WfWasm.Renderer;

/// <summary>
/// Real SVG backend: every command becomes an element, so output can be
/// asserted textually and opened in a browser. No native dependencies.
/// </summary>
public sealed class SvgRenderer : ICanvasRenderer
{
    private readonly StringBuilder _svg = new();
    private bool _open;
    private int _gradientCount;

    public void BeginFrame(int width, int height)
    {
        _svg.Clear();
        _gradientCount = 0;
        _svg.Append(FormattableString.Invariant(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\">"));
        _open = true;
    }

    public void FillRectangle(float x, float y, float width, float height, uint rgba)
    {
        RequireOpen();
        _svg.Append(FormattableString.Invariant(
            $"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"#{rgba:x8}\"/>"));
    }

    public void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba)
    {
        RequireOpen();
        _svg.Append(FormattableString.Invariant(
            $"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" rx=\"{radius}\" fill=\"#{rgba:x8}\"/>"));
    }

    public void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba)
    {
        RequireOpen();
        int id = _gradientCount++;
        _svg.Append(CultureInfo.InvariantCulture,
            $"<defs><linearGradient id=\"g{id}\" x1=\"0\" y1=\"0\" x2=\"0\" y2=\"1\">" +
            $"<stop offset=\"0\" stop-color=\"#{topRgba:x8}\"/>" +
            $"<stop offset=\"1\" stop-color=\"#{bottomRgba:x8}\"/></linearGradient></defs>");
        _svg.Append(FormattableString.Invariant(
            $"<rect x=\"{x}\" y=\"{y}\" width=\"{width}\" height=\"{height}\" fill=\"url(#g{id})\"/>"));
    }

    public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba)
    {
        RequireOpen();
        _svg.Append(FormattableString.Invariant(
            $"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"#{rgba:x8}\" stroke-width=\"{width}\" stroke-linecap=\"round\"/>"));
    }

    public void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null)
    {
        RequireOpen();
        float cx = x + (width / 2f);
        float cy = y + (height / 2f);
        string fill = fillRgba.HasValue ? $"#{fillRgba.Value:x8}" : "none";
        _svg.Append(FormattableString.Invariant(
            $"<ellipse cx=\"{cx}\" cy=\"{cy}\" rx=\"{width / 2f}\" ry=\"{height / 2f}\" fill=\"{fill}\" stroke=\"#{outlineRgba:x8}\" stroke-width=\"{lineWidth}\"/>"));
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
        // SVG has no font metrics here: horizontal intent maps to text-anchor,
        // vertical intent to dominant-baseline. Mnemonic underlines need measured
        // widths, so the SVG reference backend records the text only.
        string anchor = hAlign switch
        {
            TextHAlign.Center => " text-anchor=\"middle\"",
            TextHAlign.Right => " text-anchor=\"end\"",
            _ => string.Empty,
        };
        string baseline = vAlign switch
        {
            TextVAlign.Top => " dominant-baseline=\"hanging\"",
            TextVAlign.Middle => " dominant-baseline=\"central\"",
            TextVAlign.Bottom => " dominant-baseline=\"text-after-edge\"",
            _ => string.Empty,
        };
        _svg.Append(FormattableString.Invariant(
            $"<text x=\"{x}\" y=\"{y}\"{anchor}{baseline} style=\"font:{fontCss};fill:#{rgba:x8}\">{Escape(text)}</text>"));
    }

    public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(rgba);
        // No raster encoder ships here: record the placement as an assertable
        // placeholder so SVG stays textual and valid.
        _svg.Append(FormattableString.Invariant(
            $"<g data-image=\"{width}x{height}\"><rect x=\"{x}\" y=\"{y}\" width=\"{dw}\" height=\"{dh}\" fill=\"none\" stroke=\"#888888ff\" stroke-width=\"1\"/></g>"));
    }

    public void EndFrame()
    {
        RequireOpen();
        _svg.Append("</svg>");
        _open = false;
    }

    public string GetSvg() => _svg.ToString();

    public override string ToString() => GetSvg();

    private void RequireOpen()
    {
        if (!_open)
            throw new InvalidOperationException("BeginFrame must be called first.");
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
