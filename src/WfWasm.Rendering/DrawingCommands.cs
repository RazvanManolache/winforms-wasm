using System.Numerics;

namespace WfWasm.Rendering;

public enum PathVerb { Move, Line, Quadratic, Cubic, Close }
public enum PathFillRule { Alternate, Winding }
public enum ClipOperation { Replace, Intersect, Exclude, Union, Xor, Complement }
public enum CompositeOperation { SourceOver, SourceCopy }
public enum ImageSampling { Nearest, Linear }
public enum GradientSpread { Clamp, Repeat, Reflect }
public enum StrokeCap { Butt, Square, Round }
public enum StrokeJoin { Miter, Bevel, Round }
public readonly record struct PathSegment(PathVerb Verb, Vector2 A = default, Vector2 B = default, Vector2 C = default);

/// <summary>Owned path snapshot; deferred commands cannot reference a mutable GraphicsPath.</summary>
public sealed class PathSnapshot
{
    public IReadOnlyList<PathSegment> Segments { get; }
    public PathFillRule FillRule { get; }
    public PathSnapshot(IEnumerable<PathSegment> segments, PathFillRule fillRule)
    {
        ArgumentNullException.ThrowIfNull(segments);
        Segments = Array.AsReadOnly(segments.ToArray()); FillRule = fillRule;
    }
}
public abstract record BrushSnapshot;
public sealed record SolidBrushSnapshot(RenderColor Color) : BrushSnapshot;
public readonly record struct GradientStop(float Position, RenderColor Color);
public sealed record LinearGradientSnapshot : BrushSnapshot
{
    public Vector2 Start { get; }
    public Vector2 End { get; }
    public IReadOnlyList<GradientStop> Stops { get; }
    public GradientSpread Spread { get; }
    public Matrix3x2 Transform { get; }
    public LinearGradientSnapshot(Vector2 start, Vector2 end, IEnumerable<GradientStop> stops,
        GradientSpread spread, Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var owned = stops.ToArray();
        if (owned.Length < 2 || owned.Any(stop => !float.IsFinite(stop.Position) || stop.Position < 0 || stop.Position > 1) ||
            owned.Zip(owned.Skip(1)).Any(pair => pair.First.Position > pair.Second.Position))
            throw new ArgumentException("Gradient stops must be ordered within [0,1].", nameof(stops));
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y) || !float.IsFinite(end.X) || !float.IsFinite(end.Y))
            throw new ArgumentException("Gradient coordinates must be finite.");
        ResourceGuards.Finite(transform);
        Start = start; End = end; Stops = Array.AsReadOnly(owned); Spread = spread; Transform = transform;
    }
}
public sealed record ImageBrushSnapshot(ImageHandle Image, Matrix3x2 Transform, GradientSpread Spread, ImageSampling Sampling) : BrushSnapshot;
public sealed record StrokeSnapshot(BrushSnapshot Brush, float Width, StrokeCap Cap, StrokeJoin Join, float MiterLimit);

public readonly record struct GlyphPlacement(uint GlyphId, Vector2 Offset, Vector2 Advance, int TextCluster);
/// <summary>Already-shaped text. Measurement and painting must share the shaping service.</summary>
public sealed class GlyphRunSnapshot
{
    public FontHandle Font { get; }
    public float FontSize { get; }
    public IReadOnlyList<GlyphPlacement> Glyphs { get; }
    public GlyphRunSnapshot(FontHandle font, float fontSize, IEnumerable<GlyphPlacement> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        if (!float.IsFinite(fontSize) || fontSize <= 0) throw new ArgumentOutOfRangeException(nameof(fontSize));
        Font = font; FontSize = fontSize; Glyphs = Array.AsReadOnly(glyphs.ToArray());
    }
}

// Ordered state commands: save/restore is a stack, clips are applied in the
// transform active at SetClip, Clear ignores clip, and draws capture brush data.
public abstract record DrawingCommand;
public sealed record SaveState : DrawingCommand;
public sealed record RestoreState : DrawingCommand;
public sealed record SetTransform(Matrix3x2 Transform) : DrawingCommand;
public sealed record SetClip(PathSnapshot Path, ClipOperation Operation) : DrawingCommand;
public sealed record ResetClip : DrawingCommand;
public sealed record SetComposite(CompositeOperation Operation) : DrawingCommand;
public sealed record ClearSurface(RenderColor Color) : DrawingCommand;
public sealed record FillPath(PathSnapshot Path, BrushSnapshot Brush) : DrawingCommand;
public sealed record StrokePath(PathSnapshot Path, StrokeSnapshot Stroke) : DrawingCommand;
public sealed record DrawSurfaceImage(ImageHandle Image, RenderRect Source, RenderRect Destination, ImageSampling Sampling) : DrawingCommand;
public sealed record DrawGlyphRun(GlyphRunSnapshot Run, Vector2 Origin, BrushSnapshot Brush) : DrawingCommand;

/// <summary>Immutable command order. No implicit CPU pixel payloads in ordinary drawing.</summary>
public sealed class DrawingBatch
{
    public SurfaceHandle Target { get; }
    public long ExpectedContentVersion { get; }
    public IReadOnlyList<DrawingCommand> Commands { get; }
    public DrawingBatch(SurfaceHandle target, long expectedContentVersion, IEnumerable<DrawingCommand> commands)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedContentVersion);
        ArgumentNullException.ThrowIfNull(commands);
        var owned = commands.ToArray();
        if (owned.Any(command => command is null)) throw new ArgumentException("Null drawing command.", nameof(commands));
        int depth = 0;
        foreach (var command in owned)
        {
            if (command is SaveState) depth++;
            if (command is RestoreState && --depth < 0) throw new ArgumentException("Unbalanced drawing state.", nameof(commands));
            switch (command)
            {
                case SetTransform transform: ResourceGuards.Finite(transform.Transform); break;
                case FillPath fill: ValidateBrush(fill.Brush, target.Device); break;
                case StrokePath stroke: ValidateBrush(stroke.Stroke.Brush, target.Device); break;
                case DrawSurfaceImage image:
                    ValidateImage(image.Image, target.Device); image.Source.Validate(); image.Destination.Validate(); break;
                case DrawGlyphRun text:
                    ResourceGuards.SameDevice(target.Device, text.Run.Font.Device);
                    if (text.Run.Font.Id <= 0) throw new ArgumentException("Invalid font handle.");
                    ValidateBrush(text.Brush, target.Device); break;
            }
        }
        if (depth != 0) throw new ArgumentException("Drawing batch must restore its saved states.", nameof(commands));
        Target = target; ExpectedContentVersion = expectedContentVersion; Commands = Array.AsReadOnly(owned);
    }

    private static void ValidateImage(ImageHandle image, DeviceIdentity device)
    {
        ResourceGuards.SameDevice(device, image.Device);
        if (image.Id <= 0 || image.Version < 0) throw new ArgumentException("Invalid image handle.");
    }
    private static void ValidateBrush(BrushSnapshot brush, DeviceIdentity device)
    {
        ArgumentNullException.ThrowIfNull(brush);
        if (brush is ImageBrushSnapshot image) { ValidateImage(image.Image, device); ResourceGuards.Finite(image.Transform); }
        // The backend still validates supported brush semantics and resource
        // table membership; equal device IDs do not make an arbitrary ID valid.
    }
}
