extern alias WfDraw;

namespace WfWasm.Shim;

using Draw = WfDraw::System.Drawing;

/// <summary>
/// Global rendering switch (cf. Application.EnableVisualStyles /
/// Application.RenderWithVisualStyles). When true, controls paint the
/// Windows 10 visual-styles look; when false, the classic 3D look.
/// </summary>
public static class RenderingConfig
{
    public static bool UseVisualStyles { get; set; } = true;
}

/// <summary>
/// Mnemonic (&amp;) and alignment helpers shared by button-like controls
/// (cf. ButtonBase mnemonics, Draw.ContentAlignment layout).
/// </summary>
public static class TextRules
{
    /// <summary>
    /// Strip mnemonic markers: single &amp; marks the next char (returned as
    /// the underline index into the stripped text), &amp;&amp; is a literal &amp;.
    /// </summary>
    public static (string Text, int MnemonicIndex) ParseMnemonic(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new System.Text.StringBuilder(text.Length);
        int mnemonic = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '&')
            {
                if (i + 1 < text.Length && text[i + 1] == '&')
                {
                    sb.Append('&');
                    i++;
                }
                else if (mnemonic == -1 && i + 1 < text.Length)
                {
                    mnemonic = sb.Length;
                }
            }
            else
            {
                sb.Append(text[i]);
            }
        }

        return (sb.ToString(), mnemonic);
    }

    /// <summary>Split content alignment into canvas align + anchor fractions.</summary>
    public static (TextHAlign HAlign, TextVAlign VAlign) ToCanvasAlign(Draw.ContentAlignment alignment) =>
        (alignment switch
        {
            Draw.ContentAlignment.TopLeft or Draw.ContentAlignment.MiddleLeft or Draw.ContentAlignment.BottomLeft
                => TextHAlign.Left,
            Draw.ContentAlignment.TopCenter or Draw.ContentAlignment.MiddleCenter or Draw.ContentAlignment.BottomCenter
                => TextHAlign.Center,
            _ => TextHAlign.Right,
        },
        alignment switch
        {
            Draw.ContentAlignment.TopLeft or Draw.ContentAlignment.TopCenter or Draw.ContentAlignment.TopRight
                => TextVAlign.Top,
            Draw.ContentAlignment.MiddleLeft or Draw.ContentAlignment.MiddleCenter or Draw.ContentAlignment.MiddleRight
                => TextVAlign.Middle,
            _ => TextVAlign.Bottom,
        });

    /// <summary>
    /// Anchor point for an aligned text block inside (x, y, width, height).
    /// Horizontal anchors are exact; vertical Top/Middle/Bottom anchors are
    /// resolved with real font metrics by the backend.
    /// </summary>
    public static (float Ax, float Ay) Anchor(
        Draw.ContentAlignment alignment, float x, float y, float width, float height) =>
        (alignment switch
        {
            Draw.ContentAlignment.TopLeft or Draw.ContentAlignment.MiddleLeft or Draw.ContentAlignment.BottomLeft
                => x,
            Draw.ContentAlignment.TopCenter or Draw.ContentAlignment.MiddleCenter or Draw.ContentAlignment.BottomCenter
                => x + (width / 2f),
            _ => x + width,
        },
        alignment switch
        {
            Draw.ContentAlignment.TopLeft or Draw.ContentAlignment.TopCenter or Draw.ContentAlignment.TopRight
                => y,
            Draw.ContentAlignment.MiddleLeft or Draw.ContentAlignment.MiddleCenter or Draw.ContentAlignment.MiddleRight
                => y + (height / 2f),
            _ => y + height,
        });

    /// <summary>Draw one rule-following text run: mnemonic, alignment, disabled gray.</summary>
    public static void DrawAlignedText(
        ICanvas canvas, string text, bool useMnemonic,
        Draw.ContentAlignment alignment, float x, float y, float width, float height,
        string fontCss, uint ink, bool disabled)
    {
        (string stripped, int mnemonic) = useMnemonic ? ParseMnemonic(text) : (text, -1);
        if (string.IsNullOrEmpty(stripped))
            return;
        (TextHAlign hAlign, TextVAlign vAlign) = ToCanvasAlign(alignment);
        // Multiline: paint line by line; first-line anchor only (no wrap).
        string[] lines = stripped.Split('\n');
        (float ax, float ay) = Anchor(alignment, x, y, width, height);
        float lineStep = EstimateLineHeight(fontCss);
        bool singleLine = lines.Length == 1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i]))
                continue;
            float ly = i == 0 ? ay : y + (i * lineStep);
            TextVAlign lineV = i == 0 ? vAlign : TextVAlign.Top;
            int lineMnemonic = singleLine ? mnemonic : -1;
            if (disabled)
            {
                canvas.DrawTextAligned(lines[i], ax + 1, ly + 1,
                    fontCss, SystemColors.White, hAlign, lineV);
                canvas.DrawTextAligned(lines[i], ax, ly, fontCss,
                    SystemColors.GrayText, hAlign, lineV);
            }
            else
            {
                canvas.DrawTextAligned(lines[i], ax, ly, fontCss, ink, hAlign, lineV, lineMnemonic);
            }
        }
    }

    /// <summary>Rough line height from "NNpx ..." font CSS (metrics come later).</summary>
    public static float EstimateLineHeight(string fontCss)
    {
        foreach (string part in fontCss.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                && float.TryParse(part[..^2], out float px))
                return px * 1.2f;
        }

        return 16;
    }
}
