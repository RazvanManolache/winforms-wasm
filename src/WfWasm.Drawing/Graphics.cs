// CPU rasterizer: exact .NET member shapes; draws into a bound Bitmap's
// ARGB buffer with SourceOver blending, page-unit scaling, and rect-list
// clipping. Antialiasing, dashing, and text use stored settings until their
// milestones; quality flags are honored by storage, not yet by pixels.
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace System.Drawing
{
    /// <summary>2D rasterizer (cf. System.Drawing.Graphics).</summary>
    public sealed partial class Graphics : IDeviceContext, IDisposable
    {
        private Bitmap? _target;
        private Matrix _transform = new();
        private IntPtr _overlayHwnd;
        private bool _overlayCommitted;




        private Region? _clip;
        private readonly Stack<StateFrame> _stack = new();

        internal Graphics(Bitmap target)
        {
            _target = target;
        }

        internal Graphics()
        {
        }

        public static Graphics FromImage(Image image)
        {
            if (image is Bitmap bitmap)
                return new Graphics(bitmap);
            throw new ArgumentException("Image must be a Bitmap.", nameof(image));
        }

        [Obsolete("Use FromHwnd with HandleRef in new code.")]
        public static Graphics FromHdc(IntPtr hdc) => new();

        public static Graphics FromHdc(IntPtr hdc, IntPtr hdevice) => new();

        // Fork hook: FromHwnd paints onto a per-window overlay bitmap
        // the host composites after the control (spell squiggles,
        // adorner layers). Wired once by the fork module initializer;
        // without the fork, or for unknown windows, the Graphics stays
        // detached as before.
        internal static Func<IntPtr, Size>? HwndOverlaySizeHook { get; set; }
        internal static Action<IntPtr, Bitmap>? HwndOverlayCommitHook { get; set; }

        [Obsolete("Use FromHwnd with HandleRef in new code.")]
        public static Graphics FromHwnd(IntPtr hwnd) => FromHwndCore(hwnd);

        public static Graphics FromHwndInternal(IntPtr hwnd) => FromHwndCore(hwnd);

        private static Graphics FromHwndCore(IntPtr hwnd)
        {
            try
            {
                Size size = HwndOverlaySizeHook?.Invoke(hwnd) ?? Size.Empty;
                if (size.Width > 0 && size.Height > 0)
                {
                    var overlay = new Graphics(new Bitmap(size.Width, size.Height));
                    overlay._overlayHwnd = hwnd;
                    return overlay;
                }
            }
            catch
            {
            }

            return new Graphics();
        }

        // Fork hook: headless HDCs are tokens into the fork's Graphics
        // registry (WasmFillBridge), minted on GetHdc so GDI paint calls
        // (DrawText, ImageList_Draw) resolve the target. Wired once by the
        // fork module initializer; without the fork, GetHdc answers Zero
        // as before.
        internal static Func<Graphics, IntPtr>? HdcHook { get; set; }
        internal static Action<IntPtr>? HdcReleaseHook { get; set; }

        public IntPtr GetHdc() => HdcHook?.Invoke(this) ?? IntPtr.Zero;
        public void ReleaseHdc()
        {
        }

        public void ReleaseHdc(IntPtr hdc)
        {
            try { HdcReleaseHook?.Invoke(hdc); } catch { }
        }

        public void Flush()
        {
        }

        public void Flush(FlushIntention intention)
        {
        }

        ~Graphics()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (disposing && _overlayHwnd != IntPtr.Zero && !_overlayCommitted && _target is Bitmap bmp)
            {
                _overlayCommitted = true;
                try { HwndOverlayCommitHook?.Invoke(_overlayHwnd, bmp); } catch { }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public float DpiX => _target?.HorizontalResolution ?? 96f;
        public float DpiY => _target?.VerticalResolution ?? 96f;
        public bool IsClipEmpty => ClipBounds.IsEmpty;
        public bool IsVisibleClipEmpty => VisibleClipBounds.IsEmpty;

        public CompositingMode CompositingMode { get; set; } = CompositingMode.SourceOver;
        public CompositingQuality CompositingQuality { get; set; } = CompositingQuality.Default;
        public InterpolationMode InterpolationMode { get; set; } = InterpolationMode.Bilinear;
        public SmoothingMode SmoothingMode { get; set; } = SmoothingMode.None;
        public PixelOffsetMode PixelOffsetMode { get; set; } = PixelOffsetMode.Default;
        public TextRenderingHint TextRenderingHint { get; set; } = TextRenderingHint.SystemDefault;
        public int TextContrast { get; set; } = 4;
        public GraphicsUnit PageUnit { get; set; } = GraphicsUnit.Display;
        public float PageScale { get; set; } = 1f;
        public Point RenderingOrigin { get; set; } = new(0, 0);

        public Matrix Transform
        {
            get => _transform.Clone();
            set
            {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");

                _transform = value?.Clone() ?? new Matrix();
            }
        }

        // Clip space contract (GDI+): _clip is stored in DEVICE space
        // (drawing tests device pixels against it in PutPixel/HitTest),
        // while the public surface speaks WORLD space: setters transform
        // world->device, getters transform device->world. Storing the
        // world rect raw collapsed every translated SetClip (Telerik
        // SetClipping passes local Bounds under an absolute paint
        // transform), which emptied nested clips and hid grid cells.
        public Region Clip
        {
            get
            {
                Region raw = _clip is null ? new Region(Bounds()) : (Region)_clip.Clone();
                raw.Transform(WorldMatrix());
                return raw;
            }
            set
            {
                if (value is null)
                {
                    _clip = null;
                    if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.ResetClip());
                    return;
                }

                Region device = (Region)value.Clone();
                device.Transform(DeviceMatrix());
                if (IsDisplay) RecordDisplayClip(device, CombineMode.Replace);
                _clip = device;
            }
        }

        public RectangleF ClipBounds
        {
            get
            {
                RectangleF bounds = Bounds();
                if (_clip is null)
                    return ToWorldRect(bounds);
                RectangleF hit = RectangleF.Intersect(bounds, _clip.GetBounds(null));
                return ToWorldRect(hit);
            }
        }

        public RectangleF VisibleClipBounds => ClipBounds;

        public GraphicsState Save()
        {
            _stack.Push(new StateFrame(_transform.Clone(), _clip is null ? null : (Region)_clip.Clone()));
            if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.SaveState());
            return new GraphicsState();
        }

        public void Restore(GraphicsState gstate)
        {
            if (gstate is null || _stack.Count == 0)
                return;
            StateFrame frame = _stack.Pop();
            _transform = frame.Transform;
            _clip = frame.Clip;
            if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.RestoreState());
        }

        public GraphicsContainer BeginContainer() => BeginContainer(new Rectangle(), new Rectangle(), GraphicsUnit.Pixel);

        public GraphicsContainer BeginContainer(Rectangle dstrect, Rectangle srcrect, GraphicsUnit unit)
        {
            _stack.Push(new StateFrame(_transform.Clone(), _clip is null ? null : (Region)_clip.Clone()));
            if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.SaveState());
            return new GraphicsContainer();
        }

        public GraphicsContainer BeginContainer(RectangleF dstrect, RectangleF srcrect, GraphicsUnit unit)
        {
            _stack.Push(new StateFrame(_transform.Clone(), _clip is null ? null : (Region)_clip.Clone()));
            if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.SaveState());
            return new GraphicsContainer();
        }

        public void EndContainer(GraphicsContainer container)
        {
            if (container is null || _stack.Count == 0)
                return;
            StateFrame frame = _stack.Pop();
            _transform = frame.Transform;
            _clip = frame.Clip;
            if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.RestoreState());
        }

        public void ResetTransform()
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");
            _transform.Reset();
        }

        public void MultiplyTransform(Matrix matrix) =>
            MultiplyTransform(matrix, MatrixOrder.Prepend);

        public void MultiplyTransform(Matrix matrix, MatrixOrder order)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");
            _transform.Multiply(matrix, order);
        }

        public void TranslateTransform(float dx, float dy) =>
            TranslateTransform(dx, dy, MatrixOrder.Prepend);

        public void TranslateTransform(float dx, float dy, MatrixOrder order)
        {

            TranslateTransformCore(dx, dy, order);
        }

        private void TranslateTransformCore(float dx, float dy, MatrixOrder order)
        {
            _transform.Translate(dx, dy, order);
            if (IsDisplay) RecordDisplayTransform();
        }

        public void ScaleTransform(float sx, float sy) =>
            ScaleTransform(sx, sy, MatrixOrder.Prepend);

        public void ScaleTransform(float sx, float sy, MatrixOrder order)
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");
            _transform.Scale(sx, sy, order);
        }

        public void RotateTransform(float angle) =>
            RotateTransform(angle, MatrixOrder.Prepend);

        public void RotateTransform(float angle, MatrixOrder order)
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");
            _transform.Rotate(angle, order);
        }

        public void TransformPoints(PointF[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            var matrix = DeviceMatrix();
            matrix.TransformPoints(pts);
        }

        public void TransformPoints(Point[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            var matrix = DeviceMatrix();
            matrix.TransformPoints(pts);
        }

        public void TransformVectors(PointF[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            var matrix = DeviceMatrix();
            matrix.TransformVectors(pts);
        }

        public void TransformVectors(Point[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            var matrix = DeviceMatrix();
            matrix.TransformVectors(pts);
        }

        public void SetClip(Graphics g) => SetClip(g, CombineMode.Replace);

        public void SetClip(Graphics g, CombineMode combineMode)
        {
            ArgumentNullException.ThrowIfNull(g);
            CombineClipDevice(g._clip is null ? new Region(Bounds()) : (Region)g._clip.Clone(), combineMode);
        }

        public void SetClip(Rectangle rect) => SetClip(rect, CombineMode.Replace);
        public void SetClip(Rectangle rect, CombineMode combineMode) =>
            CombineClip(new Region(rect), combineMode);

        public void SetClip(RectangleF rect) => SetClip(rect, CombineMode.Replace);
        public void SetClip(RectangleF rect, CombineMode combineMode) =>
            CombineClip(new Region(rect), combineMode);

        public void SetClip(Region region, CombineMode combineMode)
        {
            ArgumentNullException.ThrowIfNull(region);
            CombineClip((Region)region.Clone(), combineMode);
        }

        public void IntersectClip(Rectangle rect) => SetClip(rect, CombineMode.Intersect);
        public void IntersectClip(RectangleF rect) => SetClip(rect, CombineMode.Intersect);
        public void IntersectClip(Region region) => SetClip(region, CombineMode.Intersect);

        public void ExcludeClip(Rectangle rect) => CombineClip(new Region(rect), CombineMode.Exclude);



        public void ExcludeClip(RectangleF rect) => CombineClip(new Region(rect), CombineMode.Exclude);

        public void ExcludeClip(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);
            CombineClip((Region)region.Clone(), CombineMode.Exclude);
        }

        public void ResetClip()
        {
            _clip = null;
            if (IsDisplay) _displayRecorder!.Record(new WfWasm.Rendering.ResetClip());
        }

        public bool IsVisible(int x, int y) => IsVisible(new Point(x, y));
        public bool IsVisible(Point point) => IsVisible(point.X, point.Y, null);
        public bool IsVisible(float x, float y) => IsVisible(new PointF(x, y));
        public bool IsVisible(PointF point) => IsVisible(point, null);
        public bool IsVisible(int x, int y, int width, int height) => IntersectsDevice(new RectangleF(x, y, width, height));
        public bool IsVisible(Rectangle rect) => IntersectsDevice(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));
        public bool IsVisible(RectangleF rect) => IntersectsDevice(rect);
        public bool IsVisible(int x, int y, Pen? pen) => IsVisible(new PointF(x, y), pen);
        public bool IsVisible(Point point, Pen? pen) => IsVisible(new PointF(point.X, point.Y), pen);
        public bool IsVisible(float x, float y, Pen? pen) => IsVisible(new PointF(x, y), pen);
        public bool IsVisible(PointF point, Pen? pen) => HitTest(point);

        private bool HitTest(PointF point)
        {
            DevicePoint(point, out float dx, out float dy);
            if (_target is null)
                return false;
            if (dx < 0 || dy < 0 || dx >= _target.Width || dy >= _target.Height)
                return false;
            return _clip is null || _clip.IsVisible(dx, dy);
        }

        private bool IntersectsDevice(RectangleF rect)
        {
            if (_target is null)
                return false;
            ToDevice(rect, out float x, out float y, out float w, out float h);
            return _clip is null
                ? new RectangleF(0, 0, _target.Width, _target.Height).IntersectsWith(new RectangleF(x, y, w, h))
                : _clip.IsVisible(new RectangleF(x, y, w, h));
        }

        private void CombineClip(Region region, CombineMode mode)
        {
            // GDI+: the incoming region is expressed in world units and
            // mapped through the current transform; storage is device.
            region.Transform(DeviceMatrix());
            CombineClipDevice(region, mode);
        }

        private void CombineClipDevice(Region region, CombineMode mode)
        {
            // Display records first (it may honestly reject unrepresentable
            // regions/modes with state untouched), then shares the stored
            // device-space clip so getters and text islands observe it.
            if (IsDisplay) RecordDisplayClip(region, mode);
            switch (mode)
            {
                case CombineMode.Intersect:
                    if (_clip is null)
                        _clip = region;
                    else
                        _clip.Intersect(region);
                    break;
                case CombineMode.Union:
                    if (_clip is null)
                        _clip = region;
                    else
                        _clip.Union(region);
                    break;
                case CombineMode.Xor:
                    if (_clip is null)
                        _clip = region;
                    else
                        _clip.Xor(region);
                    break;
                case CombineMode.Exclude:
                    if (_clip is null)
                        _clip = new Region(Bounds());
                    _clip.Exclude(region);
                    break;
                case CombineMode.Complement:
                    if (_clip is null)
                        _clip = new Region(Bounds());
                    _clip.Complement(region);
                    break;
                default:
                    _clip = region;
                    break;
            }
        }

        private sealed class StateFrame
        {
            public StateFrame(Matrix transform, Region? clip)
            {
                Transform = transform;
                Clip = clip;
            }

            public Matrix Transform { get; }
            public Region? Clip { get; }
        }

        private RectangleF Bounds() =>
            _target is null
                ? IsDisplay && _clip is not null
                    // No CPU target by design; the stored clip is the only
                    // known-visible display extent (never fabricate one).
                    ? _clip.GetBounds(null)
                    : new RectangleF(0, 0, 0, 0)
                : new RectangleF(0, 0, _target.Width, _target.Height);

        private float UnitScale() => PageScale * PageUnit switch
        {
            GraphicsUnit.Pixel or GraphicsUnit.Display or GraphicsUnit.World => 1f,
            GraphicsUnit.Point => DpiX / 72f,
            GraphicsUnit.Inch => DpiX,
            GraphicsUnit.Document => DpiX / 300f,
            GraphicsUnit.Millimeter => DpiX / 25.4f,
            _ => 1f,
        };

        private Matrix DeviceMatrix()
        {
            var device = new Matrix(UnitScale(), 0f, 0f, UnitScale(), RenderingOrigin.X, RenderingOrigin.Y);
            device.Multiply(_transform, MatrixOrder.Append);
            return device;
        }

        private Matrix WorldMatrix()
        {
            Matrix world = DeviceMatrix();
            if (world.IsInvertible)
                world.Invert();
            else
                world.Reset();
            return world;
        }

        private RectangleF ToWorldRect(RectangleF device)
        {
            Matrix world = WorldMatrix();
            var corners = new PointF[]
            {
                new(device.X, device.Y),
                new(device.Right, device.Y),
                new(device.Right, device.Bottom),
                new(device.X, device.Bottom),
            };
            world.TransformPoints(corners);
            float minX = corners[0].X;
            float minY = corners[0].Y;
            float maxX = corners[0].X;
            float maxY = corners[0].Y;
            for (int i = 1; i < corners.Length; i++)
            {
                if (corners[i].X < minX) minX = corners[i].X;
                if (corners[i].Y < minY) minY = corners[i].Y;
                if (corners[i].X > maxX) maxX = corners[i].X;
                if (corners[i].Y > maxY) maxY = corners[i].Y;
            }

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        private void DevicePoint(PointF point, out float dx, out float dy)
        {
            Matrix device = DeviceMatrix();
            device.TransformPoint(point.X, point.Y, out dx, out dy);
        }

        private void ToDevice(RectangleF rect, out float x, out float y, out float w, out float h)
        {
            // Fast path: pixel units at scale 1 with a pure-translation
            // world transform (the TPF span pattern is translate-there/
            // back around DrawString). Bit-identical: the general path
            // computes ((p + t) * s + o) with s == 1, o == 0 and a
            // translation-only matrix, which reduces exactly to p + t
            // (same float ops; int casts absorb -0); anything else takes
            // the slow path below.
            if (RenderingOrigin.X == 0 && RenderingOrigin.Y == 0
                && PageScale == 1f
                && (PageUnit == GraphicsUnit.Pixel || PageUnit == GraphicsUnit.Display || PageUnit == GraphicsUnit.World)
                && _transform.IsPureTranslation(out float ttx, out float tty))
            {
                x = rect.X + ttx;
                y = rect.Y + tty;
                w = Math.Abs(rect.Width);
                h = Math.Abs(rect.Height);
                if (rect.Width < 0)
                    x += rect.Width;
                if (rect.Height < 0)
                    y += rect.Height;
                return;
            }
            DevicePoint(new PointF(rect.X, rect.Y), out x, out y);
            Matrix device = DeviceMatrix();
            device.TransformVector(rect.Width, rect.Height, out float vw, out float vh);
            w = Math.Abs(vw);
            h = Math.Abs(vh);
            if (vw < 0)
                x += vw;
            if (vh < 0)
                y += vh;
        }

        private void PutPixel(int x, int y, Color color)
        {
            if (_target is null)
                return;
            if ((uint)x >= (uint)_target.Width || (uint)y >= (uint)_target.Height)
                return;
            if (_clip is not null && !_clip.IsVisible(x, y))
                return;
            _target.PaintVersion++;
            int[] pixels = _target.Pixels;
            int index = (y * _target.Width) + x;
            pixels[index] = CompositingMode == CompositingMode.SourceCopy || color.A >= 255
                ? color.ToArgb()
                : BlendOver(pixels[index], color);
        }

        private static int BlendOver(int dest, Color src)
        {
            int sa = src.A;
            int inv = 255 - sa;
            int dr = (dest >> 16) & 0xFF;
            int dg = (dest >> 8) & 0xFF;
            int db = dest & 0xFF;
            int da = (int)((uint)dest >> 24);
            int r = ((src.R * sa) + (dr * inv)) / 255;
            int g = ((src.G * sa) + (dg * inv)) / 255;
            int b = ((src.B * sa) + (db * inv)) / 255;
            int a = sa + ((da * inv) / 255);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        // Integer-component twin of BlendOver (the DrawString hoisted
        // loop composes src ARGB directly instead of round-tripping a
        // Color through FromArgb's CheckBytes; identical math).
        private static int BlendOverInt(int dest, int sa, int sr, int sg, int sb)
        {
            int inv = 255 - sa;
            int dr = (dest >> 16) & 0xFF;
            int dg = (dest >> 8) & 0xFF;
            int db = dest & 0xFF;
            int da = (int)((uint)dest >> 24);
            int r = ((sr * sa) + (dr * inv)) / 255;
            int g = ((sg * sa) + (dg * inv)) / 255;
            int b = ((sb * sa) + (db * inv)) / 255;
            int a = sa + ((da * inv) / 255);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        // Drawing frame: the integer device-pixel rectangle in which a
        // PutPixel can possibly land for the current target/clip. Finite
        // clips contribute their bounds envelope (a superset: disjoint
        // rects and holes still go through PutPixel's exact IsVisible);
        // null/infinite clips map to the whole target; an empty clip (or
        // an envelope disjoint from the target, or no target) yields no
        // frame and callers skip all scan/color work. The bounds of an
        // infinite/empty region are never read: IsInfinite/IsEmpty are
        // the tokens, checked first, and no rect is handed to Region.
        private bool TryGetDrawFrame(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = 0;
            y0 = 0;
            x1 = 0;
            y1 = 0;
            if (_target is null)
                return false;
            x1 = _target.Width;
            y1 = _target.Height;
            if (x1 <= 0 || y1 <= 0)
                return false;
            Region? clip = _clip;
            if (clip is null || clip.IsInfinite(null))
                return true;
            if (clip.IsEmpty(null))
                return false;
            RectangleF bounds = clip.GetBounds(null);
            double left = bounds.X;
            double top = bounds.Y;
            double right = (double)bounds.X + bounds.Width;
            double bottom = (double)bounds.Y + bounds.Height;
            if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(right) || double.IsNaN(bottom))
                return true; // unusable envelope: keep the target frame; PutPixel stays exact

            // Integer pixels visible through a rect satisfy X <= px < Right
            // (RectangleF.Contains), so the candidate integer range is
            // [ceil(left), ceil(right)) intersected with the target.
            x0 = (int)Math.Clamp(Math.Ceiling(left), 0, x1);
            y0 = (int)Math.Clamp(Math.Ceiling(top), 0, y1);
            x1 = (int)Math.Clamp(Math.Ceiling(right), 0, x1);
            y1 = (int)Math.Clamp(Math.Ceiling(bottom), 0, y1);
            return x0 < x1 && y0 < y1;
        }

        // Integer candidate range [lo, hi) for the legacy loops'
        // floor(loF) <= p < hiF span, intersected with the frame range
        // [f0, f1). Double math keeps huge/infinite float spans from
        // overflowing the int cast the old loops used; NaN spans (which
        // the old float comparisons also rejected) yield no range.
        private static bool TryBoundRange(float loF, float hiF, int f0, int f1, out int lo, out int hi)
        {
            lo = f0;
            hi = f0;
            if (float.IsNaN(loF) || float.IsNaN(hiF))
                return false;
            lo = (int)Math.Clamp(Math.Floor((double)loF), f0, f1);
            hi = (int)Math.Clamp(Math.Ceiling((double)hiF), f0, f1);
            return lo < hi;
        }

        // Per-draw brush state, resolved once at each fill's call
        // boundary (gradient stops arrive here as a cloned snapshot via
        // LerpColor/EffectiveStops, so mutating the brush between draws
        // is honored and nothing is cached across calls by identity).
        // Texture brushes stay spatial: the tile is sampled per pixel
        // exactly like the old ResolveColor did.
        private readonly struct PreparedBrush
        {
            private readonly Color _color;
            private readonly Bitmap? _tile;

            internal PreparedBrush(Color color, Bitmap? tile)
            {
                _color = color;
                _tile = tile;
            }

            internal Bitmap? Tile => _tile;

            internal bool TryGetFlatColor(out Color color)
            {
                color = _color;
                return _tile is null;
            }

            internal Color Sample(float x, float y) =>
                _tile is not null
                    ? _tile.GetPixel(Mod((int)x, _tile.Width), Mod((int)y, _tile.Height))
                    : _color;

            private static int Mod(int value, int length) => ((value % length) + length) % length;
        }

        private static PreparedBrush PrepareBrush(Brush brush)
        {
            switch (brush)
            {
                case SolidBrush solid:
                    return new PreparedBrush(solid.Color, null);
                case HatchBrush hatch:
                    return new PreparedBrush(hatch.ForegroundColor, null);
                case LinearGradientBrush linear:
                    return new PreparedBrush(LerpColor(linear), null);
                case TextureBrush texture when texture.Image is Bitmap tile && tile.Width > 0 && tile.Height > 0:
                    return new PreparedBrush(Color.Empty, tile);
                case Drawing2D.PathGradientBrush radial:
                    // Radial interpolation waits for the gradient milestone.
                    return new PreparedBrush(radial.CenterColor, null);
                default:
                    return new PreparedBrush(Color.Gray, null);
            }
        }

        private Color ResolveColor(Brush brush, float x, float y) =>
            PrepareBrush(brush).Sample(x, y);

        private static Color LerpColor(LinearGradientBrush linear)
        {
        // Effective stops: InterpolationColors when set (GDI+
        // semantics), else LinearColors. The flat resolve approximates
        // the gradient integral (trapezoid over stop positions) in all
        // 4 channels: zero-alpha stops stay transparent, never the
        // opaque black that ARGB(0,0,0,0) theme sentinels produced.
        Color[] colors = EffectiveStops(linear, out float[] positions);
        if (colors.Length == 0)
        return Color.Empty;
        if (colors.Length == 1)
        return colors[0];
        double a = 0, r = 0, g = 0, b = 0, wsum = 0;
        for (int i = 0; i + 1 < colors.Length; i++)
        {
        double w = Math.Max(0f, positions[i + 1] - positions[i]);
        if (w <= 0)
        continue;
        a += w * (colors[i].A + colors[i + 1].A) * 0.5;
        r += w * (colors[i].R + colors[i + 1].R) * 0.5;
        g += w * (colors[i].G + colors[i + 1].G) * 0.5;
        b += w * (colors[i].B + colors[i + 1].B) * 0.5;
        wsum += w;
        }
        if (wsum <= 0)
        {
        foreach (Color c in colors)
        {
        a += c.A;
        r += c.R;
        g += c.G;
        b += c.B;
        }
        return Color.FromArgb(ToByte(a / colors.Length), ToByte(r / colors.Length), ToByte(g / colors.Length), ToByte(b / colors.Length));
        }
        return Color.FromArgb(ToByte(a / wsum), ToByte(r / wsum), ToByte(g / wsum), ToByte(b / wsum));
        }
        private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 255));
        // Snapshot rule: the returned arrays are private clones taken at
        // the calling draw's boundary (UniformPositions already returns a
        // fresh array), never the brush's live arrays, so a prepared
        // gradient cannot observe a later mutation mid-draw and no
        // cross-call cache is keyed by brush identity.
        private static Color[] EffectiveStops(LinearGradientBrush linear, out float[] positions)
        {
            ColorBlend blend = linear.InterpolationColors;
            if (blend?.Colors is { Length: > 0 } blendColors)
            {
                positions = blend.Positions is { Length: > 0 } p && p.Length == blendColors.Length
                    ? (float[])p.Clone()
                    : UniformPositions(blendColors.Length);
                return (Color[])blendColors.Clone();
            }

            Color[] colors = linear.LinearColors ?? Array.Empty<Color>();
            positions = UniformPositions(colors.Length);
            return colors.Length == 0 ? colors : (Color[])colors.Clone();
        }

        private static float[] UniformPositions(int count)
        {
        if (count <= 1)
        return [0f];
        var positions = new float[count];
        for (int i = 0; i < count; i++)
        positions[i] = i / (float)(count - 1);
        return positions;
        }

        public void Clear(Color color)
        {
            if (IsDisplay) { RecordDisplayClear(color); return; }
            if (_target is null)
                return;
            _target.PaintVersion++;
            _target.ClearVersion++;
            int[] pixels = _target.Pixels;
            int argb = color.ToArgb();
            // Perf: whole-bitmap memset (same values, same single version
            // bumps as the old loop) plus the clear color for redundancy
            // analysis (layer bg vs per-block bg fills).
            System.Array.Fill(pixels, argb);
            TextRasterCounters.NoteClearSurface(_target, _target.PaintVersion, argb);
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteClear(argb);
        }

        public void FillRectangle(Brush brush, RectangleF rect) =>
            FillRectCore(brush, rect);

        public void FillRectangle(Brush brush, Rectangle rect) =>
            FillRectCore(brush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void FillRectangle(Brush brush, float x, float y, float width, float height) =>
            FillRectCore(brush, new RectangleF(x, y, width, height));

        public void FillRectangle(Brush brush, int x, int y, int width, int height) =>
            FillRectCore(brush, new RectangleF(x, y, width, height));

        public void FillRectangles(Brush brush, RectangleF[] rects)
        {
            ArgumentNullException.ThrowIfNull(rects);
            foreach (RectangleF rect in rects)
                FillRectCore(brush, rect);
        }

        public void FillRectangles(Brush brush, Rectangle[] rects)
        {
            ArgumentNullException.ThrowIfNull(rects);
            foreach (Rectangle rect in rects)
                FillRectCore(brush, rect);
        }

        private void FillRectCore(Brush brush, RectangleF rect)
        {
            ArgumentNullException.ThrowIfNull(brush);
            if (IsDisplay) { RecordDisplayFill(brush, rect); return; }
            if (!TryGetDrawFrame(out int fx0, out int fy0, out int fx1, out int fy1))
                return;
            ToDevice(rect, out float x, out float y, out float w, out float h);
            if (!TryBoundRange(x, x + w, fx0, fx1, out int x0, out int x1) ||
                !TryBoundRange(y, y + h, fy0, fy1, out int y0, out int y1))
                return;
            // Empty device clip: the per-pixel loop below would test every
            // pixel and write none (same for the version bump). Return
            // before brush resolution.
            if (_clip is not null && _clip.IsEmpty(null))
                return;
            // Perf item 4 op census.
            if (TextRasterCounters.PerfEnabled)
            {
                TextRasterCounters.NoteFill();
                long censusPx = (long)(x1 - x0) * (y1 - y0);
                TextRasterCounters.NoteFillPx(censusPx);
            }
            PreparedBrush prepared = PrepareBrush(brush);
            if (prepared.Tile is Bitmap tile)
            {
                FillTextureFast(tile, x0, y0, x1, y1);
                return;
            }
            // Redundant-fill skip (per-surface, version-guarded): a flat
            // fill whose ARGB exactly matches the surface's own clear color,
            // with no intervening write to THAT surface, rewrites identical
            // pixels when opaque (any mode) or under SourceCopy (replace
            // with self), so it returns with the version exact. Translucent
            // SourceOver is NOT identity (128-over-128 blends to 191) and
            // must paint. Fully transparent SourceOver (alpha 0) is identity
            // on any surface. Every other path below writes, and its
            // PaintVersion bump voids the record.
            if (prepared.TryGetFlatColor(out Color skipFlat))
            {
                if (skipFlat.A == 0 && CompositingMode != CompositingMode.SourceCopy)
                    return;
                if ((skipFlat.A == 255 || CompositingMode == CompositingMode.SourceCopy)
                    && _target is not null && TextRasterCounters.TrySkipClearFill(_target, skipFlat.ToArgb()))
                    return;
            }
            // Big-fill attribution (size + color + caller) for flat fills.
            // Runs after the no-op skips above so skipped fills never pay
            // a StackTrace walk: only fills that will actually write are
            // attributed.
            if (TextRasterCounters.PerfEnabled && prepared.TryGetFlatColor(out Color bigFlat))
            {
                long bigPx = (long)(x1 - x0) * (y1 - y0);
                if (bigPx > 50000)
                    TextRasterCounters.NoteBigFill(bigPx, TextRasterCounters.CaptureTelerikCaller(), bigFlat.ToArgb());
            }
            // Perf: flat-color fills (RTE per-block backgrounds: 46M px/frame
            // through per-pixel PutPixel) take a memset path. Transparent
            // SourceOver is a no-op (the old loop rewrote identical pixels);
            // opaque-or-SourceCopy writes the clip intersection: with no
            // clip (or an infinite one) that is the frame bounds, with a
            // single integer device rect it is the rect intersection (the
            // exact set the per-pixel loop would write: same Contains
            // test per pixel, same half-open bounds). Multi-rect unions
            // and fractional rects keep the exact per-pixel loop; empty
            // intersections return with no writes and no version bump.
            if (prepared.TryGetFlatColor(out Color flat) && _target is not null)
            {
                bool fillCopy = CompositingMode == CompositingMode.SourceCopy;
                if (flat.A == 0 && !fillCopy)
                    return;
                if (flat.A == 255 || fillCopy)
                {
                    int mx0 = x0, my0 = y0, mx1 = x1, my1 = y1;
                    bool flatMemset = true;
                    if (_clip is not null && !_clip.IsInfinite(null))
                    {
                        if (_clip.TryGetIntRect(out int cx0, out int cy0, out int cx1, out int cy1))
                        {
                            mx0 = Math.Max(x0, cx0);
                            mx1 = Math.Min(x1, cx1);
                            my0 = Math.Max(y0, cy0);
                            my1 = Math.Min(y1, cy1);
                            if (mx1 <= mx0 || my1 <= my0)
                                return;
                        }
                        else
                            flatMemset = false;
                    }
                    if (flatMemset)
                    {
                        int[] fillDst = _target.Pixels;
                        int fillW = _target.Width;
                        int argb = flat.ToArgb();
                        int fw = mx1 - mx0;
                        if (mx0 == 0 && mx1 == fillW)
                            System.Array.Fill(fillDst, argb, my0 * fillW, (my1 - my0) * fillW);
                        else
                            for (int fy = my0; fy < my1; fy++)
                                System.Array.Fill(fillDst, argb, fy * fillW + mx0, fw);
                        _target.PaintVersion += (my1 - my0) * fw;
                        return;
                    }
                }
            }

            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                    PutPixel(px, py, prepared.Sample(px, py));
        }

        // Integer fast path for TextureBrush rect fills (the viewer-composite
        // blit: one 960x680 texture fill per frame at ~470ms through per-pixel
        // ResolveColor+GetPixel+PutPixel calls). Bit-identical to the slow path:
        // same tile sample (Mod of truncated coords, transforms/wrap ignored
        // exactly like ResolveColor today), same copy-or-opaque branch, same
        // clip treatment, same per-write version bump.
        private void FillTextureFast(Bitmap tile, int x0, int y0, int x1, int y1)
        {
            if (_target is null)
                return;
            int[] dst = _target.Pixels;
            int[] tpx = tile.Pixels;
            int dstW = _target.Width;
            int dstH = _target.Height;
            int tileW = tile.Width;
            int tileH = tile.Height;
            bool copy = CompositingMode == CompositingMode.SourceCopy;
            var clip = _clip;
            x0 = Math.Max(x0, 0);
            y0 = Math.Max(y0, 0);
            x1 = Math.Min(x1, dstW);
            y1 = Math.Min(y1, dstH);
            bool clipCheck = DrawImageCache.IntersectWrite(clip, ref x0, ref y0, ref x1, ref y1);
            for (int y = y0; y < y1; y++)
            {
                int drow = y * dstW;
                int trow = Mod(y, tileH) * tileW;
                for (int x = x0; x < x1; x++)
                {
                    if (clipCheck && clip is not null && !clip.IsVisible(x, y))
                        continue;
                    int argb = tpx[trow + Mod(x, tileW)];
                    _target.PaintVersion++;
                    if (copy || (((uint)argb >> 24) == 255))
                        dst[drow + x] = argb;
                    else
                        dst[drow + x] = BlendOverArgb(dst[drow + x], argb);
                }
            }

            static int Mod(int value, int length) => ((value % length) + length) % length;
        }

        public void DrawRectangle(Pen pen, Rectangle rect) =>
            DrawRectCore(pen, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void DrawRectangle(Pen pen, RectangleF rect) => DrawRectCore(pen, rect);

        public void DrawRectangle(Pen pen, float x, float y, float width, float height) =>
            DrawRectCore(pen, new RectangleF(x, y, width, height));

        public void DrawRectangle(Pen pen, int x, int y, int width, int height) =>
            DrawRectCore(pen, new RectangleF(x, y, width, height));

        public void DrawRectangles(Pen pen, RectangleF[] rects)
        {
            ArgumentNullException.ThrowIfNull(rects);
            foreach (RectangleF rect in rects)
                DrawRectCore(pen, rect);
        }

        public void DrawRectangles(Pen pen, Rectangle[] rects)
        {
            ArgumentNullException.ThrowIfNull(rects);
            foreach (Rectangle rect in rects)
                DrawRectCore(pen, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));
        }

        private void DrawRectCore(Pen pen, RectangleF rect)
        {
            ArgumentNullException.ThrowIfNull(pen);
            float inset = pen.Alignment == PenAlignment.Inset ? pen.Width / 2f : 0f;
            var inner = new RectangleF(rect.X + inset, rect.Y + inset,
                Math.Max(0f, rect.Width - (inset * 2f)), Math.Max(0f, rect.Height - (inset * 2f)));
            StrokeLine(pen, inner.Location, new PointF(inner.Right, inner.Y));
            StrokeLine(pen, new PointF(inner.Right, inner.Y), new PointF(inner.Right, inner.Bottom));
            StrokeLine(pen, new PointF(inner.Right, inner.Bottom), new PointF(inner.X, inner.Bottom));
            StrokeLine(pen, new PointF(inner.X, inner.Bottom), inner.Location);
        }

        public void DrawLine(Pen pen, PointF pt1, PointF pt2) => StrokeLine(pen, pt1, pt2);

        public void DrawLine(Pen pen, Point pt1, Point pt2) =>
            StrokeLine(pen, new PointF(pt1.X, pt1.Y), new PointF(pt2.X, pt2.Y));

        public void DrawLine(Pen pen, float x1, float y1, float x2, float y2) =>
            StrokeLine(pen, new PointF(x1, y1), new PointF(x2, y2));

        public void DrawLine(Pen pen, int x1, int y1, int x2, int y2) =>
            StrokeLine(pen, new PointF(x1, y1), new PointF(x2, y2));

        public void DrawLines(Pen pen, PointF[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            for (int i = 0; i + 1 < points.Length; i++)
                StrokeLine(pen, points[i], points[i + 1]);
        }

        public void DrawLines(Pen pen, Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            for (int i = 0; i + 1 < points.Length; i++)
                StrokeLine(pen, new PointF(points[i].X, points[i].Y), new PointF(points[i + 1].X, points[i + 1].Y));
        }

        private void StrokeLine(Pen pen, PointF a, PointF b)
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");

            // Perf item 4 op census.
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteLine();
            ArgumentNullException.ThrowIfNull(pen);
            DevicePoint(a, out float x0, out float y0);
            DevicePoint(b, out float x1, out float y1);

            float width = Math.Max(1f, pen.Width * UnitScale());
            Color color = ResolveColor(BrushFor(pen), x0, y0);
            int steps = (int)MathF.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                StampDisc(x0 + ((x1 - x0) * t), y0 + ((y1 - y0) * t), width / 2f, color);
            }
        }

        private static Brush BrushFor(Pen pen) =>
            pen.Brush ?? new SolidBrush(pen.Color);

        private void StampDisc(float cx, float cy, float radius, Color color)
        {
            int r = (int)MathF.Ceiling(radius);
            int x0 = (int)MathF.Floor(cx - r);
            int y0 = (int)MathF.Floor(cy - r);
            for (int y = y0; y <= cy + r; y++)
            {
                for (int x = x0; x <= cx + r; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    if ((dx * dx) + (dy * dy) <= (radius * radius) + 0.5f)
                        PutPixel(x, y, color);
                }
            }
        }

        public void FillEllipse(Brush brush, RectangleF rect) => FillEllipseCore(brush, rect);
        public void FillEllipse(Brush brush, Rectangle rect) =>
            FillEllipseCore(brush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));
        public void FillEllipse(Brush brush, float x, float y, float width, float height) =>
            FillEllipseCore(brush, new RectangleF(x, y, width, height));
        public void FillEllipse(Brush brush, int x, int y, int width, int height) =>
            FillEllipseCore(brush, new RectangleF(x, y, width, height));

        private void FillEllipseCore(Brush brush, RectangleF rect)
        {
            ArgumentNullException.ThrowIfNull(brush);
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");

            ToDevice(rect, out float x, out float y, out float w, out float h);
            if (w <= 0f || h <= 0f)
                return;
            if (!TryGetDrawFrame(out int fx0, out int fy0, out int fx1, out int fy1) ||
                !TryBoundRange(x, x + w, fx0, fx1, out int x0, out int x1) ||
                !TryBoundRange(y, y + h, fy0, fy1, out int y0, out int y1))
                return;
            float rx = w / 2f;
            float ry = h / 2f;
            float cx = x + rx;
            float cy = y + ry;
            PreparedBrush prepared = PrepareBrush(brush);
            for (int py = y0; py < y1; py++)
            {
                for (int px = x0; px < x1; px++)
                {
                    float nx = (px + 0.5f - cx) / rx;
                    float ny = (py + 0.5f - cy) / ry;
                    if ((nx * nx) + (ny * ny) <= 1f)
                        PutPixel(px, py, prepared.Sample(px, py));
                }
            }
        }

        public void DrawEllipse(Pen pen, RectangleF rect) => DrawEllipseCore(pen, rect);
        public void DrawEllipse(Pen pen, Rectangle rect) =>
            DrawEllipseCore(pen, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));
        public void DrawEllipse(Pen pen, float x, float y, float width, float height) =>
            DrawEllipseCore(pen, new RectangleF(x, y, width, height));
        public void DrawEllipse(Pen pen, int x, int y, int width, int height) =>
            DrawEllipseCore(pen, new RectangleF(x, y, width, height));

        private void DrawEllipseCore(Pen pen, RectangleF rect)
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");

            ArgumentNullException.ThrowIfNull(pen);
            ToDevice(rect, out float x, out float y, out float w, out float h);
            if (w <= 0f || h <= 0f)
                return;
            float rx = w / 2f;
            float ry = h / 2f;
            float cx = x + rx;
            float cy = y + ry;
            float width = Math.Max(1f, pen.Width * UnitScale());
            Color color = ResolveColor(BrushFor(pen), cx, cy);
            int steps = (int)MathF.Ceiling(2f * MathF.PI * Math.Max(rx, ry));
            for (int i = 0; i <= steps; i++)
            {
                float a = i * 2f * MathF.PI / Math.Max(1, steps);
                StampDisc(cx + (rx * MathF.Cos(a)), cy + (ry * MathF.Sin(a)), width / 2f, color);
            }
        }

        public void FillPolygon(Brush brush, PointF[] points) =>
            FillPolygonCore(brush, points, FillMode.Alternate);

        public void FillPolygon(Brush brush, PointF[] points, FillMode fillMode) =>
            FillPolygonCore(brush, points, fillMode);

        public void FillPolygon(Brush brush, Point[] points) =>
            FillPolygonCore(brush, ToFloats(points), FillMode.Alternate);

        public void FillPolygon(Brush brush, Point[] points, FillMode fillMode) =>
            FillPolygonCore(brush, ToFloats(points), fillMode);

        private void FillPolygonCore(Brush brush, PointF[] points, FillMode fillMode)
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");

            ArgumentNullException.ThrowIfNull(brush);
            ArgumentNullException.ThrowIfNull(points);
            if (points.Length < 3)
                return;
            if (!TryGetDrawFrame(out int fx0, out int fy0, out int fx1, out int fy1))
                return;
            var device = new PointF[points.Length];
            Matrix matrix = DeviceMatrix();
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                matrix.TransformPoint(points[i].X, points[i].Y, out float dx, out float dy);
                device[i] = new PointF(dx, dy);
                minY = Math.Min(minY, dy);
                maxY = Math.Max(maxY, dy);
            }

            if (!TryBoundRange(minY, maxY, fy0, fy1, out int yStart, out int yEnd))
                return;
            PreparedBrush prepared = PrepareBrush(brush);
            var crossings = new List<float>();
            for (int y = yStart; y < yEnd; y++)
            {
                crossings.Clear();
                for (int i = 0, j = device.Length - 1; i < device.Length; j = i++)
                {
                    float y0 = device[i].Y;
                    float y1 = device[j].Y;
                    if ((y0 > y) != (y1 > y))
                        crossings.Add(device[i].X + (((y - y0) / (y1 - y0)) * (device[j].X - device[i].X)));
                }

                crossings.Sort();
                // Degenerate scanlines (flattened SVG subpaths collapsing
                // to horizontal runs) carry no crossings: nothing to fill.
                if (crossings.Count == 0)
                    continue;
                if (fillMode == FillMode.Alternate)
                {
                    for (int k = 0; k + 1 < crossings.Count; k += 2)
                    {
                        if (TryBoundRange(crossings[k], crossings[k + 1], fx0, fx1, out int sx0, out int sx1))
                            for (int x = sx0; x < sx1; x++)
                                PutPixel(x, y, prepared.Sample(x, y));
                    }
                }
                else
                {
                    if (TryBoundRange(crossings[0], crossings[^1], fx0, fx1, out int sx0, out int sx1))
                        for (int x = sx0; x < sx1; x++)
                            PutPixel(x, y, prepared.Sample(x, y));
                }
            }
        }

        public void DrawPolygon(Pen pen, PointF[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            for (int i = 0; i < points.Length; i++)
                StrokeLine(pen, points[i], points[(i + 1) % points.Length]);
        }

        public void DrawPolygon(Pen pen, Point[] points) => DrawPolygon(pen, ToFloats(points));

        public void FillPath(Brush brush, GraphicsPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            FillPolygonCore(brush, path.Flattened(null).ToArray(), path.FillMode);
        }

        public void DrawPath(Pen pen, GraphicsPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            List<PointF> flat = path.Flattened(null);
            var device = new PointF[flat.Count];
            for (int i = 0; i < flat.Count; i++)
            {
                DevicePoint(flat[i], out float dx, out float dy);
                device[i] = new PointF(dx, dy);
            }

            for (int i = 0; i + 1 < device.Length; i++)
                StrokeLineDevice(pen, device[i], device[i + 1]);
        }

        private void StrokeLineDevice(Pen pen, PointF a, PointF b)
        {
            float width = Math.Max(1f, pen.Width * UnitScale());
            Color color = ResolveColor(BrushFor(pen), a.X, a.Y);
            int steps = (int)MathF.Ceiling(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : i / (float)steps;
                StampDisc(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), width / 2f, color);
            }
        }

        public void FillRegion(Brush brush, Region region)
        {
            ArgumentNullException.ThrowIfNull(brush);
            ArgumentNullException.ThrowIfNull(region);
            foreach (RectangleF scan in region.GetRegionScans(DeviceMatrix()))
                FillRectCore(brush, scan);
        }

        public void DrawArc(Pen pen, RectangleF rect, float startAngle, float sweepAngle)
        {
            var path = new GraphicsPath();
            path.AddArc(rect, startAngle, sweepAngle);
            DrawPath(pen, path);
        }

        public void DrawArc(Pen pen, Rectangle rect, float startAngle, float sweepAngle) =>
            DrawArc(pen, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), startAngle, sweepAngle);

        public void DrawArc(Pen pen, float x, float y, float width, float height, float startAngle, float sweepAngle) =>
            DrawArc(pen, new RectangleF(x, y, width, height), startAngle, sweepAngle);

        public void DrawArc(Pen pen, int x, int y, int width, int height, int startAngle, int sweepAngle) =>
            DrawArc(pen, new RectangleF(x, y, width, height), startAngle, sweepAngle);

        public void DrawPie(Pen pen, Rectangle rect, float startAngle, float sweepAngle) =>
            DrawPie(pen, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), startAngle, sweepAngle);

        public void DrawPie(Pen pen, RectangleF rect, float startAngle, float sweepAngle)
        {
            var path = new GraphicsPath();
            path.AddPie(rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);
            DrawPath(pen, path);
        }

        public void DrawPie(Pen pen, float x, float y, float width, float height, float startAngle, float sweepAngle) =>
            DrawPie(pen, new RectangleF(x, y, width, height), startAngle, sweepAngle);

        public void FillPie(Brush brush, Rectangle rect, float startAngle, float sweepAngle) =>
            FillPie(brush, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), startAngle, sweepAngle);

        public void FillPie(Brush brush, RectangleF rect, float startAngle, float sweepAngle)
        {
            var path = new GraphicsPath();
            path.AddPie(rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);
            FillPath(brush, path);
        }

        public void FillPie(Brush brush, float x, float y, float width, float height, float startAngle, float sweepAngle) =>
            FillPie(brush, new RectangleF(x, y, width, height), startAngle, sweepAngle);

        public void FillPie(Brush brush, int x, int y, int width, int height, int startAngle, int sweepAngle) =>
            FillPie(brush, new RectangleF(x, y, width, height), startAngle, sweepAngle);

        public void DrawBeziers(Pen pen, PointF[] points)
        {
            var path = new GraphicsPath();
            path.AddBeziers(points);
            DrawPath(pen, path);
        }

        public void DrawBeziers(Pen pen, Point[] points) => DrawBeziers(pen, ToFloats(points));
        public void DrawBeziers(Pen pen, ReadOnlySpan<PointF> points) => DrawBeziers(pen, points.ToArray());
        public void DrawBeziers(Pen pen, ReadOnlySpan<Point> points) => DrawBeziers(pen, points.ToArray());

        public void FillClosedCurve(Brush brush, PointF[] points) =>
            FillClosedCurve(brush, points, FillMode.Alternate);

        public void FillClosedCurve(Brush brush, PointF[] points, FillMode fillmode)
        {
            var path = new GraphicsPath();
            path.AddClosedCurve(points);
            FillPolygonCore(brush, path.Flattened(null).ToArray(), fillmode);
        }

        public void FillClosedCurve(Brush brush, Point[] points) =>
            FillClosedCurve(brush, ToFloats(points));

        public void FillClosedCurve(Brush brush, Point[] points, FillMode fillmode) =>
            FillClosedCurve(brush, ToFloats(points), fillmode);

        public void DrawClosedCurve(Pen pen, PointF[] points)
        {
            var path = new GraphicsPath();
            path.AddClosedCurve(points);
            DrawPath(pen, path);
        }

        public void DrawClosedCurve(Pen pen, Point[] points) => DrawClosedCurve(pen, ToFloats(points));

        public void DrawCurve(Pen pen, PointF[] points)
        {
            var path = new GraphicsPath();
            path.AddCurve(points);
            DrawPath(pen, path);
        }

        public void DrawCurve(Pen pen, PointF[] points, float tension)
        {
            var path = new GraphicsPath();
            path.AddCurve(points, tension);
            DrawPath(pen, path);
        }

        public void DrawCurve(Pen pen, PointF[] points, int offset, int numberOfSegments) =>
            DrawCurve(pen, points, offset, numberOfSegments, 0.5f);

        public void DrawCurve(Pen pen, PointF[] points, int offset, int numberOfSegments, float tension)
        {
            var path = new GraphicsPath();
            path.AddCurve(points, offset, numberOfSegments, tension);
            DrawPath(pen, path);
        }

        public void DrawCurve(Pen pen, Point[] points)
        {
            var path = new GraphicsPath();
            path.AddCurve(points, 0.5f);
            DrawPath(pen, path);
        }

        public void DrawCurve(Pen pen, Point[] points, float tension)
        {
            var path = new GraphicsPath();
            path.AddCurve(points, tension);
            DrawPath(pen, path);
        }

        public void DrawCurve(Pen pen, Point[] points, int offset, int numberOfSegments, float tension)
        {
            var path = new GraphicsPath();
            path.AddCurve(points, offset, numberOfSegments, tension);
            DrawPath(pen, path);
        }

        public void DrawCurve(Pen pen, ReadOnlySpan<Point> points) =>
            DrawCurve(pen, points.ToArray());

        public void DrawCurve(Pen pen, ReadOnlySpan<Point> points, float tension) =>
            DrawCurve(pen, points.ToArray(), tension);

        public void DrawCurve(Pen pen, ReadOnlySpan<Point> points, int offset, int numberOfSegments, float tension) =>
            DrawCurve(pen, points.ToArray(), offset, numberOfSegments, tension);

        public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points) =>
            DrawCurve(pen, points.ToArray());

        public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points, float tension) =>
            DrawCurve(pen, points.ToArray(), tension);

        public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points, int offset, int numberOfSegments) =>
            DrawCurve(pen, points.ToArray(), offset, numberOfSegments);

        public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points, int offset, int numberOfSegments, float tension) =>
            DrawCurve(pen, points.ToArray(), offset, numberOfSegments, tension);

        public void DrawClosedCurve(Pen pen, PointF[] points, float tension, Drawing2D.FillMode fillmode)
        {
            var path = new GraphicsPath();
            path.AddClosedCurve(points, tension);
            DrawPath(pen, path);
        }

        public void DrawClosedCurve(Pen pen, Point[] points, float tension, Drawing2D.FillMode fillmode) =>
            DrawClosedCurve(pen, ToFloats(points), tension, fillmode);

        public void DrawClosedCurve(Pen pen, ReadOnlySpan<Point> points) =>
            DrawClosedCurve(pen, points.ToArray());

        public void DrawClosedCurve(Pen pen, ReadOnlySpan<Point> points, float tension, Drawing2D.FillMode fillmode) =>
            DrawClosedCurve(pen, points.ToArray(), tension, fillmode);

        public void DrawClosedCurve(Pen pen, ReadOnlySpan<PointF> points) =>
            DrawClosedCurve(pen, points.ToArray());

        public void DrawClosedCurve(Pen pen, ReadOnlySpan<PointF> points, float tension, Drawing2D.FillMode fillmode) =>
            DrawClosedCurve(pen, points.ToArray(), tension, fillmode);

        public void DrawLines(Pen pen, ReadOnlySpan<Point> points) =>
            DrawLines(pen, points.ToArray());

        public void DrawLines(Pen pen, ReadOnlySpan<PointF> points) =>
            DrawLines(pen, points.ToArray());

        public void DrawPolygon(Pen pen, ReadOnlySpan<Point> points) =>
            DrawPolygon(pen, points.ToArray());

        public void DrawPolygon(Pen pen, ReadOnlySpan<PointF> points) =>
            DrawPolygon(pen, points.ToArray());

        public void DrawRectangles(Pen pen, ReadOnlySpan<Rectangle> rects)
        {
            foreach (Rectangle rect in rects)
                DrawRectangle(pen, rect);
        }

        public void DrawRectangles(Pen pen, ReadOnlySpan<RectangleF> rects)
        {
            foreach (RectangleF rect in rects)
                DrawRectangle(pen, rect);
        }

        public void DrawBezier(Pen pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4) =>
            DrawBezier(pen, new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3), new PointF(x4, y4));

        public void DrawBezier(Pen pen, Point pt1, Point pt2, Point pt3, Point pt4) =>
            DrawBezier(pen, new PointF(pt1.X, pt1.Y), new PointF(pt2.X, pt2.Y), new PointF(pt3.X, pt3.Y), new PointF(pt4.X, pt4.Y));

        public void DrawBezier(Pen pen, PointF pt1, PointF pt2, PointF pt3, PointF pt4)
        {
            var path = new GraphicsPath();
            path.AddBezier(pt1, pt2, pt3, pt4);
            DrawPath(pen, path);
        }

        public void DrawPie(Pen pen, int x, int y, int width, int height, int startAngle, int sweepAngle) =>
            DrawPie(pen, (float)x, (float)y, (float)width, (float)height, startAngle, sweepAngle);

        public void DrawRoundedRectangle(Pen pen, Rectangle rect, Size size)
        {
            var path = new GraphicsPath();
            path.AddRoundedRectangle(rect, size);
            DrawPath(pen, path);
        }

        public void DrawRoundedRectangle(Pen pen, RectangleF rect, SizeF size)
        {
            var path = new GraphicsPath();
            path.AddRoundedRectangle(rect, size);
            DrawPath(pen, path);
        }

        public void FillRoundedRectangle(Brush brush, Rectangle rect, Size size)
        {
            var path = new GraphicsPath();
            path.AddRoundedRectangle(rect, size);
            FillPath(brush, path);
        }

        public void FillRoundedRectangle(Brush brush, RectangleF rect, SizeF size)
        {
            var path = new GraphicsPath();
            path.AddRoundedRectangle(rect, size);
            FillPath(brush, path);
        }

        public void FillPolygon(Brush brush, ReadOnlySpan<Point> points) =>
            FillPolygon(brush, points.ToArray());

        public void FillPolygon(Brush brush, ReadOnlySpan<Point> points, FillMode fillMode) =>
            FillPolygon(brush, points.ToArray(), fillMode);

        public void FillPolygon(Brush brush, ReadOnlySpan<PointF> points) =>
            FillPolygon(brush, points.ToArray());

        public void FillPolygon(Brush brush, ReadOnlySpan<PointF> points, FillMode fillMode) =>
            FillPolygon(brush, points.ToArray(), fillMode);

        public void FillClosedCurve(Brush brush, PointF[] points, FillMode fillmode, float tension)
        {
            var path = new GraphicsPath();
            path.AddClosedCurve(points, tension);
            FillPolygonCore(brush, path.Flattened(null).ToArray(), fillmode);
        }

        public void FillClosedCurve(Brush brush, Point[] points, FillMode fillmode, float tension) =>
            FillClosedCurve(brush, ToFloats(points), fillmode, tension);

        public void FillClosedCurve(Brush brush, ReadOnlySpan<Point> points) =>
            FillClosedCurve(brush, points.ToArray());

        public void FillClosedCurve(Brush brush, ReadOnlySpan<Point> points, FillMode fillmode) =>
            FillClosedCurve(brush, points.ToArray(), fillmode);

        public void FillClosedCurve(Brush brush, ReadOnlySpan<Point> points, FillMode fillmode, float tension) =>
            FillClosedCurve(brush, points.ToArray(), fillmode, tension);

        public void FillClosedCurve(Brush brush, ReadOnlySpan<PointF> points) =>
            FillClosedCurve(brush, points.ToArray());

        public void FillClosedCurve(Brush brush, ReadOnlySpan<PointF> points, FillMode fillmode) =>
            FillClosedCurve(brush, points.ToArray(), fillmode);

        public void FillClosedCurve(Brush brush, ReadOnlySpan<PointF> points, FillMode fillmode, float tension) =>
            FillClosedCurve(brush, points.ToArray(), fillmode, tension);

        public void FillRectangles(Brush brush, ReadOnlySpan<Rectangle> rects)
        {
            foreach (Rectangle rect in rects)
                FillRectangle(brush, rect);
        }

        public void FillRectangles(Brush brush, ReadOnlySpan<RectangleF> rects)
        {
            foreach (RectangleF rect in rects)
                FillRectangle(brush, rect);
        }

        public void DrawIcon(Icon icon, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(icon);
            DrawImage(icon.ToBitmap(), x, y);
        }

        public void DrawIcon(Icon icon, Rectangle targetRect)
        {
            ArgumentNullException.ThrowIfNull(icon);
            DrawImage(icon.ToBitmap(), targetRect);
        }

        public void DrawIconUnstretched(Icon icon, Rectangle targetRect)
        {
            ArgumentNullException.ThrowIfNull(icon);
            using Bitmap bitmap = icon.ToBitmap();
            ToDevice(new RectangleF(targetRect.X, targetRect.Y, targetRect.Width, targetRect.Height),
                out float x, out float y, out _, out _);
            Blit(bitmap, 0, 0, bitmap.Width, bitmap.Height, (int)MathF.Round(x), (int)MathF.Round(y), bitmap.Width, bitmap.Height);
        }

        private static PointF[] ToFloats(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = new PointF(points[i].X, points[i].Y);
            return floats;
        }

        public void DrawImage(Image image, Point point) =>
            DrawImage(image, new Rectangle(point.X, point.Y, image.Width, image.Height));

        public void DrawImage(Image image, PointF point) =>
            DrawImage(image, new RectangleF(point.X, point.Y, image.Width, image.Height));

        public void DrawImage(Image image, Rectangle rect) =>
            DrawImage(image, new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void DrawImage(Image image, RectangleF rect) =>
            DrawImage(image, rect, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);

        public void DrawImage(Image image, int x, int y) =>
            DrawImage(image, new Rectangle(x, y, image.Width, image.Height));

        public void DrawImage(Image image, float x, float y) =>
            DrawImage(image, new RectangleF(x, y, image.Width, image.Height));

        public void DrawImage(Image image, Rectangle destRect, Rectangle srcRect, GraphicsUnit srcUnit) =>
            DrawImage(image, new RectangleF(destRect.X, destRect.Y, destRect.Width, destRect.Height),
                new RectangleF(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height), srcUnit);

        private static int s_drawImgCount;

        // Permanent paint-path census (same standing as [RESIMG]): which
        // image draws reach the device funnel, with source and dest sizes.
        // First 8 + every 128th; silent otherwise.
        private static void NoteDrawImage(Image image, float dw, float dh)
        {
            int n = System.Threading.Interlocked.Increment(ref s_drawImgCount);
            if (n <= 8 || n % 128 == 0)
            {
                string src = image is Bitmap bmp ? bmp.Width + "x" + bmp.Height : image.GetType().Name;
                System.Console.WriteLine($"[DRAWIMG] src={src} dest={dw}x{dh}");
            }
        }

        public void DrawImage(Image image, RectangleF destRect, RectangleF srcRect, GraphicsUnit srcUnit)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image is not Bitmap bitmap)
                throw new ArgumentException("Image must be a Bitmap.", nameof(image));
            if (!IsAxisAlignedDevice())
            {
                // Rotated/sheared transform: the axis-aligned Blit below
                // would drop the off-axis components (ToDevice keeps only
                // origin + |w|/|h|), so route through the parallelogram blit
                // built from the dest rect corners instead.
                DrawImageParallelogram(image,
                    new PointF[]
                    {
                        new(destRect.X, destRect.Y),
                        new(destRect.X + destRect.Width, destRect.Y),
                        new(destRect.X, destRect.Y + destRect.Height)
                    },
                    srcRect);
                return;
            }

            ToDevice(destRect, out float x, out float y, out float w, out float h);
            Blit(bitmap, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height,
                (int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(w), (int)MathF.Round(h));
        }

        // True when the device matrix maps axes to axes (identity,
        // translate, axis-aligned scale/flip): the fast Blit path stays
        // pixel-exact. Any rotation/shear (off-diagonal != 0) needs the
        // parallelogram path. Epsilon absorbs float trig residue (e.g.
        // cos(90deg) ~= -4.4e-8) so near-180deg flips keep fast-path behavior.
        private bool IsAxisAlignedDevice()
        {
            System.Numerics.Matrix3x2 elements = DeviceMatrix().MatrixElements;
            return MathF.Abs(elements.M12) < 1e-6f && MathF.Abs(elements.M21) < 1e-6f;
        }

        public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit) =>
            DrawImageParallelogram(image, Array.ConvertAll(destPoints, p => new PointF(p.X, p.Y)),
                new RectangleF(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));

        public void DrawImage(Image image, PointF[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit) =>
            DrawImageParallelogram(image, destPoints,
                new RectangleF(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));

        private void DrawImageParallelogram(Image image, PointF[] destPoints, RectangleF srcRect)
        {
            if (IsDisplay && !IsDisplayAxisBlit(destPoints))
                throw new NotSupportedException("Display path records axis-aligned image blits only.");
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(destPoints);
            if (image is not Bitmap bitmap || destPoints.Length < 3)
                throw new ArgumentException("Image must be a Bitmap with three destination points.", nameof(image));
            var unit = new RectangleF(0, 0, 1, 1);
            var fit = new Matrix(unit, new PointF[] { destPoints[0], destPoints[1], destPoints[2] });
            fit.Invert();
            Matrix toDevice = DeviceMatrix();
            Matrix toWorld = toDevice.Clone();
            toWorld.Invert();
            // Device-space bounding box over all four corners: under
            // rotation the world-space min/max corners do not map to the
            // device-space extremes, so transform every corner.
            var fourth = new PointF(
                destPoints[1].X + destPoints[2].X - destPoints[0].X,
                destPoints[1].Y + destPoints[2].Y - destPoints[0].Y);
            var corners = new PointF[] { destPoints[0], destPoints[1], destPoints[2], fourth };
            toDevice.TransformPoints(corners);
            float minX = corners[0].X;
            float minY = corners[0].Y;
            float maxX = corners[0].X;
            float maxY = corners[0].Y;
            for (int i = 1; i < corners.Length; i++)
            {
                minX = Math.Min(minX, corners[i].X);
                minY = Math.Min(minY, corners[i].Y);
                maxX = Math.Max(maxX, corners[i].X);
                maxY = Math.Max(maxY, corners[i].Y);
            }

            // Same hot-loop treatment as Blit: the two 3x2 transforms are
            // inlined in identical operation order (bit-identical
            // sampling), arrays/strides hoisted, dest bbox clamped to
            // the target once. Tilted photos paint through here.
            if (_target is null)
                return;
            int[] dst = _target.Pixels;
            _target.PaintVersion++;
            int[] spx = bitmap.Pixels;
            int dstW = _target.Width;
            int dstH = _target.Height;
            int srcW = bitmap.Width;
            int srcH = bitmap.Height;
            System.Numerics.Matrix3x2 worldToLocal = toWorld.MatrixElements;
            System.Numerics.Matrix3x2 fitToUnit = fit.MatrixElements;
            bool copy = CompositingMode == CompositingMode.SourceCopy;
            var clip = _clip;
            // Full inked bbox (pre-clip): the memo layer covers this set, so
            // identical draws recall across clip changes; the clip applies
            // fresh at write time via the shared write-set computation.
            int fbx0 = Math.Max((int)MathF.Floor(minX), 0);
            int fby0 = Math.Max((int)MathF.Floor(minY), 0);
            int fbx1 = Math.Min((int)MathF.Ceiling(maxX), dstW);
            int fby1 = Math.Min((int)MathF.Ceiling(maxY), dstH);
            DrawImageKey memoKey = default;
            bool stash = fbx1 > fbx0 && fby1 > fby0 && DrawImageCache.KeyForPara(bitmap, _target, srcRect, worldToLocal, fitToUnit, fbx1 - fbx0, fby1 - fby0, InterpolationMode, clip, out memoKey);
            if (stash && DrawImageCache.TryRecallPara(bitmap, memoKey, fbx0, fby0, fbx1 - fbx0, fby1 - fby0, out DrawImageEntry? hit) && hit is not null)
            {
                ParaCached(_target, hit, copy, clip);
                return;
            }
            stash = stash && DrawImageCache.NoteRepeat(bitmap, memoKey);
            int x0 = fbx0;
            int y0 = fby0;
            int x1 = fbx1;
            int y1 = fby1;
            bool clipCheck = DrawImageCache.IntersectWrite(clip, ref x0, ref y0, ref x1, ref y1);
            int stashW = fbx1 - fbx0;
            int stashH = fby1 - fby0;
            int[]? stashPx = stash ? new int[stashW * stashH] : null;
            byte[]? stashInk = stash ? new byte[(stashW * stashH + 7) / 8] : null;
            bool stashOpaque = true;
            for (int py = fby0; py < fby1; py++)
            {
                int drow = py * dstW;
                bool inY = py >= y0 && py < y1;
                for (int px = fbx0; px < fbx1; px++)
                {
                    float wx = px + 0.5f;
                    float wy = py + 0.5f;
                    float tx = (wx * worldToLocal.M11) + (wy * worldToLocal.M21) + worldToLocal.M31;
                    float ty = (wx * worldToLocal.M12) + (wy * worldToLocal.M22) + worldToLocal.M32;
                    float ux = (tx * fitToUnit.M11) + (ty * fitToUnit.M21) + fitToUnit.M31;
                    float uy = (tx * fitToUnit.M12) + (ty * fitToUnit.M22) + fitToUnit.M32;
                    if (ux < 0f || ux >= 1f || uy < 0f || uy >= 1f)
                        continue;
                    int srcX = Math.Clamp((int)(srcRect.X + (ux * srcRect.Width)), 0, srcW - 1);
                    int srcY = Math.Clamp((int)(srcRect.Y + (uy * srcRect.Height)), 0, srcH - 1);
                    int argb = spx[srcY * srcW + srcX];
                    if (stashPx is not null && stashInk is not null)
                    {
                        int so = (py - fby0) * stashW + (px - fbx0);
                        stashPx[so] = argb;
                        stashInk[so >> 3] |= (byte)(1 << (so & 7));
                        if (((uint)argb >> 24) != 255)
                            stashOpaque = false;
                    }
                    if (!inY || px < x0 || px >= x1)
                        continue;
                    if (clipCheck && clip is not null && !clip.IsVisible(px, py))
                        continue;
                    if (copy || (((uint)argb >> 24) == 255))
                        dst[drow + px] = argb;
                    else
                        dst[drow + px] = BlendOverArgb(dst[drow + px], argb);
                }
            }
            if (stashPx is not null && stashInk is not null)
                DrawImageCache.Store(bitmap, new DrawImageEntry { Key = memoKey, Pixels = stashPx, Ox = fbx0, Oy = fby0, W = stashW, H = stashH, AllOpaque = stashOpaque, Inked = stashInk });
        }

        // Memo hit writer for the parallelogram loop: replays a sampled layer
        // with a fresh clip and dst. Mirrors the miss loop exactly (same write
        // set via IntersectWrite, same inked-hole skip, same copy-or-opaque
        // branch over live dst), so hits are bit-identical to direct draws.
        private static void ParaCached(Bitmap target, DrawImageEntry hit, bool copy, Region? clip)
        {
            // Perf item 4 op census (parallelogram recall path; px unknown here).
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteImage();
            int[] dst = target.Pixels;
            int dstW = target.Width;
            int dstH = target.Height;
            int x0 = Math.Max(hit.Ox, 0);
            int y0 = Math.Max(hit.Oy, 0);
            int x1 = Math.Min(hit.Ox + hit.W, dstW);
            int y1 = Math.Min(hit.Oy + hit.H, dstH);
            bool clipCheck = DrawImageCache.IntersectWrite(clip, ref x0, ref y0, ref x1, ref y1);
            int[] layer = hit.Pixels;
            byte[]? inked = hit.Inked;
            bool direct = copy || hit.AllOpaque;
            bool wrotePara = false;
            if (!clipCheck && direct)
            {
                // Fast replay: the write set is exactly the intersected
                // rect rows and every written pixel is a plain store, so
                // whole inked runs move with block copies (bit-identical
                // to the per-pixel loop below; holes stay skipped). Solid
                // and empty inked bytes fast-forward 8 pixels at a time.
                int ox = hit.Ox;
                int ow = hit.W;
                if (inked is null)
                {
                    int count = x1 - x0;
                    if (count > 0 && y0 < y1)
                        target.PaintVersion++;
                    for (int y = y0; y < y1; y++)
                        Array.Copy(layer, (y - hit.Oy) * ow + (x0 - ox), dst, y * dstW + x0, count);
                    return;
                }
                for (int y = y0; y < y1; y++)
                {
                    int lrow = (y - hit.Oy) * ow;
                    int lo = lrow + (x0 - ox);
                    int hi = lrow + (x1 - ox);
                    int b = lo;
                    int rs = -1;
                    while (b < hi)
                    {
                        if (rs < 0)
                        {
                            if ((b & 7) == 0 && b + 8 <= hi && inked[b >> 3] == 0) { b += 8; continue; }
                            if ((inked[b >> 3] & (1 << (b & 7))) != 0) rs = b;
                            b++;
                        }
                        else
                        {
                            if ((b & 7) == 0 && b + 8 <= hi && inked[b >> 3] == 0xFF) { b += 8; continue; }
                            if ((inked[b >> 3] & (1 << (b & 7))) == 0)
                            {
                                Array.Copy(layer, rs, dst, y * dstW + ox + (rs - lrow), b - rs);
                                wrotePara = true;
                                rs = -1;
                            }
                            b++;
                        }
                    }
                    if (rs >= 0)
                    {
                        Array.Copy(layer, rs, dst, y * dstW + ox + (rs - lrow), hi - rs);
                        wrotePara = true;
                    }
                }
                if (wrotePara)
                    target.PaintVersion++;
                return;
            }
            for (int y = y0; y < y1; y++)
            {
                int drow = y * dstW;
                int lrow = (y - hit.Oy) * hit.W;
                for (int x = x0; x < x1; x++)
                {
                    if (clipCheck && clip is not null && !clip.IsVisible(x, y))
                        continue;
                    int o = lrow + (x - hit.Ox);
                    if (inked is not null && (inked[o >> 3] & (1 << (o & 7))) == 0)
                        continue;
                    int argb = layer[o];
                    if (direct || (((uint)argb >> 24) == 255))
                        dst[drow + x] = argb;
                    else
                        dst[drow + x] = BlendOverArgb(dst[drow + x], argb);
                    wrotePara = true;
                }
            }
            if (wrotePara)
                target.PaintVersion++;
        }

        public void DrawImage(Image image, float x, float y, float width, float height) =>
            DrawImage(image, new RectangleF(x, y, width, height));

        public void DrawImage(Image image, float x, float y, RectangleF srcRect, GraphicsUnit srcUnit) =>
            DrawImage(image, new RectangleF(x, y, srcRect.Width, srcRect.Height), srcRect, srcUnit);

        public void DrawImage(Image image, int x, int y, int width, int height) =>
            DrawImage(image, new Rectangle(x, y, width, height));

        public void DrawImage(Image image, int x, int y, Rectangle srcRect, GraphicsUnit srcUnit) =>
            DrawImage(image, new Rectangle(x, y, srcRect.Width, srcRect.Height), srcRect, srcUnit);

        public void DrawImage(Image image, Point[] destPoints)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(destPoints);
            DrawImageParallelogram(image, Array.ConvertAll(destPoints, p => new PointF(p.X, p.Y)),
                new RectangleF(0, 0, image.Width, image.Height));
        }

        public void DrawImage(Image image, PointF[] destPoints)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(destPoints);
            DrawImageParallelogram(image, destPoints, new RectangleF(0, 0, image.Width, image.Height));
        }

        public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit) =>
            DrawImageParallelogram(image, destPoints, srcRect);

        public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr) =>
            DrawImageAttributedParallelogram(image, ToPointF(destPoints),
                new RectangleF(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height), imageAttr, null, IntPtr.Zero);

        public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback) =>
            DrawImageAttributedParallelogram(image, ToPointF(destPoints),
                new RectangleF(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height), imageAttr, callback, IntPtr.Zero);

        public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback, int callbackData) =>
            DrawImageAttributedParallelogram(image, ToPointF(destPoints),
                new RectangleF(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height), imageAttr, callback, (IntPtr)callbackData);

        public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr) =>
            DrawImageAttributedParallelogram(image, destPoints, srcRect, imageAttr, null, IntPtr.Zero);

        public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback) =>
            DrawImageAttributedParallelogram(image, destPoints, srcRect, imageAttr, callback, IntPtr.Zero);

        public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback, int callbackData) =>
            DrawImageAttributedParallelogram(image, destPoints, srcRect, imageAttr, callback, (IntPtr)callbackData);

        public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit) =>
            DrawImage(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), srcUnit);

        public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr) =>
            DrawImageAttributed(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr, null, IntPtr.Zero);

        public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback) =>
            DrawImageAttributed(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr, callback, IntPtr.Zero);

        public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback, nint callbackData) =>
            DrawImageAttributed(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr, callback, (IntPtr)callbackData);

        public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit) =>
            DrawImage(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), srcUnit);

        public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr) =>
            DrawImageAttributed(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr, null, IntPtr.Zero);

        public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback) =>
            DrawImageAttributed(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr, callback, IntPtr.Zero);

        public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr, DrawImageAbort? callback, nint callbackData) =>
            DrawImageAttributed(image, RectToF(destRect), new RectangleF(srcX, srcY, srcWidth, srcHeight), imageAttr, callback, (IntPtr)callbackData);

        public void DrawImage(Image image, Imaging.Effects.Effect effect)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(effect);
            var temp = new Bitmap(image);
            effect.ApplyTo(temp);
            DrawImage(temp, new RectangleF(0, 0, temp.Width, temp.Height));
        }

        public void DrawImage(Image image, Imaging.Effects.Effect effect, RectangleF destRect, Drawing2D.Matrix? matrix, GraphicsUnit srcUnit, Imaging.ImageAttributes? imageAttr)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(effect);
            var temp = new Bitmap(image);
            imageAttr?.ApplyDefaultTo(temp);
            effect.ApplyTo(temp);
            Drawing2D.Matrix saved = Transform;
            try
            {
                if (matrix is not null)
                    MultiplyTransform(matrix, Drawing2D.MatrixOrder.Prepend);
                DrawImage(temp, destRect, new RectangleF(0, 0, temp.Width, temp.Height), srcUnit);
            }
            finally
            {
                Transform = saved;
            }
        }

        private static PointF[] ToPointF(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            return Array.ConvertAll(points, p => new PointF(p.X, p.Y));
        }

        private static RectangleF RectToF(Rectangle rect) =>
            new(rect.X, rect.Y, rect.Width, rect.Height);

        private void DrawImageAttributed(Image image, RectangleF destRect, RectangleF srcRect, Imaging.ImageAttributes? attributes, DrawImageAbort? callback, IntPtr callbackData)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (callback is not null && callback(callbackData))
                return;
            if (IsDisplay && attributes is not null)
                throw new NotSupportedException("Display path does not implement ImageAttributes.");
            if (attributes is null)
            {
                DrawImage(image, destRect, srcRect, GraphicsUnit.Pixel);
                return;
            }
            var temp = new Bitmap(image);
            attributes.ApplyDefaultTo(temp);
            DrawImage(temp, destRect, new RectangleF(0, 0, temp.Width, temp.Height), GraphicsUnit.Pixel);
        }

        private void DrawImageAttributedParallelogram(Image image, PointF[] destPoints, RectangleF srcRect, Imaging.ImageAttributes? attributes, DrawImageAbort? callback, IntPtr callbackData)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(destPoints);
            if (callback is not null && callback(callbackData))
                return;
            if (IsDisplay && attributes is not null)
                throw new NotSupportedException("Display path does not implement ImageAttributes.");
            if (attributes is null)
            {
                DrawImageParallelogram(image, destPoints, srcRect);
                return;
            }
            var temp = new Bitmap(image);
            attributes.ApplyDefaultTo(temp);
            DrawImageParallelogram(temp, destPoints, srcRect);
        }

        public void DrawImageUnscaledAndClipped(Image image, Rectangle rect)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image is not Bitmap bitmap)
                throw new ArgumentException("Image must be a Bitmap.", nameof(image));
            int w = Math.Min(rect.Width, bitmap.Width);
            int h = Math.Min(rect.Height, bitmap.Height);
            if (w <= 0 || h <= 0)
                return;
            DevicePoint(new PointF(rect.X, rect.Y), out float dx, out float dy);
            Blit(bitmap, 0, 0, w, h, (int)MathF.Round(dx), (int)MathF.Round(dy), w, h);
        }

        public void DrawCachedBitmap(Imaging.CachedBitmap cachedBitmap, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(cachedBitmap);
            DrawImageUnscaled(cachedBitmap.Bitmap, x, y);
        }

        public void DrawImageUnscaled(Image image, Point point) =>
            DrawImageUnscaled(image, point.X, point.Y);

        public void DrawImageUnscaled(Image image, Rectangle rect) =>
            DrawImageUnscaled(image, rect.X, rect.Y, rect.Width, rect.Height);

        public void DrawImageUnscaled(Image image, int x, int y) =>
            DrawImageUnscaled(image, x, y, image.Width, image.Height);

        public void DrawImageUnscaled(Image image, int x, int y, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image is not Bitmap bitmap)
                throw new ArgumentException("Image must be a Bitmap.", nameof(image));
            if (!IsAxisAlignedDevice())
            {
                DrawImageParallelogram(image,
                    new PointF[]
                    {
                        new(x, y),
                        new(x + width, y),
                        new(x, y + height)
                    },
                    new RectangleF(0, 0, bitmap.Width, bitmap.Height));
                return;
            }

            DevicePoint(new PointF(x, y), out float dx, out float dy);
            Blit(bitmap, 0, 0, Math.Min(width, bitmap.Width), Math.Min(height, bitmap.Height),
                (int)MathF.Round(dx), (int)MathF.Round(dy),
                Math.Min(width, bitmap.Width), Math.Min(height, bitmap.Height));
        }

        // Hot loop (a photo view paints ~500K dest pixels/frame through
        // here): raw int arrays, hoisted steps/strides, dest bounds
        // clamped once up front, zero per-pixel calls on the opaque
        // fast path. Sampling matches the old math (nearest, +0.5
        // centers, clamp): (int) truncation equals Floor for the
        // non-negative coords that survive the clamp, and negatives
        // clamp to 0 either way.
        private void Blit(Bitmap src, float sx, float sy, float sw, float sh, int dx, int dy, int dw, int dh)
        {
            // Census first: attempts with empty rects return silently below,
            // and those are exactly the layout bugs this marker hunts.
            NoteDrawImage(src, dw, dh);
            if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0)
                return;
            if (IsDisplay) { RecordDisplayBlit(src, sx, sy, sw, sh, dx, dy, dw, dh); return; }
            if (_target is null)
                return;
            {
            }
            _target.PaintVersion++;
            // Memoized resample (see DrawImageCache): the sampled layer is a
            // pure function of (src version, src rect, dst size), so position,
            // clip and dst bounds apply fresh at write time and identical draws
            // recall across moves and clip changes.
            // Perf item 4 op census.
            if (TextRasterCounters.PerfEnabled)
            {
                TextRasterCounters.NoteImage();
                TextRasterCounters.NoteImagePx((long)dw * dh);
            }
            bool stash = DrawImageCache.KeyForBlit(src, _target, sx, sy, sw, sh, dw, dh, InterpolationMode, _clip, out DrawImageKey memoKey);
            if (stash && DrawImageCache.TryRecallBlit(src, memoKey, out DrawImageEntry? hit) && hit is not null)
            {
                BlitCached(_target, hit, dx, dy, dw, dh, CompositingMode == CompositingMode.SourceCopy, _clip);
                return;
            }
            stash = stash && DrawImageCache.NoteRepeat(src, memoKey);
            int[] dst = _target.Pixels;
            int[] spx = src.Pixels;
            int dstW = _target.Width;
            int dstH = _target.Height;
            int srcW = src.Width;
            int srcH = src.Height;
            float stepX = sw / dw;
            float stepY = sh / dh;
            int x0 = Math.Max(dx, 0);
            int y0 = Math.Max(dy, 0);
            int x1 = Math.Min(dx + dw, dstW);
            int y1 = Math.Min(dy + dh, dstH);
            bool copy = CompositingMode == CompositingMode.SourceCopy;
            var clip = _clip;
            bool clipCheck = DrawImageCache.IntersectWrite(clip, ref x0, ref y0, ref x1, ref y1);
            int[]? stashPx = stash ? new int[dw * dh] : null;
            bool stashOpaque = true;
            for (int ly = 0; ly < dh; ly++)
            {
                int y = dy + ly;
                bool inY = y >= y0 && y < y1;
                int srcY = Math.Clamp((int)(sy + ((ly + 0.5f) * stepY)), 0, srcH - 1);
                int srow = srcY * srcW;
                int drow = y * dstW;
                for (int lx = 0; lx < dw; lx++)
                {
                    int srcX = Math.Clamp((int)(sx + ((lx + 0.5f) * stepX)), 0, srcW - 1);
                    int argb = spx[srow + srcX];
                    if (stashPx is not null)
                    {
                        stashPx[ly * dw + lx] = argb;
                        if (((uint)argb >> 24) != 255)
                            stashOpaque = false;
                    }
                    int x = dx + lx;
                    if (!inY || x < x0 || x >= x1)
                        continue;
                    if (clipCheck && clip is not null && !clip.IsVisible(x, y))
                        continue;
                    if (copy || (((uint)argb >> 24) == 255))
                        dst[drow + x] = argb;
                    else
                        dst[drow + x] = BlendOverArgb(dst[drow + x], argb);
                }
            }
            if (stashPx is not null)
                DrawImageCache.Store(src, new DrawImageEntry { Key = memoKey, Pixels = stashPx, Ox = 0, Oy = 0, W = dw, H = dh, AllOpaque = stashOpaque });
        }

        // Memo hit writer: replays a sampled layer with fresh position, clip
        // and dst. Mirrors the Blit miss loop exactly (same write set via
        // IntersectWrite, same copy-or-opaque branch over live dst), so hits
        // are bit-identical to direct draws.
        private static void BlitCached(Bitmap target, DrawImageEntry hit, int dx, int dy, int dw, int dh, bool copy, Region? clip)
        {
            // Perf item 4 op census (memoized recall path).
            if (TextRasterCounters.PerfEnabled)
            {
                TextRasterCounters.NoteImage();
                TextRasterCounters.NoteImagePx((long)dw * dh);
            }
            int[] dst = target.Pixels;
            int dstW = target.Width;
            int dstH = target.Height;
            int x0 = Math.Max(dx, 0);
            int y0 = Math.Max(dy, 0);
            int x1 = Math.Min(dx + dw, dstW);
            int y1 = Math.Min(dy + dh, dstH);
            bool clipCheck = DrawImageCache.IntersectWrite(clip, ref x0, ref y0, ref x1, ref y1);
            int[] layer = hit.Pixels;
            bool direct = copy || hit.AllOpaque;
            if (!clipCheck && direct)
            {
                // Fast replay: rectangular write set, plain stores.
                int count = x1 - x0;
                if (count > 0 && y0 < y1)
                    target.PaintVersion++;
                for (int y = y0; y < y1; y++)
                    Array.Copy(layer, (y - dy) * dw + (x0 - dx), dst, y * dstW + x0, count);
                return;
            }
            bool wroteBlit = false;
            for (int y = y0; y < y1; y++)
            {
                int drow = y * dstW;
                int lrow = (y - dy) * dw;
                for (int x = x0; x < x1; x++)
                {
                    if (clipCheck && clip is not null && !clip.IsVisible(x, y))
                        continue;
                    int argb = layer[lrow + (x - dx)];
                    if (direct || (((uint)argb >> 24) == 255))
                        dst[drow + x] = argb;
                    else
                        dst[drow + x] = BlendOverArgb(dst[drow + x], argb);
                    wroteBlit = true;
                }
            }
            if (wroteBlit)
                target.PaintVersion++;
        }

        // Int-ARGB twin of BlendOver (identical math, no Color round trip).
        private static int BlendOverArgb(int dest, int srcArgb)
        {
            int sa = (int)((uint)srcArgb >> 24);
            int inv = 255 - sa;
            int sr = (srcArgb >> 16) & 0xFF;
            int sg = (srcArgb >> 8) & 0xFF;
            int sb = srcArgb & 0xFF;
            int dr = (dest >> 16) & 0xFF;
            int dg = (dest >> 8) & 0xFF;
            int db = dest & 0xFF;
            int da = (int)((uint)dest >> 24);
            int r = ((sr * sa) + (dr * inv)) / 255;
            int g = ((sg * sa) + (dg * inv)) / 255;
            int b = ((sb * sa) + (db * inv)) / 255;
            int a = sa + ((da * inv) / 255);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        private static Color SampleNearest(Bitmap src, float x, float y)
        {
            int sx = Math.Clamp((int)MathF.Floor(x), 0, src.Width - 1);
            int sy = Math.Clamp((int)MathF.Floor(y), 0, src.Height - 1);
            return src.GetPixel(sx, sy);
        }

        public void DrawString(string? text, Font font, Brush brush, PointF point) =>
            DrawString(text, font, brush, new RectangleF(point.X, point.Y, 0, 0), null);

        public void DrawString(string? text, Font font, Brush brush, RectangleF layoutRectangle) =>
            DrawString(text, font, brush, layoutRectangle, null);

        public void DrawString(string? text, Font font, Brush brush, PointF point, StringFormat? format) =>
            DrawString(text, font, brush, new RectangleF(point.X, point.Y, 0, 0), format);

        // Bounded DrawString census (tile-text diagnosis): which rects reach
        // the rasterizer, with what shape. Counters only (no per-call
        // strings on this hot path); surfaced via DumpDrawStringCensus.
        private static int s_dsCalls;
        private static int s_dsEmptyRect;
        private static int s_dsNullFormat;
        public static string DumpDrawStringCensus() =>
            "draws=" + s_dsCalls + " emptyRect=" + s_dsEmptyRect + " nullFormat=" + s_dsNullFormat;
        public void DrawString(string? text, Font font, Brush brush, RectangleF layoutRectangle, StringFormat? format)
        {

            ArgumentNullException.ThrowIfNull(font);
            ArgumentNullException.ThrowIfNull(brush);

            if (string.IsNullOrEmpty(text))
                return;
            // Display graphics has no CPU target by design; its text hook
            // fires after rasterization below (glyph island only). Only the
            // CPU path needs a target here.
            if (_target is null && !IsDisplay)
                return;
            if (!BrowserText.IsAvailable && BrowserText.RasterOverride is null)
                throw new NotSupportedException("Text rasterization needs the browser canvas bridge.");
            // Perf item 4: paint volume (which spans reach the rasterizer).
            s_dsCalls++;
            if (layoutRectangle.Width <= 0f || layoutRectangle.Height <= 0f) s_dsEmptyRect++;
            if (format is null) s_dsNullFormat++;
            long tDraw0 = TextRasterCounters.PerfEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            if (tDraw0 != 0)
                TextRasterCounters.NoteDraw(text?.Length ?? 0);
            // Glyph pixels come from the browser (white-with-alpha); the
            // tint below is the brush color, so antialiasing blends via
            // the normal SourceOver PutPixel path. Rotation beyond the
            // origin is ignored (matches the rect cores); multiline
            // blocks align as one block, not per line.
                        ToDevice(layoutRectangle, out float dx, out float dy, out float dw, out float dh);
            int maxW = dw > 0f ? ClampRasterWidth(dw)
                : IsDisplay ? 2048  // point layout: unbounded, no CPU target by design
                : Math.Max(1, _target!.Width - (int)dx);  // CPU path always has a target here
            if (!WrapsText(format))
            maxW = 2048;
            BrowserText.Raster? raster = BrowserText.TryRaster(text, font, DpiY, Math.Min(maxW, 2048));
            // TEMP-DIAG(headtext): remove after the mosaic root cause lands.
            if (text == "RGB" || text == "HSV" || text == "OPACITY - ALPHA")
                System.Console.WriteLine("[D2DTXT] text='" + text + "' raster=" + (raster is null ? "null" : raster.Width + "x" + raster.Height) + " dpi=" + DpiY + " maxW=" + Math.Min(maxW, 2048) + " target=" + (_target is null ? "null" : _target.Width + "x" + _target.Height) + " css=" + BrowserText.ToCss(font, DpiY));
            if (raster is null)
                throw new NotSupportedException("Browser text raster failed.");
            // Perf item 4: blend volume (is the invoke bucket pixel-bound?).
            if (TextRasterCounters.PerfEnabled)
                TextRasterCounters.NoteDrawPx((long)raster.Width * raster.Height);

            Color ink = brush is SolidBrush solid ? solid.Color : ResolveColor(brush, layoutRectangle.X, layoutRectangle.Y);
            int ox = (int)MathF.Round(dx);
            int oy = (int)MathF.Round(dy);
            if (dw > 0f && format is not null)
            {
                ox = format.Alignment switch
                {
                    StringAlignment.Center => ox + (int)MathF.Round((dw - raster.Width) / 2f),
                    StringAlignment.Far => ox + (int)MathF.Round(dw - raster.Width),
                    _ => ox,
                };
            }

            if (dh > 0f && format is not null)
            {
                oy = format.LineAlignment switch
                {
                    StringAlignment.Center => oy + (int)MathF.Round((dh - raster.Height) / 2f),
                    StringAlignment.Far => oy + (int)MathF.Round(dh - raster.Height),
                    _ => oy,
                };
            }

            bool noClip = format is not null && (format.FormatFlags & StringFormatFlags.NoClip) != 0;
            int clipX0 = ox, clipY0 = oy, clipX1 = ox + raster.Width, clipY1 = oy + raster.Height;
            if (!noClip && dw > 0f && dh > 0f)
            {
                clipX0 = Math.Max(clipX0, (int)MathF.Floor(dx));
                clipY0 = Math.Max(clipY0, (int)MathF.Floor(dy));
                clipX1 = Math.Min(clipX1, SaturateCeilToInt(dx + dw));
                clipY1 = Math.Min(clipY1, SaturateCeilToInt(dy + dh));
            }

            if (IsDisplay)
            {
                // Explicit CPU text island (glyphs only, measured import);
                // the control surface itself is never rasterized/uploaded.
                RecordDisplayText(raster.Rgba, raster.Width, raster.Height,
                    ink.A, ink.R, ink.G, ink.B,
                    ox, oy, clipX0, clipY0, clipX1, clipY1);
                return;
            }
            // Bit-identical blend hoists: ink channels out of the loop, clip
            // ranges instead of per-pixel branches, target fields hoisted,
            // one PaintVersion bump per call (change-detection only needs
            // bump-on-write: DrawImageCache keys on change, and zero
            // writes bump nothing either way). NaN geometry draws nothing
            // either way (PutPixel bounds-swallows), so an explicit finite
            // guard replaces wraparound reasoning at the ranges below.
            if (!float.IsFinite(dx) || !float.IsFinite(dy))
                return;
            byte[] rgba = raster.Rgba;
            int rw = raster.Width, rh = raster.Height;
            Bitmap? target = _target;
            if (target is null)
                return;
            int tw = target.Width, th = target.Height;
            int[] tpixels = target.Pixels;
            int inkA = ink.A, inkR = ink.R, inkG = ink.G, inkB = ink.B;
            bool sourceCopy = CompositingMode == CompositingMode.SourceCopy;
            Region? drawClip = _clip;
            bool clipNone = drawClip is null || drawClip.IsInfinite(null);
            int ry0 = Math.Max(0, Math.Max(clipY0 - oy, 0 - oy));
            int ry1 = Math.Min(rh, Math.Min(clipY1 - oy, th - oy));
            int rx0 = Math.Max(0, Math.Max(clipX0 - ox, 0 - ox));
            int rx1 = Math.Min(rw, Math.Min(clipX1 - ox, tw - ox));
            bool wrote = false;
            for (int ry = ry0; ry < ry1; ry++)
            {
                int py = oy + ry;
                int row = ry * rw;
                for (int rx = rx0; rx < rx1; rx++)
                {
                    int a = rgba[(row + rx) * 4 + 3];
                    if (a == 0)
                        continue;
                    if (!clipNone)
                    {
                        int pxc = ox + rx;
                        if (!drawClip.IsVisible(pxc, py))
                            continue;
                    }
                    int ea = (a * inkA) / 255;
                    int src = (ea << 24) | (inkR << 16) | (inkG << 8) | inkB;
                    int idx = py * tw + ox + rx;
                    tpixels[idx] = (sourceCopy || ea >= 255) ? src : BlendOverInt(tpixels[idx], ea, inkR, inkG, inkB);
                    wrote = true;
                }
            }
            if (wrote)
                target.PaintVersion++;
            if (tDraw0 != 0)
                TextRasterCounters.NoteDrawTicks(System.Diagnostics.Stopwatch.GetTimestamp() - tDraw0);
        }

        public void DrawString(string? text, Font font, Brush brush, float x, float y) =>
            DrawString(text, font, brush, new PointF(x, y));

        public void DrawString(string? text, Font font, Brush brush, float x, float y, StringFormat? format) =>
            DrawString(text, font, brush, new PointF(x, y), format);

        public SizeF MeasureString(string? text, Font font) =>
            MeasureReal(text, font, SizeF.Empty);

        public SizeF MeasureString(string? text, Font font, int width) =>
            MeasureReal(text, font, new SizeF(width, 0));

        public SizeF MeasureString(string? text, Font font, int width, StringFormat? format) =>
            MeasureReal(text, font, new SizeF(width, 0), WrapsText(format), format);

        public SizeF MeasureString(string? text, Font font, PointF origin, StringFormat? format) =>
            MeasureReal(text, font, SizeF.Empty, true, format);

        public SizeF MeasureString(string? text, Font font, SizeF layoutArea) =>
            MeasureReal(text, font, layoutArea);

        public SizeF MeasureString(string? text, Font font, SizeF layoutArea, StringFormat? format) =>
            MeasureReal(text, font, layoutArea, WrapsText(format), format);

        public SizeF MeasureString(string? text, Font font, SizeF layoutArea, StringFormat? format, out int charsFitted, out int linesFilled)
        {
            SizeF size = MeasureReal(text, font, layoutArea, WrapsText(format), format);
            charsFitted = text?.Length ?? 0;
            // Wrapped lines filled counts visual rows off the line-box
            // grid (the em/8 height pad is under half a line, so plain
            // rounding absorbs it for 1..N rows alike).
            float lineH = LineBoxHeight(font);
            linesFilled = lineH > 0f && size.Height > 0f
                ? Math.Max(1, (int)MathF.Round(size.Height / lineH))
                : (text is null ? 0 : Math.Max(1, text.Split('\n').Length));
            return size;
        }

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font, SizeF layoutArea, StringFormat? format, out int charsFitted, out int linesFilled) =>
            MeasureString(text.ToString(), font, layoutArea, format, out charsFitted, out linesFilled);

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font, SizeF layoutArea, StringFormat? format) =>
            MeasureString(text.ToString(), font, layoutArea, format);

        internal SizeF MeasureStringTight(string? text, Font font) =>
            MeasureReal(text, font, SizeF.Empty, true, null, true);

        internal SizeF MeasureStringTight(string? text, Font font, int width, StringFormat? format) =>
            MeasureReal(text, font, new SizeF(width, 0), WrapsText(format), format, true);

        public Region[] MeasureCharacterRanges(string? text, Font font, RectangleF layoutRect, StringFormat? format) =>
            throw new NotSupportedException("Text measurement arrives with the text milestone.");

        public Region[] MeasureCharacterRanges(string? text, Font font, RectangleF layoutRect, StringFormat? format, out int charsFitted) =>
            throw new NotSupportedException("Text measurement arrives with the text milestone.");

        // Raster-backed measurement (mirrors the DrawString core above):
        // when the browser text raster is available its tight glyph bounds
        // are the size, otherwise (headless, raster failure) fall back to
        // the historical approximation. Never throws: layout paths call
        // MeasureString on every pass, so any raster failure degrades to
        // the fallback instead of surfacing.
        // Raster width clamp: narrow BEFORE the float->int conversion.
        // (int)Ceiling(int.MaxValue-as-float) wraps to int.MinValue in an
        // unchecked context, which Math.Max(1, ..) then pins to a 1px
        // raster: unconstrained Telerik measures (proposedSize=int.MaxValue)
        // collapsed every label to ~1px (selectCategoryLabel rendered "I").
        // Saturating float->int for clip bounds: layout rects of int.MaxValue
        // (the standard "don't wrap" idiom, used by Telerik TPF text) make
        // dx+dw round to 2^31 in float, and an unchecked (int) cast wraps it
        // to int.MinValue, which empties the clip and paints nothing.
        private static int SaturateCeilToInt(float v)
        {
            float c = MathF.Ceiling(v);
            if (c >= 2147483647f) return int.MaxValue;
            if (c <= -2147483648f) return int.MinValue;
            return (int)c;
        }

        private static int ClampRasterWidth(float w) =>
            w > 0f && w < 2048f ? Math.Max(1, (int)MathF.Ceiling(w)) : 2048;
        // GDI+ wraps at the layout width unless StringFormatFlags.NoWrap;
        // honor it so single-line Telerik labels measure (and render) one line.
        private static bool WrapsText(StringFormat? format) =>
            format is null || (format.FormatFlags & StringFormatFlags.NoWrap) == 0;
        // GenericTypographic (FitBlackBox|LineLimit|NoClip) measures tight
        // design advances; every other format gets the GDI+ layout pad
        // (desktop oracle text-padfit.fsx: all single non-typographic
        // flag combos keep the full pad).
        // GDI+ line-box height in graphics units (oracle text-linebox.fsx).
        private float LineBoxHeight(Font font)
        {
            float unit = UnitScale();
            if (unit == 0f)
                unit = 1f;
            float emPx = Math.Max(1f, font.SizeInPoints * DpiY / 72f);
            return emPx * FontFamily.GdiLineBoxFactor(font.Name) / unit;
        }

        private static bool SkipsGdiPad(StringFormat? format) =>
            format is not null && (format.FormatFlags &
                (StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip)) ==
                (StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit | StringFormatFlags.NoClip);

        private SizeF MeasureReal(string? text, Font font, SizeF layoutArea, bool wrap = true,
            StringFormat? format = null, bool tight = false)
        {

            ArgumentNullException.ThrowIfNull(font);
            if (string.IsNullOrEmpty(text))
                return SizeF.Empty;
            try
            {
                                // GDI+ drops trailing spaces/tabs unless MeasureTrailingSpaces
                // (desktop oracle gdimeasure: "The "=="The" in both formats).
                // Whitespace-only is one space wide by default but zero-width
                // when typographic/tight (" "=4.00 default, -0.00 typo); Telerik
                // TPF lays out inter-word gaps as space elements, which must
                // measure zero or every line grows ~4px per gap.
                if (format is null || (format.FormatFlags & StringFormatFlags.MeasureTrailingSpaces) == 0)
                {
                    string trimmed = text is null ? string.Empty : text.TrimEnd(' ', '\t');
                    if (trimmed.Length == 0)
                    {
                        float unit0 = UnitScale();
                        if (unit0 == 0f)
                            unit0 = 1f;
                        if (tight || SkipsGdiPad(format))
                            return new SizeF(0f, LineBoxHeight(font));
                        float spaceW = 0f;
                        try
                        {
                            BrowserText.Raster? spaceRaster = BrowserText.TryMeasure(" ", font, DpiY, 2048);
                            if (spaceRaster is not null && spaceRaster.Width > 0)
                                spaceW = spaceRaster.Width / unit0;
                        }
                        catch
                        {
                        }
                        if (spaceW <= 0f)
                            spaceW = MeasureApproximate(" ", font, SizeF.Empty).Width;
                        float emPx0 = Math.Max(1f, font.SizeInPoints * DpiY / 72f);
                        return new SizeF(spaceW, LineBoxHeight(font) + emPx0 / (8f * unit0));
                    }
                    text = trimmed;
                }
                int maxW = wrap && layoutArea.Width > 0f ? ClampRasterWidth(layoutArea.Width) : 2048;
                BrowserText.Raster? raster = BrowserText.TryMeasure(text, font, DpiY, maxW);
                if (raster is not null && raster.Width > 0 && raster.Height > 0)
                {
                    float unit = UnitScale();
                    if (unit == 0f)
                        unit = 1f;
                    float w = raster.Width / unit;
                    if (tight || SkipsGdiPad(format))
                    {
                        // Fractional canvas advance (exact GDI+ Typographic
                        // twin): integer rasters ceil (19.35->20), drifting
                        // TPF layouts ~+0.6px/word. The padded path keeps the
                        // integer width (its pad fit absorbs the ceil).
                        // Headless/tests fall back to raster width.
                        float? fw = BrowserText.TryMeasureWidth(text, font, DpiY, maxW);
                        if (fw.HasValue && fw.Value > 0f && fw.Value <= raster.Width + 0.5f)
                            w = fw.Value / unit;
                    }
                    // GDI+ heights are string-independent line boxes; GDI
                    // extents (tight/Typographic) keep the raster ink box.
                    bool gdiPad = !tight && !SkipsGdiPad(format);
                    float emPx = gdiPad ? Math.Max(1f, font.SizeInPoints * DpiY / 72f) : 0f;
                    float lineH = gdiPad ? LineBoxHeight(font) : raster.Height / unit;
                    if (wrap && layoutArea.Width > 0f && w > layoutArea.Width)
                    {
                        int rows = Math.Max(1, (int)Math.Ceiling(w / layoutArea.Width));
                        // GDI+ adds em/8 once over rows x line box (oracle
                        // text-multi.fsx: 2 lines = 33.42 = 2 x 15.96 + 1.5).
                        return new SizeF(layoutArea.Width, rows * lineH + emPx / (8f * unit));
                    }
                    float h = lineH;

                    if (gdiPad)
                    {
                        // GDI+ GenericDefault layout pad (desktop oracle
                        // text-padfit.fsx/text-padgen.fsx, 6 fonts x sizes):
                        // pad = em/3 + 0.03*tight, minus 0.5px for the
                        // integer-raster ceil bias. GDI+ breaks words on
                        // tight advances and pads the result, so the pad
                        // applies only past the wrap compare above (a pad
                        // before the compare double-counted rows).
                        // Headless fallback below stays unpadded (crude
                        // units, sanity-only).
                        float tightPx = w * unit;
                        float padPx = emPx / 3f + 0.03f * tightPx - 0.5f;
                        if (padPx > 0f)
                            w += padPx / unit;
                        // Height: GDI+ line box + em/8 (oracle
                        // text-height-ink.fsx: Segoe 9 = 17.46).
                        h = lineH + emPx / (8f * unit);
                    }
                    return new SizeF(w, h);
                }
            }
            catch
            {
                // Fall through to the approximation below.
            }

            return MeasureApproximate(text, font, layoutArea, wrap);
        }

        private static SizeF MeasureApproximate(string? text, Font font, SizeF layoutArea, bool wrap = true)
        {
            ArgumentNullException.ThrowIfNull(font);
            if (string.IsNullOrEmpty(text))
                return SizeF.Empty;
            float em = Math.Max(1f, font.Size);
            float lineHeight = Math.Max(1f, font.Height);
            string[] lines = text.Split('\n');
            float maxWidth = 0f;
            foreach (string line in lines)
                maxWidth = Math.Max(maxWidth, line.Length * em * 0.5f);
                        // NoWrap (GDI SingleLine/Default): one row per embedded \n, never
            // word-wrapped (desktop parity; the old code wrapped SingleLine
            // measures to 3 rows, e.g. Telerik tile titles).
            if (!wrap)
                return new SizeF(maxWidth, lines.Length * lineHeight);
            if (layoutArea.Width > 0f && maxWidth > layoutArea.Width)
            {
                int wrapped = 0;
                foreach (string line in lines)
                    wrapped += Math.Max(1, (int)Math.Ceiling((line.Length * em * 0.5f) / layoutArea.Width));
                return new SizeF(layoutArea.Width, wrapped * lineHeight);
            }

            return new SizeF(maxWidth, lines.Length * lineHeight);
        }

        public void CopyFromScreen(Point upperLeftSource, Point upperLeftDestination, Size blockRegionSize) =>
            throw new NotSupportedException("Screen capture is not available on web.");

        public void CopyFromScreen(int sourceX, int sourceY, int destinationX, int destinationY, Size blockRegionSize) =>
            throw new NotSupportedException("Screen capture is not available on web.");

        public void CopyFromScreen(Point upperLeftSource, Point upperLeftDestination, Size blockRegionSize, CopyPixelOperation copyPixelOperation) =>
            throw new NotSupportedException("Screen capture is not available on web.");

        public void CopyFromScreen(int sourceX, int sourceY, int destinationX, int destinationY, Size blockRegionSize, CopyPixelOperation copyPixelOperation) =>
            throw new NotSupportedException("Screen capture is not available on web.");

        public void DrawString(ReadOnlySpan<char> text, Font font, Brush brush, float x, float y) =>
            DrawString(text.ToString(), font, brush, x, y);

        public void DrawString(ReadOnlySpan<char> text, Font font, Brush brush, PointF point) =>
            DrawString(text.ToString(), font, brush, point);

        public void DrawString(ReadOnlySpan<char> text, Font font, Brush brush, RectangleF layoutRectangle) =>
            DrawString(text.ToString(), font, brush, layoutRectangle);

        public void DrawString(ReadOnlySpan<char> text, Font font, Brush brush, float x, float y, StringFormat? format) =>
            DrawString(text.ToString(), font, brush, x, y, format);

        public void DrawString(ReadOnlySpan<char> text, Font font, Brush brush, PointF point, StringFormat? format) =>
            DrawString(text.ToString(), font, brush, point, format);

        public void DrawString(ReadOnlySpan<char> text, Font font, Brush brush, RectangleF layoutRectangle, StringFormat? format) =>
            DrawString(text.ToString(), font, brush, layoutRectangle, format);

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font) =>
            MeasureString(text.ToString(), font);

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font, int width) =>
            MeasureString(text.ToString(), font, width);

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font, int width, StringFormat? format) =>
            MeasureString(text.ToString(), font, width, format);

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font, PointF origin, StringFormat? format) =>
            MeasureString(text.ToString(), font, origin, format);

        public SizeF MeasureString(ReadOnlySpan<char> text, Font font, SizeF layoutArea) =>
            MeasureString(text.ToString(), font, layoutArea);

        public SizeF MeasureStringInternal(ReadOnlySpan<char> text, Font font, RectangleF layoutRectangle, StringFormat? format, out int charsFitted, out int linesFilled)
        {
            SizeF size = MeasureString(text.ToString(), font, new SizeF(layoutRectangle.Width, layoutRectangle.Height), format, out charsFitted, out linesFilled);
            return size;
        }

        public Region[] MeasureCharacterRanges(ReadOnlySpan<char> text, Font font, RectangleF layoutRect, StringFormat? format) =>
            MeasureCharacterRanges(text.ToString(), font, layoutRect, format);

        public System.Numerics.Matrix3x2 TransformElements
        {
            get => _transform.MatrixElements;
            set => _transform = new Matrix(value);
        }

        public void TransformPoints(Drawing2D.CoordinateSpace destSpace, Drawing2D.CoordinateSpace sourceSpace, Point[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            PointF[] floats = ToFloats(pts);
            TransformPoints(destSpace, sourceSpace, floats);
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Point((int)MathF.Round(floats[i].X), (int)MathF.Round(floats[i].Y));
        }

        public void TransformPoints(Drawing2D.CoordinateSpace destSpace, Drawing2D.CoordinateSpace sourceSpace, PointF[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            if (destSpace == sourceSpace)
                return;
            PointF[] device = (PointF[])pts.Clone();
            if (sourceSpace != Drawing2D.CoordinateSpace.Device)
            {
                if (sourceSpace == Drawing2D.CoordinateSpace.World)
                    ScalePoints(device, PageScale == 0f ? 1f : PageScale);
                Matrix toDevice = DeviceMatrix();
                toDevice.TransformPoints(device);
            }

            if (destSpace == Drawing2D.CoordinateSpace.Device)
            {
                Array.Copy(device, pts, pts.Length);
                return;
            }

            Matrix fromDevice = DeviceMatrix();
            fromDevice.Invert();
            fromDevice.TransformPoints(device);
            if (destSpace == Drawing2D.CoordinateSpace.World)
                ScalePoints(device, PageScale == 0f ? 1f : 1f / PageScale);
            Array.Copy(device, pts, pts.Length);
        }

        public void TransformPoints(Drawing2D.CoordinateSpace destSpace, Drawing2D.CoordinateSpace sourceSpace, ReadOnlySpan<Point> pts)
        {
            Point[] copy = pts.ToArray();
            TransformPoints(destSpace, sourceSpace, copy);
        }

        public void TransformPoints(Drawing2D.CoordinateSpace destSpace, Drawing2D.CoordinateSpace sourceSpace, ReadOnlySpan<PointF> pts)
        {
            PointF[] copy = pts.ToArray();
            TransformPoints(destSpace, sourceSpace, copy);
        }

        public void SetClip(Drawing2D.GraphicsPath path) =>
            SetClip(path, Drawing2D.CombineMode.Replace);

        public void SetClip(Drawing2D.GraphicsPath path, Drawing2D.CombineMode combineMode)
        {
            ArgumentNullException.ThrowIfNull(path);
            CombineClip(new Region(path), combineMode);
        }

        public void TranslateClip(float dx, float dy) => _clip?.Translate(dx, dy);

        public void TranslateClip(int dx, int dy) => _clip?.Translate(dx, dy);

        public bool IsVisible(float x, float y, float width, float height) =>
            IntersectsDevice(new RectangleF(x, y, width, height));

        public Color GetNearestColor(Color color) => color;

        public static IntPtr GetHalftonePalette() => IntPtr.Zero;

        public static Graphics FromHdcInternal(IntPtr hdc) =>
            new();

        public void ReleaseHdcInternal(IntPtr hdc)
        {
            try { HdcReleaseHook?.Invoke(hdc); } catch { }
        }

        public void GetContextInfo(out PointF dpi) => dpi = new PointF(DpiX, DpiY);

        public void GetContextInfo(out PointF dpi, out Region clip)
        {
            dpi = new PointF(DpiX, DpiY);
            clip = (_clip?.Clone() as Region) ?? new Region();
        }

        public object GetContextInfo() => new ContextInfo(DpiX, DpiY, (_clip?.Clone() as Region) ?? new Region());

        private sealed class ContextInfo
        {
            public ContextInfo(float dpiX, float dpiY, Region clip)
            {
                DpiX = dpiX;
                DpiY = dpiY;
                Clip = clip;
            }

            public float DpiX { get; }
            public float DpiY { get; }
            public Region Clip { get; }
        }

        public void AddMetafileComment(byte[] data) =>
            throw new NotSupportedException("Metafiles are not supported on web.");

        public void EnumerateMetafile(Imaging.Metafile metafile, Point destPoint, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point destPoint, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point destPoint, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point[] destPoint, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point[] destPoint, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point[] destPoint, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point[] destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point[] destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Point[] destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF destPoint, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF destPoint, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF destPoint, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF[] destPoint, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF[] destPoint, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF[] destPoint, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF[] destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF[] destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, PointF[] destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, Rectangle destPoint, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Rectangle destPoint, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Rectangle destPoint, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, Rectangle destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Rectangle destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, Rectangle destPoint, Rectangle srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, RectangleF destPoint, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, RectangleF destPoint, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, RectangleF destPoint, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        public void EnumerateMetafile(Imaging.Metafile metafile, RectangleF destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback) =>
            EnumerateMetafileCore(metafile, callback, IntPtr.Zero, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, RectangleF destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, null);

        public void EnumerateMetafile(Imaging.Metafile metafile, RectangleF destPoint, RectangleF srcRect, GraphicsUnit srcUnit, EnumerateMetafileProc callback, nint callbackData, Imaging.ImageAttributes? imageAttr) =>
            EnumerateMetafileCore(metafile, callback, (IntPtr)callbackData, imageAttr);

        private void EnumerateMetafileCore(Imaging.Metafile metafile, EnumerateMetafileProc callback, IntPtr callbackData, Imaging.ImageAttributes? imageAttr)
        {
            if (IsDisplay) throw new NotSupportedException("Display path does not implement this operation.");
            ArgumentNullException.ThrowIfNull(metafile);
            ArgumentNullException.ThrowIfNull(callback);
        }

        public delegate bool DrawImageAbort(IntPtr callbackData);

        public delegate bool EnumerateMetafileProc(
            Imaging.EmfPlusRecordType recordType, int flags, int dataSize,
            IntPtr data, Imaging.PlayRecordCallback callbackData);

        private static void ScalePoints(PointF[] pts, float scale)
        {
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new PointF(pts[i].X * scale, pts[i].Y * scale);
        }
    }
}

