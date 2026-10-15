// Path and region model: exact .NET member shapes; curves flatten to
// polylines with managed math (cardinal splines, arc-to-bezier, de Casteljau).
using System.Drawing.Drawing2D;

namespace System.Drawing.Drawing2D
{
    /// <summary>Path snapshot (cf. System.Drawing.Drawing2D.PathData).</summary>
    public sealed class PathData
    {
        public PointF[] Points { get; set; } = [];
        public byte[] Types { get; set; } = [];
    }

    /// <summary>Region blob (cf. System.Drawing.Drawing2D.RegionData):
    /// web-model encoding, not a GDI-compatible blob.</summary>
    public class RegionData
    {
        public RegionData(byte[] data)
        {
            Data = data;
        }

        public byte[] Data { get; set; }
    }

    /// <summary>Figure list path (cf. System.Drawing.Drawing2D.GraphicsPath).</summary>
    public sealed class GraphicsPath : ICloneable, IDisposable
    {
        private const byte Start = 0;
        private const byte Line = 1;
        private const byte Bezier = 3;
        private const byte CloseSubpath = 128;

        private readonly List<Figure> _figures = new();
        private FillMode _fillMode;

        public GraphicsPath()
        {
        }

        public GraphicsPath(FillMode fillMode)
        {
            FillMode = fillMode;
        }

        public GraphicsPath(PointF[] pts, byte[] types)
            : this(pts, types, FillMode.Alternate)
        {
        }

        public GraphicsPath(PointF[] pts, byte[] types, FillMode fillMode)
        {
            ArgumentNullException.ThrowIfNull(pts);
            ArgumentNullException.ThrowIfNull(types);
            if (pts.Length != types.Length)
                throw new ArgumentException("Points and types must have the same length.");
            FillMode = fillMode;
            AppendRaw(pts, types);
        }

        public GraphicsPath(ReadOnlySpan<PointF> pts, ReadOnlySpan<byte> types)
            : this(pts.ToArray(), types.ToArray())
        {
        }

        public GraphicsPath(ReadOnlySpan<PointF> pts, ReadOnlySpan<byte> types, FillMode fillMode)
            : this(pts.ToArray(), types.ToArray(), fillMode)
        {
        }

        public GraphicsPath(Point[] pts, byte[] types)
            : this(pts, types, FillMode.Alternate)
        {
        }

        public GraphicsPath(Point[] pts, byte[] types, FillMode fillMode)
        {
            ArgumentNullException.ThrowIfNull(pts);
            ArgumentNullException.ThrowIfNull(types);
            if (pts.Length != types.Length)
                throw new ArgumentException("Points and types must have the same length.");
            FillMode = fillMode;
            var floats = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                floats[i] = new PointF(pts[i].X, pts[i].Y);
            AppendRaw(floats, types);
        }

        public FillMode FillMode
        {
            get => _fillMode;
            set => _fillMode = value;
        }

        public PathData PathData
        {
            get
            {
                var points = new List<PointF>();
                var types = new List<byte>();
                foreach (Figure figure in _figures)
                {
                    for (int i = 0; i < figure.Points.Count; i++)
                    {
                        points.Add(figure.Points[i]);
                        byte type = figure.Types[i];
                        if (i == figure.Points.Count - 1 && figure.Closed)
                            type |= CloseSubpath;
                        types.Add(type);
                    }
                }

                return new PathData { Points = points.ToArray(), Types = types.ToArray() };
            }
        }

        public PointF[] PathPoints => PathData.Points;
        public byte[] PathTypes => PathData.Types;
        public int PointCount => _figures.Sum(f => f.Points.Count);

        public object Clone()
        {
            var clone = new GraphicsPath(FillMode);
            foreach (Figure figure in _figures)
                clone._figures.Add(figure.Clone());
            return clone;
        }

        public GraphicsPath(ReadOnlySpan<Point> pts, ReadOnlySpan<byte> types, FillMode fillMode)
        {
            var floats = new PointF[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                floats[i] = new PointF(pts[i].X, pts[i].Y);
            var bytes = types.ToArray();
            if (floats.Length != bytes.Length)
                throw new ArgumentException("Points and types must have the same length.");
            FillMode = fillMode;
            AppendRaw(floats, bytes);
        }

        public bool IsVisible(int x, int y) => IsVisible((float)x, (float)y, null);

        public bool IsVisible(int x, int y, Graphics? graphics) => IsVisible((float)x, (float)y, graphics);

        public bool IsVisible(Point point) => IsVisible(new PointF(point.X, point.Y), null);

        public bool IsVisible(Point point, Graphics? graphics) => IsVisible(new PointF(point.X, point.Y), graphics);

        public bool IsOutlineVisible(int x, int y, Pen pen) => IsOutlineVisible((float)x, (float)y, pen);

        public bool IsOutlineVisible(int x, int y, Pen pen, Graphics? graphics) =>
            IsOutlineVisible((float)x, (float)y, pen, graphics);

        public bool IsOutlineVisible(Point point, Pen pen) =>
            IsOutlineVisible(new PointF(point.X, point.Y), pen);

        public bool IsOutlineVisible(Point point, Pen pen, Graphics? graphics) =>
            IsOutlineVisible(new PointF(point.X, point.Y), pen, graphics);

        public int GetPathPoints(Span<PointF> points)
        {
            PointF[] all = PathPoints;
            int n = Math.Min(all.Length, points.Length);
            all.AsSpan(0, n).CopyTo(points);
            return n;
        }

        public int GetPathTypes(Span<byte> types)
        {
            byte[] all = PathTypes;
            int n = Math.Min(all.Length, types.Length);
            all.AsSpan(0, n).CopyTo(types);
            return n;
        }

        public void AddArc(int x, int y, int width, int height, float startAngle, float sweepAngle) =>
            AddArc((float)x, (float)y, (float)width, (float)height, startAngle, sweepAngle);

        public void AddBezier(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4) =>
            AddBezier(new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3), new PointF(x4, y4));

        public void AddBezier(int x1, int y1, int x2, int y2, int x3, int y3, int x4, int y4) =>
            AddBezier(new Point(x1, y1), new Point(x2, y2), new Point(x3, y3), new Point(x4, y4));

        public void AddClosedCurve(ReadOnlySpan<Point> points) => AddClosedCurve(points.ToArray());

        public void AddClosedCurve(ReadOnlySpan<Point> points, float tension) =>
            AddClosedCurve(points.ToArray(), tension);

        public void AddClosedCurve(ReadOnlySpan<PointF> points) => AddClosedCurve(points.ToArray());

        public void AddClosedCurve(ReadOnlySpan<PointF> points, float tension) =>
            AddClosedCurve(points.ToArray(), tension);

        public void AddCurve(ReadOnlySpan<Point> points) => AddCurve(points.ToArray());

        public void AddCurve(ReadOnlySpan<Point> points, float tension) =>
            AddCurve(points.ToArray(), tension);

        public void AddCurve(ReadOnlySpan<PointF> points) => AddCurve(points.ToArray());

        public void AddCurve(ReadOnlySpan<PointF> points, float tension) =>
            AddCurve(points.ToArray(), tension);

        public void AddCurve(Point[] points) => AddCurve(points, 0.5f);

        public void AddEllipse(int x, int y, int width, int height) =>
            AddEllipse((float)x, (float)y, (float)width, (float)height);

        public void AddLine(int x1, int y1, int x2, int y2) =>
            AddLine((float)x1, (float)y1, (float)x2, (float)y2);

        public void AddPie(int x, int y, int width, int height, float startAngle, float sweepAngle) =>
            AddPie((float)x, (float)y, (float)width, (float)height, startAngle, sweepAngle);

        public void AddRoundedRectangle(Rectangle rect, Size size) =>
            AddRoundedRectangle(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height),
                new SizeF(size.Width, size.Height));

        public void AddRoundedRectangle(RectangleF rect, SizeF size)
        {
            float dw = Math.Clamp(size.Width, 0f, rect.Width);
            float dh = Math.Clamp(size.Height, 0f, rect.Height);
            if (dw <= 0f || dh <= 0f)
            {
                AddRectangle(rect);
                return;
            }

            float x = rect.X;
            float y = rect.Y;
            float r = x + rect.Width;
            float b = y + rect.Height;
            StartFigure();
            AddArc(x, y, dw, dh, 180f, 90f);
            AddArc(r - dw, y, dw, dh, 270f, 90f);
            AddArc(r - dw, b - dh, dw, dh, 0f, 90f);
            AddArc(x, b - dh, dw, dh, 90f, 90f);
            CloseFigure();
        }

        public void Warp(ReadOnlySpan<PointF> destPoints, RectangleF srcRect, Matrix? matrix, WarpMode warpMode, float flatness) =>
            Warp(destPoints.ToArray(), srcRect, matrix, warpMode, flatness);

        public void Reset()
        {
            _figures.Clear();
            _fillMode = FillMode.Alternate;
        }

        public void StartFigure() => _figures.Add(new Figure());

        public void CloseFigure()
        {
            if (_figures.Count > 0)
                _figures[^1].Closed = true;
        }

        public void CloseAllFigures()
        {
            foreach (Figure figure in _figures)
                figure.Closed = true;
        }

        internal void AppendSubpath(PointF[] points, byte[] types, bool closed)
        {
            ArgumentNullException.ThrowIfNull(points);
            ArgumentNullException.ThrowIfNull(types);
            if (points.Length != types.Length)
                throw new ArgumentException("Points and types must have the same length.");
            StartFigure();
            Figure figure = _figures[^1];
            figure.Points.AddRange(points);
            figure.Types.AddRange(types);
            figure.Closed = closed;
        }

        public void ClearMarkers()
        {
        }

        public void SetMarkers()
        {
        }

        public void AddLine(Point pt1, Point pt2) =>
            AddLine(new PointF(pt1.X, pt1.Y), new PointF(pt2.X, pt2.Y));

        public void AddLine(float x1, float y1, float x2, float y2) =>
            AddLine(new PointF(x1, y1), new PointF(x2, y2));

        public void AddLine(PointF pt1, PointF pt2)
        {
            Figure figure = OpenFigure();
            if (figure.Points.Count == 0)
            {
                figure.Points.Add(pt1);
                figure.Types.Add(Start);
            }

            figure.Points.Add(pt2);
            figure.Types.Add(Line);
        }

        public void AddLines(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = new PointF(points[i].X, points[i].Y);
            AddLines(floats);
        }

        public void AddLines(PointF[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            for (int i = 0; i + 1 < points.Length; i += 1)
            {
                if (i == 0)
                    AddLine(points[0], points[1]);
                else
                {
                    Figure figure = OpenFigure();
                    figure.Points.Add(points[i + 1]);
                    figure.Types.Add(Line);
                }
            }
        }

        public void AddLines(ReadOnlySpan<Point> points) => AddLines(points.ToArray());
        public void AddLines(ReadOnlySpan<PointF> points) => AddLines(points.ToArray());

        public void AddRectangle(Rectangle rect) =>
            AddRectangle(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void AddRectangle(RectangleF rect)
        {
            StartFigure();
            Figure figure = OpenFigure();
            figure.Points.Add(new PointF(rect.X, rect.Y));
            figure.Types.Add(Start);
            figure.Points.Add(new PointF(rect.Right, rect.Y));
            figure.Types.Add(Line);
            figure.Points.Add(new PointF(rect.Right, rect.Bottom));
            figure.Types.Add(Line);
            figure.Points.Add(new PointF(rect.X, rect.Bottom));
            figure.Types.Add(Line);
            figure.Closed = true;
        }

        public void AddRectangles(Rectangle[] rects)
        {
            ArgumentNullException.ThrowIfNull(rects);
            foreach (Rectangle rect in rects)
                AddRectangle(rect);
        }

        public void AddRectangles(RectangleF[] rects)
        {
            ArgumentNullException.ThrowIfNull(rects);
            foreach (RectangleF rect in rects)
                AddRectangle(rect);
        }

        public void AddRectangles(ReadOnlySpan<Rectangle> rects)
        {
            foreach (Rectangle rect in rects)
                AddRectangle(rect);
        }

        public void AddRectangles(ReadOnlySpan<RectangleF> rects)
        {
            foreach (RectangleF rect in rects)
                AddRectangle(rect);
        }

        public void AddEllipse(Rectangle rect) =>
            AddEllipse(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void AddEllipse(float x, float y, float width, float height) =>
            AddEllipse(new RectangleF(x, y, width, height));

        public void AddEllipse(RectangleF rect)
        {
            const float kappa = 0.5522847498f;
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float rx = rect.Width / 2f;
            float ry = rect.Height / 2f;
            StartFigure();
            Figure figure = OpenFigure();
            AppendBezierPoints(figure,
                new PointF(cx + rx, cy),
                new PointF(cx + rx, cy + kappa * ry),
                new PointF(cx + kappa * rx, cy + ry),
                new PointF(cx, cy + ry));
            AppendBezierRaw(figure,
                new PointF(cx - kappa * rx, cy + ry),
                new PointF(cx - rx, cy + kappa * ry),
                new PointF(cx - rx, cy));
            AppendBezierRaw(figure,
                new PointF(cx - rx, cy - kappa * ry),
                new PointF(cx - kappa * rx, cy - ry),
                new PointF(cx, cy - ry));
            AppendBezierRaw(figure,
                new PointF(cx + kappa * rx, cy - ry),
                new PointF(cx + rx, cy - kappa * ry),
                new PointF(cx + rx, cy));
            figure.Closed = true;
        }

        public void AddArc(Rectangle rect, float startAngle, float sweepAngle) =>
            AddArc(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), startAngle, sweepAngle);

        public void AddArc(RectangleF rect, float startAngle, float sweepAngle) =>
            AddArc(rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

        public void AddArc(float x, float y, float width, float height, float startAngle, float sweepAngle)
        {
            if (sweepAngle == 0f)
                return;
            float cx = x + width / 2f;
            float cy = y + height / 2f;
            float rx = width / 2f;
            float ry = height / 2f;
            float remaining = sweepAngle;
            float current = startAngle;
            bool first = true;
            while (remaining != 0f)
            {
                float chunk = MathF.Sign(remaining) * Math.Min(90f, Math.Abs(remaining));
                float a0 = current * MathF.PI / 180f;
                float a1 = (current + chunk) * MathF.PI / 180f;
                float kappa = 4f / 3f * MathF.Tan((a1 - a0) / 4f);
                var p0 = new PointF(cx + rx * MathF.Cos(a0), cy + ry * MathF.Sin(a0));
                var p1 = new PointF(cx + rx * MathF.Cos(a1), cy + ry * MathF.Sin(a1));
                var c0 = new PointF(p0.X - kappa * rx * MathF.Sin(a0), p0.Y + kappa * ry * MathF.Cos(a0));
                var c1 = new PointF(p1.X + kappa * rx * MathF.Sin(a1), p1.Y - kappa * ry * MathF.Cos(a1));
                Figure figure = OpenFigure();
                if (first && figure.Points.Count == 0)
                {
                    figure.Points.Add(p0);
                    figure.Types.Add(Start);
                    first = false;
                }

                figure.Points.Add(c0);
                figure.Types.Add(Bezier);
                figure.Points.Add(c1);
                figure.Types.Add(Bezier);
                figure.Points.Add(p1);
                figure.Types.Add(Bezier);
                remaining -= chunk;
                current += chunk;
            }
        }

        public void AddPie(Rectangle rect, float startAngle, float sweepAngle) =>
            AddPie(rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

        public void AddPie(float x, float y, float width, float height, float startAngle, float sweepAngle)
        {
            if (sweepAngle == 0f)
                return;
            var center = new PointF(x + width / 2f, y + height / 2f);
            float a0 = startAngle * MathF.PI / 180f;
            var start = new PointF(
                center.X + (width / 2f) * MathF.Cos(a0),
                center.Y + (height / 2f) * MathF.Sin(a0));
            StartFigure();
            Figure figure = OpenFigure();
            figure.Points.Add(center);
            figure.Types.Add(Start);
            figure.Points.Add(start);
            figure.Types.Add(Line);
            AddArc(x, y, width, height, startAngle, sweepAngle);
            figure.Points.Add(center);
            figure.Types.Add(Line);
            figure.Closed = true;
        }

        public void AddPolygon(PointF[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            AddLines(points);
            CloseFigure();
        }

        public void AddPolygon(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = new PointF(points[i].X, points[i].Y);
            AddPolygon(floats);
        }

        public void AddPolygon(ReadOnlySpan<PointF> points) => AddPolygon(points.ToArray());
        public void AddPolygon(ReadOnlySpan<Point> points) => AddPolygon(points.ToArray());

        public void AddBezier(Point pt1, Point pt2, Point pt3, Point pt4) =>
            AddBezier(ToF(pt1), ToF(pt2), ToF(pt3), ToF(pt4));

        public void AddBezier(PointF pt1, PointF pt2, PointF pt3, PointF pt4)
        {
            Figure figure = OpenFigure();
            if (figure.Points.Count == 0)
            {
                figure.Points.Add(pt1);
                figure.Types.Add(Start);
            }

            AppendBezierRaw(figure, pt2, pt3, pt4);
        }

        public void AddBeziers(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = ToF(points[i]);
            AddBeziers(floats);
        }

        public void AddBeziers(PointF[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            for (int i = 0; i + 3 < points.Length; i += 3)
            {
                if (i == 0)
                    AddBezier(points[0], points[1], points[2], points[3]);
                else
                {
                    Figure figure = OpenFigure();
                    AppendBezierRaw(figure, points[i + 1], points[i + 2], points[i + 3]);
                }
            }
        }

        public void AddBeziers(ReadOnlySpan<Point> points) => AddBeziers(points.ToArray());
        public void AddBeziers(ReadOnlySpan<PointF> points) => AddBeziers(points.ToArray());

        public void AddCurve(PointF[] points) => AddCurve(points, 0.5f);

        public void AddCurve(PointF[] points, float tension)
        {
            ArgumentNullException.ThrowIfNull(points);
            AddCurve(points, 0, points.Length - 1, tension);
        }

        public void AddCurve(PointF[] points, int offset, int numberOfSegments, float tension)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Length == 0)
                return;
            var spline = CardinalToBeziers(points, offset, numberOfSegments, tension, false);
            AppendBezierChain(spline);
        }

        public void AddCurve(Point[] points, int offset, int numberOfSegments, float tension)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = ToF(points[i]);
            AddCurve(floats, offset, numberOfSegments, tension);
        }

        public void AddCurve(Point[] points, float tension)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = ToF(points[i]);
            AddCurve(floats, tension);
        }

        public void AddClosedCurve(PointF[] points) => AddClosedCurve(points, 0.5f);

        public void AddClosedCurve(PointF[] points, float tension)
        {
            ArgumentNullException.ThrowIfNull(points);
            var spline = CardinalToBeziers(points, 0, points.Length, tension, true);
            AppendBezierChain(spline);
            CloseFigure();
        }

        public void AddClosedCurve(Point[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = ToF(points[i]);
            AddClosedCurve(floats);
        }

        public void AddClosedCurve(Point[] points, float tension)
        {
            ArgumentNullException.ThrowIfNull(points);
            var floats = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                floats[i] = ToF(points[i]);
            AddClosedCurve(floats, tension);
        }

        public void AddPath(GraphicsPath? addingPath, bool connect)
        {
            ArgumentNullException.ThrowIfNull(addingPath);
            PathData data = addingPath.PathData;
            if (data.Points.Length == 0)
                return;
            if (!connect)
                StartFigure();
            Figure figure = OpenFigure();
            for (int i = 0; i < data.Points.Length; i++)
            {
                byte type = (byte)(data.Types[i] & 7);
                if (figure.Points.Count == 0 && type != Start)
                    type = Start;
                figure.Points.Add(data.Points[i]);
                figure.Types.Add(type);
            }

            if ((data.Types[^1] & CloseSubpath) != 0)
                figure.Closed = true;
        }

        public void AddString(string s, FontFamily family, int style, float emSize, Point origin, StringFormat format) =>
            throw new NotSupportedException("Text outlines arrive with the text-rasterization milestone.");

        public void AddString(string s, FontFamily family, int style, float emSize, PointF origin, StringFormat format) =>
            throw new NotSupportedException("Text outlines arrive with the text-rasterization milestone.");

        public void AddString(string s, FontFamily family, int style, float emSize, Rectangle layoutRect, StringFormat format) =>
            throw new NotSupportedException("Text outlines arrive with the text-rasterization milestone.");

        public void AddString(string s, FontFamily family, int style, float emSize, RectangleF layoutRect, StringFormat format) =>
            throw new NotSupportedException("Text outlines arrive with the text-rasterization milestone.");

        public void Transform(Matrix matrix)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            foreach (Figure figure in _figures)
            {
                for (int i = 0; i < figure.Points.Count; i++)
                {
                    matrix.TransformPoint(figure.Points[i].X, figure.Points[i].Y, out float x, out float y);
                    figure.Points[i] = new PointF(x, y);
                }
            }
        }

        public void Flatten() => Flatten(null, 0.25f);

        public void Flatten(Matrix? matrix) => Flatten(matrix, 0.25f);

        public void Flatten(Matrix? matrix, float flatness)
        {
            var flat = new List<Figure>();
            foreach (Figure figure in _figures)
            {
                var lines = new Figure { Closed = figure.Closed };
                FlattenFigure(figure, matrix, Math.Max(0.01f, flatness), lines);
                if (lines.Points.Count > 0)
                    flat.Add(lines);
            }

            _figures.Clear();
            _figures.AddRange(flat);
        }

        public RectangleF GetBounds() => GetBounds(null, null);

        public RectangleF GetBounds(Matrix? matrix) => GetBounds(matrix, null);

        public RectangleF GetBounds(Matrix? matrix, Pen? pen)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (PointF pt in Flattened(matrix))
            {
                minX = Math.Min(minX, pt.X);
                minY = Math.Min(minY, pt.Y);
                maxX = Math.Max(maxX, pt.X);
                maxY = Math.Max(maxY, pt.Y);
            }

            if (minX == float.MaxValue)
                return new RectangleF(0, 0, 0, 0);
            float inflate = pen is null ? 0f : Math.Max(1f, pen.Width / 2f);
            return new RectangleF(minX - inflate, minY - inflate, (maxX - minX) + (inflate * 2f), (maxY - minY) + (inflate * 2f));
        }

        public PointF GetLastPoint()
        {
            if (_figures.Count == 0 || _figures[^1].Points.Count == 0)
                return PointF.Empty;
            return _figures[^1].Points[^1];
        }

        public bool IsVisible(float x, float y) => IsVisible(new PointF(x, y), null);

        public bool IsVisible(PointF point) => IsVisible(point, null);

        public bool IsVisible(float x, float y, Graphics? graphics) => IsVisible(new PointF(x, y), graphics);

        public bool IsVisible(PointF point, Graphics? graphics)
        {
            List<PointF> flat = Flattened(null);
            if (flat.Count < 3)
                return false;
            bool inside = false;
            if (_fillMode == FillMode.Alternate)
            {
                for (int i = 0, j = flat.Count - 1; i < flat.Count; j = i++)
                {
                    if ((flat[i].Y > point.Y) != (flat[j].Y > point.Y)
                        && point.X < ((flat[j].X - flat[i].X) * (point.Y - flat[i].Y) / (flat[j].Y - flat[i].Y)) + flat[i].X)
                        inside = !inside;
                }

                return inside;
            }

            int winding = 0;
            for (int i = 0, j = flat.Count - 1; i < flat.Count; j = i++)
            {
                if (flat[i].Y <= point.Y)
                {
                    if (flat[j].Y > point.Y && IsLeft(flat[i], flat[j], point) > 0)
                        winding++;
                }
                else if (flat[j].Y <= point.Y && IsLeft(flat[i], flat[j], point) < 0)
                {
                    winding--;
                }
            }

            return winding != 0;
        }

        public bool IsOutlineVisible(float x, float y, Pen pen) =>
            IsOutlineVisible(new PointF(x, y), pen, null);

        public bool IsOutlineVisible(PointF point, Pen pen) =>
            IsOutlineVisible(point, pen, null);

        public bool IsOutlineVisible(float x, float y, Pen pen, Graphics? graphics) =>
            IsOutlineVisible(new PointF(x, y), pen, graphics);

        public bool IsOutlineVisible(PointF point, Pen? pen, Graphics? graphics)
        {
            float half = pen is null ? 0.5f : Math.Max(0.5f, pen.Width / 2f);
            List<PointF> flat = Flattened(null);
            for (int i = 0; i + 1 < flat.Count; i++)
            {
                if (DistanceToSegment(point, flat[i], flat[i + 1]) <= half)
                    return true;
            }

            return false;
        }

        public void Reverse()
        {
            foreach (Figure figure in _figures)
            {
                figure.Points.Reverse();
                figure.Types.Reverse();
                for (int i = 0; i < figure.Types.Count; i++)
                    figure.Types[i] = (byte)(figure.Types[i] & 7);
                if (figure.Types.Count > 0)
                    figure.Types[0] = Start;
            }

            _figures.Reverse();
        }

        public void Warp(PointF[] destPoints, RectangleF srcRect) =>
            throw new NotSupportedException("Path warping arrives with the warp milestone.");

        public void Warp(PointF[] destPoints, RectangleF srcRect, Matrix? matrix) =>
            throw new NotSupportedException("Path warping arrives with the warp milestone.");

        public void Warp(PointF[] destPoints, RectangleF srcRect, Matrix? matrix, WarpMode warpMode) =>
            throw new NotSupportedException("Path warping arrives with the warp milestone.");

        public void Warp(PointF[] destPoints, RectangleF srcRect, Matrix? matrix, WarpMode warpMode, float flatness) =>
            throw new NotSupportedException("Path warping arrives with the warp milestone.");

        public void Widen(Pen pen) =>
            throw new NotSupportedException("Path widening arrives with the widen milestone.");

        public void Widen(Pen pen, Matrix? matrix) =>
            throw new NotSupportedException("Path widening arrives with the widen milestone.");

        public void Widen(Pen pen, Matrix? matrix, float flatness) =>
            throw new NotSupportedException("Path widening arrives with the widen milestone.");

        ~GraphicsPath()
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

        internal List<PointF> Flattened(Matrix? matrix)
        {
            var flat = new List<Figure>();
            foreach (Figure figure in _figures)
            {
                var lines = new Figure();
                FlattenFigure(figure, matrix, 0.25f, lines);
                flat.Add(lines);
            }

            var points = new List<PointF>();
            foreach (Figure figure in flat)
                points.AddRange(figure.Points);
            return points;
        }

        private Figure OpenFigure()
        {
            if (_figures.Count == 0 || IsFigureComplete(_figures[^1]))
                _figures.Add(new Figure());
            return _figures[^1];
        }

        private static bool IsFigureComplete(Figure figure) =>
            figure.Closed || figure.Points.Count >= 2 && figure.Types.Count > 0 && figure.Types[^1] == CloseSubpath;

        private void AppendRaw(PointF[] points, byte[] types)
        {
            var figure = new Figure();
            figure.Points.AddRange(points);
            figure.Types.AddRange(types);
            _figures.Add(figure);
        }

        private void AppendBezierPoints(Figure figure, PointF p0, PointF c0, PointF c1, PointF p1)
        {
            figure.Points.Add(p0);
            figure.Types.Add(Start);
            AppendBezierRaw(figure, c0, c1, p1);
        }

        private static void AppendBezierRaw(Figure figure, PointF c0, PointF c1, PointF p1)
        {
            figure.Points.Add(c0);
            figure.Types.Add(Bezier);
            figure.Points.Add(c1);
            figure.Types.Add(Bezier);
            figure.Points.Add(p1);
            figure.Types.Add(Bezier);
        }

        private void AppendBezierChain(List<PointF> chain)
        {
            if (chain.Count == 0)
                return;
            Figure figure = OpenFigure();
            int start = 0;
            if (figure.Points.Count == 0)
            {
                figure.Points.Add(chain[0]);
                figure.Types.Add(Start);
                start = 1;
            }

            for (int i = start; i < chain.Count; i++)
            {
                figure.Points.Add(chain[i]);
                figure.Types.Add(Bezier);
            }
        }

        private static List<PointF> CardinalToBeziers(PointF[] points, int offset, int count, float tension, bool closed)
        {
            var chain = new List<PointF>();
            int n = closed ? points.Length : Math.Min(points.Length, offset + count + 1);
            if (n < 2)
                return chain;
            chain.Add(points[closed ? 0 : offset]);
            int segments = closed ? n : count;
            for (int i = 0; i < segments; i++)
            {
                PointF p0 = points[closed ? ((offset + i - 1 + n) % n) : Math.Max(offset, offset + i - 1)];
                PointF p1 = points[closed ? ((offset + i) % n) : offset + i];
                PointF p2 = points[closed ? ((offset + i + 1) % n) : Math.Min(n - 1, offset + i + 1)];
                PointF p3 = points[closed ? ((offset + i + 2) % n) : Math.Min(n - 1, offset + i + 2)];
                float t = tension / 3f;
                chain.Add(new PointF(p1.X + ((p2.X - p0.X) * t), p1.Y + ((p2.Y - p0.Y) * t)));
                chain.Add(new PointF(p2.X - ((p3.X - p1.X) * t), p2.Y - ((p3.Y - p1.Y) * t)));
                chain.Add(p2);
            }

            return chain;
        }

        private static void FlattenFigure(Figure figure, Matrix? matrix, float flatness, Figure lines)
        {
            var run = new List<PointF>();
            var runTypes = new List<byte>();
            for (int i = 0; i < figure.Points.Count; i++)
            {
                byte type = (byte)(figure.Types[i] & 7);
                if (type == Bezier && run.Count >= 1)
                {
                    if (i + 2 >= figure.Points.Count)
                        break;
                    Subdivide(run[^1], figure.Points[i], figure.Points[i + 1], figure.Points[i + 2], flatness, run);
                    run.Add(figure.Points[i + 2]);
                    runTypes.Add(Line);
                    runTypes.Add(Line);
                    runTypes.Add(Line);
                    i += 2;
                }
                else
                {
                    run.Add(figure.Points[i]);
                    runTypes.Add(run.Count == 1 ? Start : Line);
                }
            }

            foreach (PointF pt in run)
            {
                if (matrix is null)
                    lines.Points.Add(pt);
                else
                {
                    matrix.TransformPoint(pt.X, pt.Y, out float x, out float y);
                    lines.Points.Add(new PointF(x, y));
                }
            }

            lines.Types.AddRange(runTypes);
            lines.Closed = figure.Closed;
            if (figure.Closed && lines.Points.Count > 0)
            {
                lines.Points.Add(lines.Points[0]);
                lines.Types.Add(Line);
            }
        }

        private static void Subdivide(PointF p0, PointF p1, PointF p2, PointF p3, float flatness, List<PointF> output)
        {
            float dx = p3.X - p0.X;
            float dy = p3.Y - p0.Y;
            float len = MathF.Sqrt((dx * dx) + (dy * dy));
            float d1 = len == 0f ? 0f : Math.Abs(((p1.X - p3.X) * dy - ((p1.Y - p3.Y) * dx)) / len);
            float d2 = len == 0f ? 0f : Math.Abs(((p2.X - p3.X) * dy - ((p2.Y - p3.Y) * dx)) / len);
            if (Math.Max(d1, d2) <= flatness)
                return;
            var m01 = Mid(p0, p1);
            var m12 = Mid(p1, p2);
            var m23 = Mid(p2, p3);
            var m012 = Mid(m01, m12);
            var m123 = Mid(m12, m23);
            var mid = Mid(m012, m123);
            Subdivide(p0, m01, m012, mid, flatness, output);
            output.Add(mid);
            Subdivide(mid, m123, m23, p3, flatness, output);
        }

        private static PointF Mid(PointF a, PointF b) => new((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
        private static PointF ToF(Point p) => new(p.X, p.Y);

        private static float IsLeft(PointF a, PointF b, PointF p) =>
            ((b.X - a.X) * (p.Y - a.Y)) - ((p.X - a.X) * (b.Y - a.Y));

        private static float DistanceToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float denom = (dx * dx) + (dy * dy);
            float t = denom == 0f ? 0f : (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / denom;
            t = Math.Clamp(t, 0f, 1f);
            float px = a.X + (t * dx);
            float py = a.Y + (t * dy);
            return MathF.Sqrt(((p.X - px) * (p.X - px)) + ((p.Y - py) * (p.Y - py)));
        }

        private sealed class Figure
        {
            public readonly List<PointF> Points = new();
            public readonly List<byte> Types = new();
            public bool Closed;

            public Figure Clone()
            {
                var clone = new Figure { Closed = Closed };
                clone.Points.AddRange(Points);
                clone.Types.AddRange(Types);
                return clone;
            }
        }
    }

}

namespace System.Drawing
{
    /// <summary>Fill region (cf. System.Drawing.Region): rectangle-list model;
    /// path inputs contribute their bounds until exact scan conversion arrives.</summary>
    public sealed class Region : IDisposable
    {
        private List<RectangleF> _rects = new();
        private bool _infinite;

        public Region()
        {
            _infinite = true;
        }

        public Region(RectangleF rect)
        {
            _rects.Add(rect);
        }

        public Region(Rectangle rect)
            : this(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height))
        {
        }

        public Region(GraphicsPath path)
            : this(path?.GetBounds() ?? new RectangleF())
        {
        }

        public Region(RegionData rgnData)
        {
            ArgumentNullException.ThrowIfNull(rgnData);
            ReadBlob(rgnData.Data);
        }

        public Region Clone()
        {
            var clone = new Region();
            clone._rects = new List<RectangleF>(_rects);
            clone._infinite = _infinite;
            return clone;
        }

        public void MakeEmpty()
        {
            _rects.Clear();
            _infinite = false;
        }

        public void MakeInfinite()
        {
            _rects.Clear();
            _infinite = true;
        }

        public bool IsEmpty(Graphics? g) => !_infinite && _rects.Count == 0;
        public bool IsInfinite(Graphics? g) => _infinite;

        public RectangleF GetBounds(Graphics? g)
        {
            if (_infinite || _rects.Count == 0)
                return new RectangleF(0, 0, 0, 0);
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (RectangleF rect in _rects)
            {
                minX = Math.Min(minX, rect.X);
                minY = Math.Min(minY, rect.Y);
                maxX = Math.Max(maxX, rect.Right);
                maxY = Math.Max(maxY, rect.Bottom);
            }

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public RectangleF[] GetRegionScans(Matrix matrix)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            var scans = new RectangleF[_rects.Count];
            for (int i = 0; i < _rects.Count; i++)
            {
                RectangleF rect = _rects[i];
                var corners = new PointF[]
                {
                    new(rect.X, rect.Y),
                    new(rect.Right, rect.Y),
                    new(rect.Right, rect.Bottom),
                    new(rect.X, rect.Bottom),
                };
                matrix.TransformPoints(corners);
                float minX = corners.Min(p => p.X);
                float minY = corners.Min(p => p.Y);
                scans[i] = new RectangleF(minX, minY, corners.Max(p => p.X) - minX, corners.Max(p => p.Y) - minY);
            }

            return scans;
        }

        public RegionData GetRegionData()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(_infinite);
            writer.Write(_rects.Count);
            foreach (RectangleF rect in _rects)
            {
                writer.Write(rect.X);
                writer.Write(rect.Y);
                writer.Write(rect.Width);
                writer.Write(rect.Height);
            }

            return new RegionData(stream.ToArray());
        }

        public void Intersect(Rectangle rect) => Intersect(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void Intersect(RectangleF rect)
        {
            if (_infinite)
            {
                _infinite = false;
                _rects.Add(rect);
                return;
            }

            var next = new List<RectangleF>();
            foreach (RectangleF own in _rects)
            {
                RectangleF hit = RectangleF.Intersect(own, rect);
                if (!hit.IsEmpty)
                    next.Add(hit);
            }

            _rects = next;
        }

        public void Intersect(GraphicsPath path) => Intersect(path?.GetBounds() ?? new RectangleF());
        public void Intersect(Region region) => IntersectRegion(region, static (a, b) => IntersectPair(a, b));

        public void Union(Rectangle rect) => Union(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void Union(RectangleF rect)
        {
            if (_infinite)
                return;
            _rects.Add(rect);
            Normalize();
        }

        public void Union(GraphicsPath path) => Union(path?.GetBounds() ?? new RectangleF());
        public void Union(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);
            if (region._infinite)
            {
                MakeInfinite();
                return;
            }

            _rects.AddRange(region._rects);
            Normalize();
        }

        public void Xor(Rectangle rect) => Xor(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void Xor(RectangleF rect)
        {
            var overlap = new List<RectangleF>();
            var kept = new List<RectangleF>();
            foreach (RectangleF own in _rects)
            {
                RectangleF hit = RectangleF.Intersect(own, rect);
                if (hit.IsEmpty)
                    kept.Add(own);
                else
                {
                    kept.AddRange(Subtract(own, hit));
                    overlap.Add(hit);
                }
            }

            kept.AddRange(Subtract(rect, overlap));
            _rects = kept;
            _infinite = false;
            Normalize();
        }

        public void Xor(GraphicsPath path) => Xor(path?.GetBounds() ?? new RectangleF());
        public void Xor(Region region) => XorRegion(region);

        public void Exclude(Rectangle rect) => Exclude(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void Exclude(RectangleF rect)
        {
            var next = new List<RectangleF>();
            foreach (RectangleF own in _rects)
                next.AddRange(Subtract(own, rect));
            _rects = next;
            _infinite = false;
        }

        public void Exclude(GraphicsPath path) => Exclude(path?.GetBounds() ?? new RectangleF());
        public void Exclude(Region region) => ExcludeRegion(region);

        public void Complement(Rectangle rect) => Complement(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));

        public void Complement(RectangleF rect)
        {
            var next = Subtract(rect, _rects);
            _rects = next;
            _infinite = false;
        }

        public void Complement(GraphicsPath path) => Complement(path?.GetBounds() ?? new RectangleF());
        public void Complement(Region region) => ComplementRegion(region);

        public void Translate(float dx, float dy)
        {
            for (int i = 0; i < _rects.Count; i++)
            {
                RectangleF rect = _rects[i];
                _rects[i] = new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
            }
        }

        public void Translate(int dx, int dy) => Translate((float)dx, (float)dy);

        public void Transform(Matrix matrix)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            for (int i = 0; i < _rects.Count; i++)
            {
                RectangleF rect = _rects[i];
                var corners = new PointF[]
                {
                    new(rect.X, rect.Y),
                    new(rect.Right, rect.Y),
                    new(rect.Right, rect.Bottom),
                    new(rect.X, rect.Bottom),
                };
                matrix.TransformPoints(corners);
                float minX = corners.Min(p => p.X);
                float minY = corners.Min(p => p.Y);
                _rects[i] = new RectangleF(minX, minY, corners.Max(p => p.X) - minX, corners.Max(p => p.Y) - minY);
            }
        }

        public bool IsVisible(float x, float y) => ContainsPoint(x, y);
        public bool IsVisible(PointF point) => ContainsPoint(point.X, point.Y);
        public bool IsVisible(float x, float y, Graphics? graphics) => ContainsPoint(x, y);
        public bool IsVisible(PointF point, Graphics? graphics) => ContainsPoint(point.X, point.Y);
        public bool IsVisible(float x, float y, int width, int height) => Intersects(new RectangleF(x, y, width, height));
        public bool IsVisible(Rectangle rect) => Intersects(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height));
        public bool IsVisible(Rectangle rect, Graphics? graphics) => IsVisible(rect);
        public bool IsVisible(float x, float y, int width, int height, Graphics? graphics) => IsVisible(x, y, width, height);
        public bool IsVisible(RectangleF rect) => Intersects(rect);
        public bool IsVisible(RectangleF rect, Graphics? graphics) => Intersects(rect);

        public bool IsVisible(int x, int y) => ContainsPoint(x, y);
        public bool IsVisible(int x, int y, Graphics? graphics) => ContainsPoint(x, y);
        public bool IsVisible(Point point) => ContainsPoint(point.X, point.Y);
        public bool IsVisible(Point point, Graphics? graphics) => ContainsPoint(point.X, point.Y);
        public bool IsVisible(int x, int y, int width, int height) =>
            Intersects(new RectangleF(x, y, width, height));
        public bool IsVisible(int x, int y, int width, int height, Graphics? graphics) =>
            IsVisible(x, y, width, height);
        public bool IsVisible(float x, float y, float width, float height) =>
            Intersects(new RectangleF(x, y, width, height));
        public bool IsVisible(float x, float y, float width, float height, Graphics? graphics) =>
            IsVisible(x, y, width, height);

        public bool Equals(Region? region, Graphics? g)
        {
            if (region is null)
                return false;
            if (_infinite || region._infinite)
                return _infinite && region._infinite;
            if (_rects.Count != region._rects.Count)
                return false;
            for (int i = 0; i < _rects.Count; i++)
            {
                if (!_rects[i].Equals(region._rects[i]))
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_infinite);
            foreach (RectangleF rect in _rects)
                hash.Add(rect);
            return hash.ToHashCode();
        }

        public IntPtr GetHrgn(Graphics? g) => IntPtr.Zero;

        public static Region FromHrgn(IntPtr hrgn) =>
            throw new NotSupportedException("GDI handles are not available on web.");

        public void ReleaseHrgn(IntPtr regionHandle)
        {
        }

        ~Region()
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

        private bool ContainsPoint(float x, float y)
        {
            if (_infinite)
                return true;
            foreach (RectangleF rect in _rects)
            {
                if (rect.Contains(x, y))
                    return true;
            }

            return false;
        }

        // Hot-loop clip fast path: when the region is one integer rect
        // (the shape region-repaint SetClip calls produce), callers can
        // intersect their integer loop bounds once instead of paying a
        // per-pixel IsVisible. Bounds are half-open [x0,x1)x[y0,y1) to
        // match RectangleF.Contains exactly (X<=p<X+W); anything else
        // (infinite, empty, multi-rect, fractional) returns false and
        // the caller keeps per-pixel checks.
        internal bool TryGetIntRect(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            if (_infinite || _rects.Count != 1)
                return false;
            RectangleF r = _rects[0];
            if (r.X != (int)r.X || r.Y != (int)r.Y || r.Width != (int)r.Width || r.Height != (int)r.Height)
                return false;
            x0 = (int)r.X;
            y0 = (int)r.Y;
            x1 = x0 + (int)r.Width;
            y1 = y0 + (int)r.Height;
            return x1 > x0 && y1 > y0;
        }

        private bool Intersects(RectangleF rect)
        {
            if (_infinite)
                return true;
            foreach (RectangleF own in _rects)
            {
                if (!RectangleF.Intersect(own, rect).IsEmpty)
                    return true;
            }

            return false;
        }

        private void Normalize()
        {
            _rects.RemoveAll(r => r.IsEmpty);
        }

        private void IntersectRegion(Region region, Func<RectangleF, RectangleF, List<RectangleF>> op)
        {
            ArgumentNullException.ThrowIfNull(region);
            if (region._infinite)
                return;
            if (_infinite)
            {
                _infinite = false;
                _rects = new List<RectangleF>(region._rects);
                return;
            }

            var next = new List<RectangleF>();
            foreach (RectangleF own in _rects)
                foreach (RectangleF other in region._rects)
                    next.AddRange(op(own, other));
            _rects = next;
            Normalize();
        }

        private static List<RectangleF> IntersectPair(RectangleF a, RectangleF b)
        {
            RectangleF hit = RectangleF.Intersect(a, b);
            return hit.IsEmpty ? [] : [hit];
        }

        private void XorRegion(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);
            var overlap = new List<RectangleF>();
            var kept = new List<RectangleF>();
            foreach (RectangleF own in _rects)
            {
                var hits = new List<RectangleF>();
                foreach (RectangleF other in region._rects)
                {
                    RectangleF hit = RectangleF.Intersect(own, other);
                    if (!hit.IsEmpty)
                        hits.Add(hit);
                }

                if (hits.Count == 0)
                {
                    kept.Add(own);
                }
                else
                {
                    var rest = new List<RectangleF> { own };
                    foreach (RectangleF hit in hits)
                    {
                        var cut = new List<RectangleF>();
                        foreach (RectangleF piece in rest)
                            cut.AddRange(Subtract(piece, hit));
                        rest = cut;
                    }

                    kept.AddRange(rest);
                    overlap.AddRange(hits);
                }
            }

            foreach (RectangleF other in region._rects)
                kept.AddRange(Subtract(other, overlap));
            _rects = kept;
            _infinite = false;
            Normalize();
        }

        private void ExcludeRegion(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);
            var next = new List<RectangleF>();
            foreach (RectangleF own in _rects)
            {
                var rest = new List<RectangleF> { own };
                foreach (RectangleF other in region._rects)
                {
                    var cut = new List<RectangleF>();
                    foreach (RectangleF piece in rest)
                        cut.AddRange(Subtract(piece, other));
                    rest = cut;
                }

                next.AddRange(rest);
            }

            _rects = next;
            _infinite = false;
        }

        private void ComplementRegion(Region region)
        {
            ArgumentNullException.ThrowIfNull(region);
            var next = new List<RectangleF>(region._rects);
            foreach (RectangleF own in _rects)
            {
                var cut = new List<RectangleF>();
                foreach (RectangleF piece in next)
                    cut.AddRange(Subtract(piece, own));
                next = cut;
            }

            _rects = next;
            _infinite = false;
        }

        private static List<RectangleF> Subtract(RectangleF source, RectangleF cut)
        {
            RectangleF hit = RectangleF.Intersect(source, cut);
            if (hit.IsEmpty)
                return [source];
            var parts = new List<RectangleF>();
            if (hit.Y > source.Y)
                parts.Add(new RectangleF(source.X, source.Y, source.Width, hit.Y - source.Y));
            if (hit.Bottom < source.Bottom)
                parts.Add(new RectangleF(source.X, hit.Bottom, source.Width, source.Bottom - hit.Bottom));
            if (hit.X > source.X)
                parts.Add(new RectangleF(source.X, hit.Y, hit.X - source.X, hit.Height));
            if (hit.Right < source.Right)
                parts.Add(new RectangleF(hit.Right, hit.Y, source.Right - hit.Right, hit.Height));
            return parts;
        }

        private static List<RectangleF> Subtract(RectangleF source, List<RectangleF> cuts)
        {
            var rest = new List<RectangleF> { source };
            foreach (RectangleF cut in cuts)
            {
                var next = new List<RectangleF>();
                foreach (RectangleF piece in rest)
                    next.AddRange(Subtract(piece, cut));
                rest = next;
            }

            return rest;
        }

        private void ReadBlob(byte[] data)
        {
            _rects.Clear();
            _infinite = false;
            try
            {
                using var stream = new MemoryStream(data);
                using var reader = new BinaryReader(stream);
                _infinite = reader.ReadBoolean();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                    _rects.Add(new RectangleF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
            }
            catch (EndOfStreamException)
            {
            }
        }
    }
}
