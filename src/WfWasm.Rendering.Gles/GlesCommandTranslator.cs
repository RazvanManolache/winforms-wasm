using System.Numerics;

namespace WfWasm.Rendering.Gles;

internal readonly record struct GlesQuad(Vector2 P0, Vector2 P1, Vector2 P2, Vector2 P3);

internal abstract record GlesOp;
internal sealed record GlesSetBlend(bool SourceOver) : GlesOp;
internal sealed record GlesSetScissor(PixelRect? Scissor) : GlesOp;
internal sealed record GlesClear(float R, float G, float B, float A) : GlesOp;
internal sealed record GlesFillQuad(GlesQuad Quad, uint Color) : GlesOp;
internal sealed record GlesBlit(int Texture, GlesQuad Quad, RenderRect Source, ImageSampling Sampling, float Opacity) : GlesOp;

// Translates DrawingBatch commands into driver ops at enqueue time, so
// unsupported semantics fail fast before any version advance or queueing.
// Command transforms are interpreted as logical-to-device-pixel mappings with
// DPI already applied exactly once upstream; the device never rescales by
// DpiScale.
internal static class GlesCommandTranslator
{
    internal static (IReadOnlyList<GlesOp> Ops, IReadOnlyList<ImageHandle> Images) Translate(
        DrawingBatch batch, SurfaceDescriptor target, GlesImageResources images)
    {
        var ops = new List<GlesOp>();
        var used = new List<ImageHandle>();
        var saved = new Stack<(Matrix3x2 Transform, RenderRect? Clip, CompositeOperation Composite)>();
        var transform = Matrix3x2.Identity;
        RenderRect? clip = null;
        var composite = CompositeOperation.SourceOver;
        // The executor starts every batch as (SourceOver, no scissor).
        bool emittedOver = true;
        PixelRect? emittedScissor = null;

        void EmitState()
        {
            bool over = composite == CompositeOperation.SourceOver;
            if (over != emittedOver) { ops.Add(new GlesSetBlend(over)); emittedOver = over; }
            var scissor = ToScissor(clip, target);
            if (scissor != emittedScissor) { ops.Add(new GlesSetScissor(scissor)); emittedScissor = scissor; }
        }

        foreach (var command in batch.Commands)
        {
            switch (command)
            {
                case SaveState:
                    saved.Push((transform, clip, composite));
                    break;
                case RestoreState:
                    if (saved.Count == 0) throw new InvalidOperationException("Unbalanced drawing state in GLES translation.");
                    (transform, clip, composite) = saved.Pop();
                    EmitState();
                    break;
                case SetTransform set:
                    transform = set.Transform;
                    break;
                case SetClip set:
                {
                    var rect = RecognizeRect(set.Path)
                        ?? throw new NotSupportedException("GLES milestone 1 supports SetClip with axis-aligned rectangles only; complex clips need stencil support.");
                    var device = ToDeviceRect(rect, transform)
                        ?? throw new NotSupportedException("GLES milestone 1 supports axis-aligned clips only; this clip is rotated or skewed.");
                    clip = set.Operation switch
                    {
                        ClipOperation.Replace => device,
                        ClipOperation.Intersect => clip is null ? device : Intersect(clip.Value, device),
                        _ => throw new NotSupportedException($"GLES milestone 1 supports clip Replace/Intersect only (got {set.Operation}).")
                    };
                    EmitState();
                    break;
                }
                case ResetClip:
                    clip = null;
                    EmitState();
                    break;
                case SetComposite set:
                    composite = set.Operation;
                    EmitState();
                    break;
                case ClearSurface clear:
                    // Clear ignores clip: drop the scissor, then restore it lazily
                    // for later draws via the unchanged logical clip.
                    if (emittedScissor is not null) { ops.Add(new GlesSetScissor(null)); emittedScissor = null; }
                    uint rgba = clear.Color.Rgba;
                    ops.Add(new GlesClear(
                        ((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f,
                        ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f));
                    break;
                case FillPath fill:
                {
                    if (fill.Brush is not SolidBrushSnapshot solid)
                        throw new NotSupportedException($"GLES milestone 1 fills solid brushes only (got {fill.Brush.GetType().Name}).");
                    var rect = RecognizeRect(fill.Path)
                        ?? throw new NotSupportedException("GLES milestone 1 fills axis-aligned rectangles only; general path triangulation is not implemented.");
                    EmitState();
                    ops.Add(new GlesFillQuad(TransformQuad(rect, transform), solid.Color.Rgba));
                    break;
                }
                case StrokePath:
                    throw new NotSupportedException("GLES milestone 1 does not implement StrokePath.");
                case DrawSurfaceImage draw:
                {
                    int texture = images.ResolveTexture(draw.Image);
                    var source = images.ResolveDescriptor(draw.Image);
                    if (draw.Source.X < 0 || draw.Source.Y < 0 || draw.Source.Width < 0 || draw.Source.Height < 0 ||
                        draw.Source.X + draw.Source.Width > source.PixelWidth ||
                        draw.Source.Y + draw.Source.Height > source.PixelHeight)
                        throw new InvalidOperationException("DrawSurfaceImage source rect is outside the imported image.");
                    EmitState();
                    used.Add(draw.Image);
                    ops.Add(new GlesBlit(texture, TransformQuad(draw.Destination, transform), draw.Source, draw.Sampling, 1f));
                    break;
                }
                case DrawGlyphRun:
                    throw new NotSupportedException("GLES milestone 1 has no text service; DrawGlyphRun is reported as unsupported.");
                default:
                    throw new NotSupportedException($"Unknown drawing command {command.GetType().Name}.");
            }
        }
        return (ops, used);
    }

    // Strict milestone-1 rect pattern: Move, Line, Line, Line, Close with
    // axis-aligned edges (exact equality; the future Graphics adapter emits
    // exact values for FillRectangle). Zero-area rects pass through and draw nothing.
    internal static RenderRect? RecognizeRect(PathSnapshot path)
    {
        var segments = path.Segments;
        if (segments.Count != 5) return null;
        if (segments[0].Verb != PathVerb.Move || segments[1].Verb != PathVerb.Line ||
            segments[2].Verb != PathVerb.Line || segments[3].Verb != PathVerb.Line ||
            segments[4].Verb != PathVerb.Close) return null;
        var p0 = segments[0].A;
        var p1 = segments[1].A;
        var p2 = segments[2].A;
        var p3 = segments[3].A;
        if (!AxisAligned(p0, p1) || !AxisAligned(p1, p2) || !AxisAligned(p2, p3) || !AxisAligned(p3, p0)) return null;
        float minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        float minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        float maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        float maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return new RenderRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool AxisAligned(Vector2 a, Vector2 b) => a.X == b.X || a.Y == b.Y;

    internal static RenderRect? ToDeviceRect(RenderRect rect, Matrix3x2 transform)
    {
        if (transform.M12 != 0 || transform.M21 != 0) return null;
        float x0 = transform.M11 * rect.X + transform.M31;
        float y0 = transform.M22 * rect.Y + transform.M32;
        float x1 = transform.M11 * (rect.X + rect.Width) + transform.M31;
        float y1 = transform.M22 * (rect.Y + rect.Height) + transform.M32;
        return new RenderRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));
    }

    internal static GlesQuad TransformQuad(RenderRect rect, Matrix3x2 transform) => new(
        Vector2.Transform(new Vector2(rect.X, rect.Y), transform),
        Vector2.Transform(new Vector2(rect.X + rect.Width, rect.Y), transform),
        Vector2.Transform(new Vector2(rect.X + rect.Width, rect.Y + rect.Height), transform),
        Vector2.Transform(new Vector2(rect.X, rect.Y + rect.Height), transform));

    private static RenderRect Intersect(RenderRect a, RenderRect b)
    {
        float x = Math.Max(a.X, b.X);
        float y = Math.Max(a.Y, b.Y);
        return new RenderRect(x, y,
            Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x),
            Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }

    // Pixel-coverage rule v0: floor the origin, ceil the far corner, clamp to
    // the target. Tighten with conformance tests before exactness claims.
    internal static PixelRect? ToScissor(RenderRect? clip, SurfaceDescriptor target)
    {
        if (clip is null) return null;
        var value = clip.Value;
        float x = Math.Clamp(MathF.Floor(value.X), 0, target.PixelWidth);
        float y = Math.Clamp(MathF.Floor(value.Y), 0, target.PixelHeight);
        float right = Math.Clamp(MathF.Ceiling(value.X + value.Width), 0, target.PixelWidth);
        float bottom = Math.Clamp(MathF.Ceiling(value.Y + value.Height), 0, target.PixelHeight);
        return new PixelRect((int)x, (int)y, Math.Max(0, (int)right - (int)x), Math.Max(0, (int)bottom - (int)y));
    }
}
