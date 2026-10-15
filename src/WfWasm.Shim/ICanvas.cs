namespace WfWasm.Shim;

/// <summary>Horizontal text alignment intent (cf. ContentAlignment / TextFormatFlags).</summary>
public enum TextHAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Vertical text anchor. <see cref="Baseline"/> preserves the original contract
/// (y is the alphabetic baseline); the others measure with real font metrics.
/// </summary>
public enum TextVAlign
{
    Baseline,
    Top,
    Middle,
    Bottom,
}

/// <summary>
/// Minimal canvas subset a shim control can paint to (cf. Control.OnPaint).
/// Lives in the shim layer so controls stay renderer-neutral; the richer
/// <c>WfWasm.Renderer.ICanvasRenderer</c> extends this interface, so any canvas
/// backend (RecordingRenderer, SvgRenderer) can be passed directly.
/// </summary>
public interface ICanvas
{
    void FillRectangle(float x, float y, float width, float height, uint rgba);
    void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba);

    /// <summary>Vertical fill interpolating top color into bottom color.</summary>
    void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba);

    /// <summary>Line segment with round caps.</summary>
    void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba);

    /// <summary>Ellipse in a bounding box: fill plus outline of given width.</summary>
    void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null);

    void DrawText(string text, float x, float y, string fontCss, uint rgba);

    /// <summary>
    /// Raw bitmap: <paramref name="rgba"/> holds width*height*4 bytes in
    /// row-major R,G,B,A order (unpremultiplied), drawn into the destination
    /// rect at (<paramref name="x"/>, <paramref name="y"/>) sized
    /// <paramref name="dw"/> x <paramref name="dh"/>.
    /// </summary>
    void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh);

    /// <summary>
    /// Text with alignment intent and an optional mnemonic index (0-based index
    /// into <paramref name="text"/> of the char drawn underlined, -1 for none).
    /// Backends resolve alignment with real font metrics.
    /// </summary>
    void DrawTextAligned(
        string text, float x, float y, string fontCss, uint rgba,
        TextHAlign hAlign, TextVAlign vAlign, int mnemonicIndex = -1);
}
