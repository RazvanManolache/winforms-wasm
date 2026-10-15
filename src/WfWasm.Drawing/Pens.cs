// Pen and brush family: exact .NET member shapes, web-compatible.
// Strokes/fills rasterize through Graphics; native GDI+ objects do not exist.
namespace System.Drawing
{
    /// <summary>Fill base (cf. System.Drawing.Brush).</summary>
    public abstract class Brush : ICloneable, IDisposable
    {
        protected Brush()
        {
        }

        public abstract object Clone();

        protected void SetNativeBrush(nint nativeBrush)
        {
            _nativeBrush = nativeBrush;
        }

        private nint _nativeBrush;

        ~Brush()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Texture fill (cf. System.Drawing.TextureBrush): image tiling
    /// arrives with the image-codec milestone; color/transform store today.</summary>
    public sealed class TextureBrush : Brush
    {
        public TextureBrush(Image image)
            : this(image, Drawing2D.WrapMode.Tile)
        {
        }

        public TextureBrush(Image image, Drawing2D.WrapMode wrapMode)
            : this(image, wrapMode, new RectangleF(0, 0, image.Width, image.Height))
        {
        }

        public TextureBrush(Image image, RectangleF dstRect)
            : this(image, Drawing2D.WrapMode.Tile, dstRect)
        {
        }

        public TextureBrush(Image image, Drawing2D.WrapMode wrapMode, RectangleF dstRect)
        {
            ArgumentNullException.ThrowIfNull(image);
            Image = image;
            WrapMode = wrapMode;
            _transform = new Drawing2D.Matrix();
        }

        public TextureBrush(Image image, Drawing2D.WrapMode wrapMode, Rectangle dstRect)
            : this(image, wrapMode, new RectangleF(dstRect.X, dstRect.Y, dstRect.Width, dstRect.Height))
        {
        }

        public TextureBrush(Image image, Rectangle dstRect)
            : this(image, Drawing2D.WrapMode.Tile, dstRect)
        {
        }

        public TextureBrush(Image image, RectangleF dstRect, Imaging.ImageAttributes imageAttributes)
            : this(image, Drawing2D.WrapMode.Tile, dstRect)
        {
            _imageAttributes = imageAttributes;
        }

        public TextureBrush(Image image, Rectangle dstRect, Imaging.ImageAttributes imageAttributes)
            : this(image, dstRect)
        {
            _imageAttributes = imageAttributes;
        }

        public Image Image { get; }
        public Drawing2D.WrapMode WrapMode { get; set; }

        public void MultiplyTransform(Drawing2D.Matrix matrix) =>
            MultiplyTransform(matrix, Drawing2D.MatrixOrder.Prepend);

        public void MultiplyTransform(Drawing2D.Matrix matrix, Drawing2D.MatrixOrder order) =>
            _transform.Multiply(matrix, order);

        public void ResetTransform() => _transform.Reset();

        public void RotateTransform(float angle) =>
            RotateTransform(angle, Drawing2D.MatrixOrder.Prepend);

        public void RotateTransform(float angle, Drawing2D.MatrixOrder order) =>
            _transform.Rotate(angle, order);

        public void ScaleTransform(float sx, float sy) =>
            ScaleTransform(sx, sy, Drawing2D.MatrixOrder.Prepend);

        public void ScaleTransform(float sx, float sy, Drawing2D.MatrixOrder order) =>
            _transform.Scale(sx, sy, order);

        public void TranslateTransform(float dx, float dy) =>
            TranslateTransform(dx, dy, Drawing2D.MatrixOrder.Prepend);

        public void TranslateTransform(float dx, float dy, Drawing2D.MatrixOrder order) =>
            _transform.Translate(dx, dy, order);
        public Drawing2D.Matrix Transform
        {
            get => _transform.Clone();
            set => _transform = value?.Clone() ?? new Drawing2D.Matrix();
        }

        public override object Clone() => new TextureBrush(Image, WrapMode);

        protected override void Dispose(bool disposing)
        {
        }

        private Drawing2D.Matrix _transform;
        private Imaging.ImageAttributes? _imageAttributes;
    }

    /// <summary>Solid fill (cf. System.Drawing.SolidBrush).</summary>
    public sealed class SolidBrush : Brush
    {
        public SolidBrush(Color color)
        {
            Color = color;
        }

        public Color Color { get; set; }

        public override object Clone() => new SolidBrush(Color);

        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>Stroke (cf. System.Drawing.Pen): width/color/caps store here;
    /// dash patterns rasterize as solid until dashing arrives.</summary>
    public sealed class Pen : ICloneable, IDisposable
    {
        private Color _color;
        private float _width;
        private Drawing2D.Matrix _transform = new();

        public Pen(Color color)
            : this(color, 1f)
        {
        }

        public Pen(Color color, float width)
        {
            _color = color;
            Width = width;
        }

        public Pen(Brush brush)
            : this(brush, 1f)
        {
        }

        public Pen(Brush brush, float width)
        {
            ArgumentNullException.ThrowIfNull(brush);
            Brush = brush;
            _color = Color.Empty;
            Width = width;
        }

        public Brush? Brush { get; set; }

        public Color Color
        {
            get => Brush is SolidBrush solid ? solid.Color : _color;
            set
            {
                _color = value;
                Brush = new SolidBrush(value);
            }
        }

        public float Width
        {
            get => _width;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                _width = value;
            }
        }

        public Drawing2D.PenAlignment Alignment { get; set; } = Drawing2D.PenAlignment.Center;
        public Drawing2D.LineCap StartCap { get; set; } = Drawing2D.LineCap.Flat;
        public Drawing2D.LineCap EndCap { get; set; } = Drawing2D.LineCap.Flat;
        public Drawing2D.DashCap DashCap { get; set; } = Drawing2D.DashCap.Flat;
        public Drawing2D.DashStyle DashStyle { get; set; } = Drawing2D.DashStyle.Solid;
        public Drawing2D.LineJoin LineJoin { get; set; } = Drawing2D.LineJoin.Miter;
        public float MiterLimit { get; set; } = 10f;
        public float DashOffset { get; set; }
        public Drawing2D.CustomLineCap? CustomStartCap { get; set; }
        public Drawing2D.CustomLineCap? CustomEndCap { get; set; }
        public Drawing2D.PenType PenType => Brush is null or SolidBrush
            ? Drawing2D.PenType.SolidColor
            : Drawing2D.PenType.HatchFill;

        public float[] CompoundArray { get; set; } = [];
        public float[] DashPattern { get; set; } = [];

        public Drawing2D.Matrix Transform
        {
            get => _transform.Clone();
            set => _transform = value?.Clone() ?? new Drawing2D.Matrix();
        }

        public object Clone()
        {
            var clone = (Pen)MemberwiseClone();
            clone._transform = _transform.Clone();
            clone.CompoundArray = (float[])CompoundArray.Clone();
            clone.DashPattern = (float[])DashPattern.Clone();
            return clone;
        }

        public void SetLineCap(Drawing2D.LineCap startCap, Drawing2D.LineCap endCap, Drawing2D.DashCap dashCap)
        {
            StartCap = startCap;
            EndCap = endCap;
            DashCap = dashCap;
        }

        public void ResetTransform() => _transform.Reset();

        public void MultiplyTransform(Drawing2D.Matrix matrix) =>
            MultiplyTransform(matrix, Drawing2D.MatrixOrder.Prepend);

        public void MultiplyTransform(Drawing2D.Matrix matrix, Drawing2D.MatrixOrder order)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            _transform.Multiply(matrix, order);
        }

        public void TranslateTransform(float dx, float dy) =>
            TranslateTransform(dx, dy, Drawing2D.MatrixOrder.Prepend);

        public void TranslateTransform(float dx, float dy, Drawing2D.MatrixOrder order) =>
            _transform.Translate(dx, dy, order);

        public void ScaleTransform(float sx, float sy) =>
            ScaleTransform(sx, sy, Drawing2D.MatrixOrder.Prepend);

        public void ScaleTransform(float sx, float sy, Drawing2D.MatrixOrder order) =>
            _transform.Scale(sx, sy, order);

        public void RotateTransform(float angle) =>
            RotateTransform(angle, Drawing2D.MatrixOrder.Prepend);

        public void RotateTransform(float angle, Drawing2D.MatrixOrder order) =>
            _transform.Rotate(angle, order);

        ~Pen()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}

namespace System.Drawing.Drawing2D
{
    /// <summary>Blend factors (cf. System.Drawing.Drawing2D.Blend).</summary>
    public sealed class Blend
    {
        public Blend()
        {
        }

        public Blend(int count)
        {
            Factors = new float[count];
            Positions = new float[count];
        }

        public float[] Factors { get; set; } = [1f];
        public float[] Positions { get; set; } = [0f, 1f];
    }

    /// <summary>Multi-color blend (cf. System.Drawing.Drawing2D.ColorBlend).</summary>
    public sealed class ColorBlend
    {
        public ColorBlend()
        {
        }

        public ColorBlend(int count)
        {
            Colors = new Color[count];
            Positions = new float[count];
        }

        public Color[] Colors { get; set; } = [Color.Empty];
        public float[] Positions { get; set; } = [0f, 1f];
    }

    /// <summary>Custom cap base (cf. System.Drawing.Drawing2D.CustomLineCap).</summary>
    public class CustomLineCap : ICloneable, IDisposable
    {
        protected CustomLineCap()
        {
        }

        public CustomLineCap(GraphicsPath? fillPath, GraphicsPath? strokePath)
            : this(fillPath, strokePath, LineCap.Flat, 0f)
        {
        }

        public CustomLineCap(GraphicsPath? fillPath, GraphicsPath? strokePath, LineCap baseCap)
            : this(fillPath, strokePath, baseCap, 0f)
        {
        }

        public CustomLineCap(GraphicsPath? fillPath, GraphicsPath? strokePath, LineCap baseCap, float baseInset)
        {
            BaseCap = baseCap;
            BaseInset = baseInset;
        }

        public LineCap BaseCap { get; set; } = LineCap.Flat;
        public LineJoin StrokeJoin { get; set; } = LineJoin.Miter;
        public float BaseInset { get; set; }
        public float WidthScale { get; set; } = 1f;

        public void GetStrokeCaps(out LineCap startCap, out LineCap endCap)
        {
            startCap = _startCap;
            endCap = _endCap;
        }

        public void SetStrokeCaps(LineCap startCap, LineCap endCap)
        {
            _startCap = startCap;
            _endCap = endCap;
        }

        private LineCap _startCap;
        private LineCap _endCap;

        public virtual object Clone() => MemberwiseClone();

        ~CustomLineCap()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }

    /// <summary>Pattern fill (cf. System.Drawing.Drawing2D.HatchBrush).</summary>
    public sealed class HatchBrush : Brush
    {
        public HatchBrush(HatchStyle style, Color foreColor)
            : this(style, foreColor, Color.FromKnownColor(KnownColor.Transparent))
        {
        }

        public HatchBrush(HatchStyle style, Color foreColor, Color backColor)
        {
            HatchStyle = style;
            ForegroundColor = foreColor;
            BackgroundColor = backColor;
        }

        public HatchStyle HatchStyle { get; }
        public Color ForegroundColor { get; }
        public Color BackgroundColor { get; }

        public override object Clone() => new HatchBrush(HatchStyle, ForegroundColor, BackgroundColor);
    }

    /// <summary>Gradient fill (cf. System.Drawing.Drawing2D.LinearGradientBrush):
    /// colors/angle store; blending rasterizes as solid lerp.</summary>
    public sealed class LinearGradientBrush : Brush
    {
        private Matrix _transform = new();

        public LinearGradientBrush(Point point1, Point point2, Color color1, Color color2)
            : this(new RectangleF(point1.X, point1.Y, point2.X - point1.X, point2.Y - point1.Y),
                  color1, color2, LinearGradientMode.Horizontal)
        {
        }

        public LinearGradientBrush(PointF point1, PointF point2, Color color1, Color color2)
            : this(new RectangleF(point1.X, point1.Y, point2.X - point1.X, point2.Y - point1.Y),
                  color1, color2, LinearGradientMode.Horizontal)
        {
        }

        public LinearGradientBrush(Rectangle rect, Color color1, Color color2, LinearGradientMode mode)
            : this(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), color1, color2, mode)
        {
        }

        public LinearGradientBrush(Rectangle rect, Color color1, Color color2, float angle)
            : this(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), color1, color2, angle, false)
        {
        }

        public LinearGradientBrush(Rectangle rect, Color color1, Color color2, float angle, bool isAngleScaleable)
            : this(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), color1, color2, angle, isAngleScaleable)
        {
        }

        public LinearGradientBrush(RectangleF rect, Color color1, Color color2, LinearGradientMode mode)
        {
            Rectangle = rect;
            LinearColors = [color1, color2];
        }

        public LinearGradientBrush(RectangleF rect, Color color1, Color color2, float angle)
            : this(rect, color1, color2, angle, false)
        {
        }

        public LinearGradientBrush(RectangleF rect, Color color1, Color color2, float angle, bool isAngleScaleable)
        {
            Rectangle = rect;
            LinearColors = [color1, color2];
            _angle = angle;
            _angleScaleable = isAngleScaleable;
        }

        public Color[] LinearColors { get; set; }
        public RectangleF Rectangle { get; }
        public bool GammaCorrection { get; set; }
        public Blend Blend { get; set; } = new();
        public ColorBlend InterpolationColors { get; set; } = new();
        public WrapMode WrapMode { get; set; } = WrapMode.Tile;

        public Matrix Transform
        {
            get => _transform.Clone();
            set => _transform = value?.Clone() ?? new Matrix();
        }

        public override object Clone()
        {
            var clone = new LinearGradientBrush(Rectangle, LinearColors[0], LinearColors[^1], LinearGradientMode.Horizontal)
            {
                GammaCorrection = GammaCorrection,
                WrapMode = WrapMode,
            };
            clone._transform = _transform.Clone();
            return clone;
        }

        public void SetBlendTriangularShape(float focus) => SetBlendTriangularShape(focus, 1f);

        public void SetBlendTriangularShape(float focus, float scale)
        {
            _ = focus;
            _ = scale;
        }

        public void SetSigmaBellShape(float focus) => SetSigmaBellShape(focus, 1f);

        public void SetSigmaBellShape(float focus, float scale)
        {
            _ = focus;
            _ = scale;
        }

        public void ResetTransform() => _transform.Reset();

        public void MultiplyTransform(Matrix matrix) =>
            MultiplyTransform(matrix, MatrixOrder.Prepend);

        public void MultiplyTransform(Matrix matrix, MatrixOrder order)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            _transform.Multiply(matrix, order);
        }

        public void TranslateTransform(float dx, float dy) =>
            TranslateTransform(dx, dy, MatrixOrder.Prepend);

        public void TranslateTransform(float dx, float dy, MatrixOrder order) =>
            _transform.Translate(dx, dy, order);

        public void ScaleTransform(float sx, float sy) =>
            ScaleTransform(sx, sy, MatrixOrder.Prepend);

        public void ScaleTransform(float sx, float sy, MatrixOrder order) =>
            _transform.Scale(sx, sy, order);

        public void RotateTransform(float angle) =>
            RotateTransform(angle, MatrixOrder.Prepend);

        public void RotateTransform(float angle, MatrixOrder order) =>
            _transform.Rotate(angle, order);

        private readonly float _angle;
        private readonly bool _angleScaleable;
    }
}
