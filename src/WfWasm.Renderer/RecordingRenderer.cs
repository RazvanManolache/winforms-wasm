using WfWasm.Shim;

namespace WfWasm.Renderer;

/// <summary>Recorded draw operations (cf. retained command streams).</summary>
public abstract record DrawCommand;

/// <summary>Frame start with canvas size in CSS pixels.</summary>
public sealed record FrameCommand(int Width, int Height) : DrawCommand;

/// <summary>Filled rectangle. Color is 0xRRGGBBAA.</summary>
public sealed record FillRectCommand(float X, float Y, float Width, float Height, uint Rgba) : DrawCommand;

/// <summary>Filled rounded rectangle (cf. day cells with radius 15). Color is 0xRRGGBBAA.</summary>
public sealed record FillRoundedRectCommand(float X, float Y, float Width, float Height, float Radius, uint Rgba) : DrawCommand;

/// <summary>Text run. FontCss is a CSS font shorthand.</summary>
public sealed record DrawTextCommand(
    string Text, float X, float Y, string FontCss, uint Rgba,
    TextHAlign HAlign = TextHAlign.Left, TextVAlign VAlign = TextVAlign.Baseline,
    int MnemonicIndex = -1) : DrawCommand;

/// <summary>Vertical gradient fill (themed headers, buttons, captions).</summary>
public sealed record FillGradientCommand(
    float X, float Y, float Width, float Height, uint TopRgba, uint BottomRgba) : DrawCommand;

/// <summary>Line segment with round caps (paint strokes, shape outlines).</summary>
public sealed record DrawLineCommand(
    float X1, float Y1, float X2, float Y2, float Width, uint Rgba) : DrawCommand;

/// <summary>Ellipse in a bounding box: optional fill plus outline width.</summary>
public sealed record DrawEllipseCommand(
    float X, float Y, float Width, float Height, float LineWidth, uint OutlineRgba,
    uint? FillRgba = null) : DrawCommand;

/// <summary>Raw RGBA bitmap drawn into a destination rect.</summary>
public sealed record DrawImageCommand(
    byte[] Rgba, int Width, int Height, float X, float Y, float DW, float DH) : DrawCommand;

/// <summary>Defines (or replaces) a retained bitmap layer. Pixels travel
/// out-of-band (binary upload); the JSON stream carries only this marker.</summary>
public sealed record DefBitmapCommand(
    string LayerId, byte[] Rgba, int Width, int Height) : DrawCommand;

/// <summary>Composites a retained layer into a destination rect.</summary>
public sealed record BlitBitmapCommand(
    string LayerId, float X, float Y, float DW, float DH) : DrawCommand;

/// <summary>Splices a sub-rect of pixels into a retained layer (region
/// repaint). Pixels travel out-of-band like DefBitmap; the layer must
/// already exist (managed side falls back to Def otherwise).</summary>
public sealed record PatchBitmapCommand(
    string LayerId, int X, int Y, int Width, int Height, byte[] Rgba) : DrawCommand;

/// <summary>End-of-frame live set: the compositor drops unlisted layers.</summary>
public sealed record RetainLayersCommand(
    IReadOnlyList<string> LiveIds) : DrawCommand;

/// <summary>Frame end marker.</summary>
public sealed record EndFrameCommand : DrawCommand
{
    public static readonly EndFrameCommand Instance = new();

    private EndFrameCommand()
    {
    }
}

/// <summary>In-memory backend: records commands for tests and future replay.</summary>
public sealed class RecordingRenderer : ICanvasRenderer
{
    private readonly List<DrawCommand> _commands = new();

    public IReadOnlyList<DrawCommand> Commands => _commands;

    public void BeginFrame(int width, int height) => _commands.Add(new FrameCommand(width, height));

    public void FillRectangle(float x, float y, float width, float height, uint rgba) =>
        _commands.Add(new FillRectCommand(x, y, width, height, rgba));

    public void FillRoundedRectangle(float x, float y, float width, float height, float radius, uint rgba) =>
        _commands.Add(new FillRoundedRectCommand(x, y, width, height, radius, rgba));

    public void FillGradientVertical(float x, float y, float width, float height, uint topRgba, uint bottomRgba) =>
        _commands.Add(new FillGradientCommand(x, y, width, height, topRgba, bottomRgba));

    public void DrawLine(float x1, float y1, float x2, float y2, float width, uint rgba) =>
        _commands.Add(new DrawLineCommand(x1, y1, x2, y2, width, rgba));

    public void DrawEllipse(float x, float y, float width, float height, float lineWidth, uint outlineRgba, uint? fillRgba = null) =>
        _commands.Add(new DrawEllipseCommand(x, y, width, height, lineWidth, outlineRgba, fillRgba));

    public void DrawText(string text, float x, float y, string fontCss, uint rgba) =>
        _commands.Add(new DrawTextCommand(text, x, y, fontCss, rgba));

    public void DrawImage(byte[] rgba, int width, int height, float x, float y, float dw, float dh) =>
        _commands.Add(new DrawImageCommand(rgba, width, height, x, y, dw, dh));

    public bool SupportsBitmapLayers => true;

    public void DefBitmap(string layerId, byte[] rgba, int width, int height) =>
        _commands.Add(new DefBitmapCommand(layerId, rgba, width, height));

    public void BlitBitmap(string layerId, float x, float y, float dw, float dh) =>
        _commands.Add(new BlitBitmapCommand(layerId, x, y, dw, dh));

    public void PatchBitmap(string layerId, int x, int y, int width, int height, byte[] rgba) =>
        _commands.Add(new PatchBitmapCommand(layerId, x, y, width, height, rgba));

    public void RetainLayers(IReadOnlyList<string> liveIds) =>
        _commands.Add(new RetainLayersCommand(liveIds));

    public void DrawTextAligned(
        string text, float x, float y, string fontCss, uint rgba,
        TextHAlign hAlign, TextVAlign vAlign, int mnemonicIndex = -1) =>
        _commands.Add(new DrawTextCommand(text, x, y, fontCss, rgba, hAlign, vAlign, mnemonicIndex));

    public void EndFrame() => _commands.Add(EndFrameCommand.Instance);
}
