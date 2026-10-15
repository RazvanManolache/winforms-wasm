// Gradient brushes: exact .NET member shapes over the CPU rasterizer.
// Path-gradient fills are approximated with the center color; the sampled
// radial interpolation waits for the gradient milestone.
namespace System.Drawing.Drawing2D
{
    /// <summary>Arrow cap (cf. System.Drawing.Drawing2D.AdjustableArrowCap).</summary>
    public sealed class AdjustableArrowCap : CustomLineCap
    {
        public AdjustableArrowCap(float width, float height)
            : this(width, height, true)
        {
        }

        public AdjustableArrowCap(float width, float height, bool filled)
        {
            Width = width;
            Height = height;
            Filled = filled;
        }

        public float Height { get; set; }
        public float Width { get; set; }
        public float MiddleInset { get; set; }
        public bool Filled { get; set; }
    }

    /// <summary>Radial gradient brush (cf. System.Drawing.Drawing2D.PathGradientBrush).</summary>
    public sealed class PathGradientBrush : Brush
    {
        public PathGradientBrush(PointF[] points)
            : this(points, WrapMode.Clamp)
        {
        }

        public PathGradientBrush(Point[] points)
            : this(ToFloat(points), WrapMode.Clamp)
        {
        }

        public PathGradientBrush(PointF[] points, WrapMode wrapMode)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Length < 2)
                throw new ArgumentException("Path gradient needs at least two points.", nameof(points));
            _points = (PointF[])points.Clone();
            WrapMode = wrapMode;
            InitDefaults();
        }

        public PathGradientBrush(Point[] points, WrapMode wrapMode)
            : this(ToFloat(points), wrapMode)
        {
        }

        public PathGradientBrush(ReadOnlySpan<PointF> points)
            : this(points.ToArray())
        {
        }

        public PathGradientBrush(ReadOnlySpan<Point> points)
            : this(points.ToArray())
        {
        }

        public PathGradientBrush(WrapMode wrapMode, ReadOnlySpan<PointF> points)
            : this(points.ToArray(), wrapMode)
        {
        }

        public PathGradientBrush(WrapMode wrapMode, ReadOnlySpan<Point> points)
            : this(points.ToArray(), wrapMode)
        {
        }

        public PathGradientBrush(GraphicsPath path)
        {
            ArgumentNullException.ThrowIfNull(path);
            _points = path.PathPoints;
            if (_points.Length < 2)
                throw new ArgumentException("Path gradient needs at least two points.", nameof(path));
            WrapMode = WrapMode.Clamp;
            InitDefaults();
        }

        public override object Clone()
        {
            var clone = new PathGradientBrush(_points, WrapMode)
            {
                CenterColor = CenterColor,
                CenterPoint = CenterPoint,
                FocusScales = FocusScales,
            };
            clone._focus = _focus;
            clone._scale = _scale;
            clone.SurroundColors = (Color[])SurroundColors.Clone();
            if (_blend is not null)
                clone.Blend = new Blend { Factors = (float[])_blend.Factors.Clone(), Positions = (float[])_blend.Positions.Clone() };
            if (_interpolation is not null)
                clone.InterpolationColors = new ColorBlend { Colors = (Color[])_interpolation.Colors.Clone(), Positions = (float[])_interpolation.Positions.Clone() };
            clone.Transform = Transform.Clone();
            return clone;
        }

        public Color CenterColor { get; set; } = Color.FromArgb(0, 0, 0);

        public PointF CenterPoint { get; set; }

        public Color[] SurroundColors { get; set; } = [Color.FromArgb(255, 255, 255)];

        public RectangleF Rectangle
        {
            get
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                foreach (PointF p in _points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
                return RectangleF.FromLTRB(minX, minY, maxX, maxY);
            }
        }

        public Blend Blend
        {
            get => _blend ??= new Blend();
            set => _blend = value;
        }

        public ColorBlend InterpolationColors
        {
            get => _interpolation ??= new ColorBlend();
            set => _interpolation = value;
        }

        public Matrix Transform { get; set; } = new Matrix();

        public PointF FocusScales
        {
            get => _focus;
            set
            {
                _focus = value;
                _scale = 0f;
            }
        }

        public WrapMode WrapMode { get; set; }

        public void SetSigmaBellShape(float focus) => SetSigmaBellShape(focus, 1f);

        public void SetSigmaBellShape(float focus, float scale)
        {
            _focus = new PointF(focus, focus);
            _scale = scale;
        }

        public void SetBlendTriangularShape(float focus) => SetBlendTriangularShape(focus, 1f);

        public void SetBlendTriangularShape(float focus, float scale)
        {
            if (focus is < 0f or > 1f)
                throw new ArgumentException("Focus must be between 0 and 1.", nameof(focus));
            Blend = new Blend
            {
                Factors = [0f, scale, 0f],
                Positions = [0f, focus, 1f],
            };
        }

        public void ResetTransform() => Transform.Reset();

        public void MultiplyTransform(Matrix matrix) =>
            MultiplyTransform(matrix, MatrixOrder.Prepend);

        public void MultiplyTransform(Matrix matrix, MatrixOrder order) =>
            Transform.Multiply(matrix, order);

        public void RotateTransform(float angle) =>
            RotateTransform(angle, MatrixOrder.Prepend);

        public void RotateTransform(float angle, MatrixOrder order) =>
            Transform.Rotate(angle, order);

        public void ScaleTransform(float sx, float sy) =>
            ScaleTransform(sx, sy, MatrixOrder.Prepend);

        public void ScaleTransform(float sx, float sy, MatrixOrder order) =>
            Transform.Scale(sx, sy, order);

        public void TranslateTransform(float dx, float dy) =>
            TranslateTransform(dx, dy, MatrixOrder.Prepend);

        public void TranslateTransform(float dx, float dy, MatrixOrder order) =>
            Transform.Translate(dx, dy, order);

        protected override void Dispose(bool disposing)
        {
        }

        private void InitDefaults()
        {
            float sx = 0f, sy = 0f;
            foreach (PointF p in _points)
            {
                sx += p.X;
                sy += p.Y;
            }

            CenterPoint = new PointF(sx / _points.Length, sy / _points.Length);
        }

        private static PointF[] ToFloat(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = new PointF(points[i].X, points[i].Y);
            return floats;
        }

        private PointF[] _points;
        private PointF _focus;
        private float _scale;
        private Blend? _blend;
        private ColorBlend? _interpolation;
    }
}
