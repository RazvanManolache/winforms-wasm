// Display Graphics adapter: record WinForms paint into Rendering command
// batches instead of CPU-rasterizing. Partial half of Graphics (same
// assembly, full private access). Created via ForDisplay; every supported
// call snapshots state and records the corresponding command, then returns
// WITHOUT touching CPU pixels. Anything outside the milestone-1 vocabulary
// throws NotSupportedException explicitly (never silent fallback, never
// approximation): non-solid fills, strokes except 1px solid rules, affine
// image blits, ImageAttributes/effects, paths/ellipses, regions, text with
// unsupported layout. Text rasterizes its GLYPHS ONLY to a scratch CPU
// bitmap (explicit, measured import) and blits the scratch; the control is
// never rasterized whole and no control bitmap is ever uploaded.
using System.Drawing.Drawing2D;
using WfWasm.Rendering;

namespace System.Drawing
{
    public sealed partial class Graphics
    {
        private DrawingCommandRecorder? _displayRecorder;
        private Func<CpuImageSnapshot, ImageHandle>? _displayImport;
        private CompositeOperation _displayComposite = CompositeOperation.SourceOver;
        private static long s_displayImportSeq;

        // Display-target Graphics: records into the batch recorder; the CPU
        // target stays null (any CPU-pixel observer throws explicitly).
        public static Graphics ForDisplay(
            DrawingCommandRecorder recorder,
            Func<CpuImageSnapshot, ImageHandle> importImage)
        {
            ArgumentNullException.ThrowIfNull(recorder);
            ArgumentNullException.ThrowIfNull(importImage);
            return new Graphics()
            {
                _displayRecorder = recorder,
                _displayImport = importImage,
            };
        }

        internal bool IsDisplay => _displayRecorder is not null;

        private static RenderColor ToRenderColor(Color c) =>
            new RenderColor((uint)((c.R << 24) | (c.G << 16) | (c.B << 8) | c.A));

        private static PathSnapshot RectPathSnapshot(float x, float y, float w, float h) =>
            new PathSnapshot(new[]
            {
                new PathSegment(PathVerb.Move, new System.Numerics.Vector2(x, y)),
                new PathSegment(PathVerb.Line, new System.Numerics.Vector2(x + w, y)),
                new PathSegment(PathVerb.Line, new System.Numerics.Vector2(x + w, y + h)),
                new PathSegment(PathVerb.Line, new System.Numerics.Vector2(x, y + h)),
                new PathSegment(PathVerb.Close),
            }, PathFillRule.Alternate);

        private void DisplayEmitComposite()
        {
            var want = CompositingMode == CompositingMode.SourceCopy
                ? CompositeOperation.SourceCopy
                : CompositeOperation.SourceOver;
            if (want != _displayComposite && _displayRecorder is not null)
            {
                _displayRecorder.Record(new SetComposite(want));
                _displayComposite = want;
            }
        }

        internal void RecordDisplayClear(Color color)
        {
            // Clear ignores clip (desktop parity, same as the CPU path).
            _displayRecorder!.Record(new ClearSurface(ToRenderColor(color)));
        }

        // Display vocabulary is solid-only: anything else is rejected BEFORE
        // any recording (PrepareBrush would flatten gradients/hatches to a
        // solid, hiding the unsupported brush). The CPU path keeps its
        // flat-gradient approximation.
        private static Color DisplayFlatColor(Brush brush)
        {
            if (brush is SolidBrush solid)
                return solid.Color;
            throw new NotSupportedException(
                "Display path records solid fills only (got " + (brush?.GetType().Name ?? "null") + ").");
        }

        internal void RecordDisplayFill(Brush brush, RectangleF rect)
        {
            Color flat = DisplayFlatColor(brush);
            // World space: the device transform (mirrored at every mutation)
            // maps this exactly once. Baking device coords here would apply
            // the transform twice and push translated fills off-surface.
            DisplayEmitComposite();
            _displayRecorder!.Record(new FillPath(
                RectPathSnapshot(rect.X, rect.Y, rect.Width, rect.Height),
                new SolidBrushSnapshot(ToRenderColor(flat))));
        }

        internal void RecordDisplayBlit(Bitmap src, float sx, float sy, float sw, float sh,
            float dx, float dy, float dw, float dh)
        {
            ImageHandle handle = ImportDisplayBitmap(src, "blit");
            DisplayEmitComposite();
            // Callers pre-position dst in device space; invert back to world
            // so the mirrored device transform maps it exactly once.
            DisplayToWorld(dx, dy, out float wx, out float wy);
            DisplayToWorldSize(dw, dh, out float ww, out float wh);
            _displayRecorder!.Record(new DrawSurfaceImage(handle,
                new RenderRect(sx, sy, sw, sh),
                new RenderRect(wx, wy, ww, wh),
                ImageSampling.Nearest));
        }

        internal void RecordDisplayText(byte[] rgba, int rw, int rh,
            int inkA, int inkR, int inkG, int inkB,
            int ox, int oy, int clipX0, int clipY0, int clipX1, int clipY1)
        {
            // Explicit CPU text island: tint the caller-rasterized glyph
            // coverage with the ink color (same math as the CPU blend
            // loop), import the scratch once, and blit it under the layout
            // clip. Measured via device UploadedBytes; the control surface
            // itself is never rasterized or uploaded.
            var scratch = new Bitmap(rw, rh);
            try
            {
                int[] pixels = scratch.Pixels;
                for (int i = 0; i < rw * rh; i++)
                {
                    int a = rgba[i * 4 + 3];
                    int ea = (a * inkA) / 255;
                    pixels[i] = (ea << 24) | (inkR << 16) | (inkG << 8) | inkB;
                }
                scratch.PaintVersion++;
                ImageHandle handle = ImportDisplayBitmap(scratch, "text");
                // Device-baked island origin/clip inverted to world; the
                // mirrored device transform maps them back exactly once.
                DisplayToWorld(ox, oy, out float wx, out float wy);
                // The application clip stays active during text drawing (GDI+
                // parity with the CPU per-pixel test): intersect the layout
                // bounds with it in device space. A NoClip layout still
                // intersects; an empty/absent overlap records one empty-clipped
                // island (balanced save/restore, never silent omission).
                RectangleF islandClip = new(clipX0, clipY0, clipX1 - clipX0, clipY1 - clipY0);
                if (_clip is not null)
                    islandClip = RectangleF.Intersect(islandClip, _clip.GetBounds(null));
                DisplayToWorld(islandClip.X, islandClip.Y, out float cx0, out float cy0);
                DisplayToWorld(islandClip.Right, islandClip.Bottom, out float cx1, out float cy1);
                // (effective island clip converted above; the raw layout bounds
                // no longer address the recorded clip.)
                DisplayToWorldSize(rw, rh, out float ww, out float wh);
                _displayRecorder!.Record(new SaveState());
                _displayRecorder.Record(new SetClip(
                    RectPathSnapshot(cx0, cy0, cx1 - cx0, cy1 - cy0),
                    ClipOperation.Replace));
                DisplayEmitComposite();
                _displayRecorder.Record(new DrawSurfaceImage(handle,
                    new RenderRect(0, 0, rw, rh),
                    new RenderRect(wx, wy, ww, wh),
                    ImageSampling.Nearest));
                _displayRecorder.Record(new RestoreState());
            }
            finally
            {
                scratch.Dispose();
            }
        }

        internal static bool IsDisplayAxisBlit(PointF[] destPoints)
        {
            if (destPoints.Length < 3)
                return false;
            var p0 = destPoints[0];
            var p1 = destPoints[1];
            var p2 = destPoints[2];
            return p0.Y == p1.Y && p0.X == p2.X && p1.X != p0.X && p2.Y != p0.Y;
        }

        internal void RecordDisplayClip(Region region, CombineMode mode)
        {
            if (mode != CombineMode.Replace && mode != CombineMode.Intersect)
                throw new NotSupportedException(
                    "Display path records Replace/Intersect clips only (got " + mode + ").");
            // Rectangular regions map exactly; anything else reports
            // instead of over-clipping (bounds would paint wrong pixels).
            RectangleF[] scans;
            try { scans = region.GetRegionScans(new Matrix()); }
            catch (Exception ex)
            {
                throw new NotSupportedException("Display path cannot read the clip region.", ex);
            }
            if (scans.Length != 1)
                throw new NotSupportedException(
                    "Display path records rectangular clips only (got " + scans.Length + " scans).");
            // Scans come back in device space (clips are stored device-side);
            // invert to world so the mirrored device transform maps once.
            var r = scans[0];
            DisplayToWorld(r.X, r.Y, out float wx, out float wy);
            DisplayToWorldSize(r.Width, r.Height, out float ww, out float wh);
            _displayRecorder!.Record(new SetClip(
                RectPathSnapshot(wx, wy, ww, wh),
                mode == CombineMode.Replace ? ClipOperation.Replace : ClipOperation.Intersect));
        }

        internal ImageHandle ImportDisplayBitmap(Bitmap src, string kind)
        {
            if (_displayImport is null)
                throw new InvalidOperationException("Display Graphics has no image importer.");
            byte[] rgba = src.ToRgbaBytes(new Rectangle(0, 0, src.Width, src.Height));
            long seq = System.Threading.Interlocked.Increment(ref s_displayImportSeq);
            var layout = new PixelLayout(PixelChannels.Rgba8, PixelAlpha.Straight, PixelColorSpace.Srgb);
            var desc = new SurfaceDescriptor(src.Width, src.Height, 1f, layout);
            return _displayImport(new CpuImageSnapshot(
                "gdi-" + kind + "-" + seq, seq, desc, src.Width * 4, rgba));
        }

        internal void RecordDisplaySetClip(RectangleF rect, CombineMode mode)
        {
            ClipOperation op = mode switch
            {
                CombineMode.Replace => ClipOperation.Replace,
                CombineMode.Intersect => ClipOperation.Intersect,
                _ => throw new NotSupportedException(
                    "Display path records Replace/Intersect clips only (got " + mode + ")."),
            };
            // World space (see RecordDisplayFill): the mirrored device
            // transform maps this exactly once at SetClip time.
            _displayRecorder!.Record(new SetClip(
                RectPathSnapshot(rect.X, rect.Y, rect.Width, rect.Height), op));
        }

        // Inverse of the mirrored device transform for axis-aligned state
        // (the only mutations the display path admits). Pre-positioned device
        // payloads (blits, text islands) invert through this so the device
        // transform maps them exactly once. Skew has no mapping: report it.
        private void DisplayToWorld(float x, float y, out float wx, out float wy)
        {
            float[] e = _transform.Elements;
            if (e[1] != 0f || e[2] != 0f)
                throw new NotSupportedException("Display path inverts axis-aligned transforms only.");
            if (e[0] == 0f || e[3] == 0f)
                throw new NotSupportedException("Display path cannot invert a degenerate transform.");
            wx = (x - e[4]) / e[0];
            wy = (y - e[5]) / e[3];
        }

        private void DisplayToWorldSize(float w, float h, out float ww, out float wh)
        {
            float[] e = _transform.Elements;
            if (e[1] != 0f || e[2] != 0f)
                throw new NotSupportedException("Display path inverts axis-aligned transforms only.");
            if (e[0] == 0f || e[3] == 0f)
                throw new NotSupportedException("Display path cannot invert a degenerate transform.");
            ww = w / e[0];
            wh = h / e[3];
        }

        internal void RecordDisplayTransform()
        {
            // Fork transform (translate/scale pages) into device space.
            // Rotation/skew has no milestone-1 mapping: report, don't skew.
            float[] e = _transform.Elements;
            if (e[1] != 0f || e[2] != 0f)
                throw new NotSupportedException("Display path records axis-aligned transforms only.");
            _displayRecorder!.Record(new SetTransform(
                new System.Numerics.Matrix3x2(e[0], 0f, 0f, e[3], e[4], e[5])));
        }
    }
}
