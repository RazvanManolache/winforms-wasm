// Affine transforms: exact .NET member shapes, managed math (row-vector
// convention matching GDI+: x' = x*m11 + y*m21 + dx).
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Drawing.Drawing2D
{
    /// <summary>Saved state token (cf. System.Drawing.Drawing2D.GraphicsState).</summary>
    public sealed class GraphicsState
    {
        internal GraphicsState()
        {
        }
    }

    /// <summary>Container token (cf. System.Drawing.Drawing2D.GraphicsContainer).</summary>
    public sealed class GraphicsContainer
    {
        internal GraphicsContainer()
        {
        }
    }

    /// <summary>2D affine matrix (cf. System.Drawing.Drawing2D.Matrix).</summary>
    public sealed class Matrix : IDisposable
    {
        private float _m11 = 1f;
        private float _m12;
        private float _m21;
        private float _m22 = 1f;
        private float _dx;
        private float _dy;

        public Matrix()
        {
        }

        public Matrix(float m11, float m12, float m21, float m22, float dx, float dy)
        {
            _m11 = m11;
            _m12 = m12;
            _m21 = m21;
            _m22 = m22;
            _dx = dx;
            _dy = dy;
        }

        public Matrix(Matrix3x2 matrix)
        {
            _m11 = matrix.M11;
            _m12 = matrix.M12;
            _m21 = matrix.M21;
            _m22 = matrix.M22;
            _dx = matrix.M31;
            _dy = matrix.M32;
        }

        public Matrix(RectangleF rect, PointF[] plgpts)
        {
            ArgumentNullException.ThrowIfNull(plgpts);
            if (plgpts.Length < 3)
                throw new ArgumentException("Three points are required.", nameof(plgpts));
            Fit(rect.X, rect.Y, rect.Width, rect.Height, plgpts[0], plgpts[1], plgpts[2]);
        }

        public Matrix(Rectangle rect, Point[] plgpts)
        {
            ArgumentNullException.ThrowIfNull(plgpts);
            if (plgpts.Length < 3)
                throw new ArgumentException("Three points are required.", nameof(plgpts));
            Fit(rect.X, rect.Y, rect.Width, rect.Height,
                new PointF(plgpts[0].X, plgpts[0].Y),
                new PointF(plgpts[1].X, plgpts[1].Y),
                new PointF(plgpts[2].X, plgpts[2].Y));
        }

        public float[] Elements => [_m11, _m12, _m21, _m22, _dx, _dy];
        public float OffsetX => _dx;
        public float OffsetY => _dy;
        public bool IsIdentity => _m11 == 1f && _m12 == 0f && _m21 == 0f && _m22 == 1f && _dx == 0f && _dy == 0f;
        public bool IsInvertible => (_m11 * _m22 - _m12 * _m21) != 0f;

        // Translation-only test for the ToDevice fast path (the TPF
        // span pattern is translate-there/back around DrawString). No
        // surface change (internal); exact float compare is the guard:
        // a scaled/rotated matrix simply takes the slow path.
        internal bool IsPureTranslation(out float tx, out float ty)
        {
            tx = _dx;
            ty = _dy;
            return _m11 == 1f && _m12 == 0f && _m21 == 0f && _m22 == 1f;
        }

        public Matrix3x2 MatrixElements
        {
            get => new(_m11, _m12, _m21, _m22, _dx, _dy);
            set
            {
                _m11 = value.M11;
                _m12 = value.M12;
                _m21 = value.M21;
                _m22 = value.M22;
                _dx = value.M31;
                _dy = value.M32;
            }
        }

        public Matrix Clone() => new(_m11, _m12, _m21, _m22, _dx, _dy);

        public void Reset()
        {
            _m11 = 1f;
            _m12 = 0f;
            _m21 = 0f;
            _m22 = 1f;
            _dx = 0f;
            _dy = 0f;
        }

        public void Invert()
        {
            float det = (_m11 * _m22) - (_m12 * _m21);
            if (det == 0f)
                throw new ArgumentException("Matrix is not invertible.");
            float m11 = _m22 / det;
            float m12 = -_m12 / det;
            float m21 = -_m21 / det;
            float m22 = _m11 / det;
            float dx = ((_m21 * _dy) - (_m22 * _dx)) / det;
            float dy = ((_m12 * _dx) - (_m11 * _dy)) / det;
            _m11 = m11;
            _m12 = m12;
            _m21 = m21;
            _m22 = m22;
            _dx = dx;
            _dy = dy;
        }

        public void Multiply(Matrix matrix) => Multiply(matrix, MatrixOrder.Prepend);

        public void Multiply(Matrix matrix, MatrixOrder order)
        {
            ArgumentNullException.ThrowIfNull(matrix);
            if (order == MatrixOrder.Prepend)
                Set(MultiplyOf(matrix, this));
            else
                Set(MultiplyOf(this, matrix));
        }

        public void Translate(float offsetX, float offsetY) =>
            Translate(offsetX, offsetY, MatrixOrder.Prepend);

        public void Translate(float offsetX, float offsetY, MatrixOrder order) =>
            Multiply(new Matrix(1f, 0f, 0f, 1f, offsetX, offsetY), order);

        public void Scale(float scaleX, float scaleY) =>
            Scale(scaleX, scaleY, MatrixOrder.Prepend);

        public void Scale(float scaleX, float scaleY, MatrixOrder order) =>
            Multiply(new Matrix(scaleX, 0f, 0f, scaleY, 0f, 0f), order);

        public void Rotate(float angle) => Rotate(angle, MatrixOrder.Prepend);

        public void Rotate(float angle, MatrixOrder order)
        {
            float radians = angle * MathF.PI / 180f;
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            Multiply(new Matrix(cos, sin, -sin, cos, 0f, 0f), order);
        }

        public void RotateAt(float angle, PointF point) =>
            RotateAt(angle, point, MatrixOrder.Prepend);

        public void RotateAt(float angle, PointF point, MatrixOrder order)
        {
            var rotation = new Matrix();
            rotation.Translate(-point.X, -point.Y);
            rotation.Rotate(angle);
            rotation.Translate(point.X, point.Y);
            Multiply(rotation, order);
        }

        public void Shear(float shearX, float shearY) =>
            Shear(shearX, shearY, MatrixOrder.Prepend);

        public void Shear(float shearX, float shearY, MatrixOrder order) =>
            Multiply(new Matrix(1f, shearY, shearX, 1f, 0f, 0f), order);

        public void TransformPoints(Point[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            for (int i = 0; i < pts.Length; i++)
            {
                TransformPoint(pts[i].X, pts[i].Y, out float x, out float y);
                pts[i] = new Point((int)MathF.Round(x), (int)MathF.Round(y));
            }
        }

        public void TransformPoints(PointF[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            for (int i = 0; i < pts.Length; i++)
            {
                TransformPoint(pts[i].X, pts[i].Y, out float x, out float y);
                pts[i] = new PointF(x, y);
            }
        }

        public void TransformPoints(ReadOnlySpan<Point> pts)
        {
            var tmp = pts.ToArray();
            TransformPoints(tmp);
            tmp.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(pts)), tmp.Length));
        }

        public void TransformPoints(ReadOnlySpan<PointF> pts)
        {
            var tmp = pts.ToArray();
            TransformPoints(tmp);
            tmp.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(pts)), tmp.Length));
        }

        public void TransformVectors(Point[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            for (int i = 0; i < pts.Length; i++)
            {
                TransformVector(pts[i].X, pts[i].Y, out float x, out float y);
                pts[i] = new Point((int)MathF.Round(x), (int)MathF.Round(y));
            }
        }

        public void TransformVectors(PointF[] pts)
        {
            ArgumentNullException.ThrowIfNull(pts);
            for (int i = 0; i < pts.Length; i++)
            {
                TransformVector(pts[i].X, pts[i].Y, out float x, out float y);
                pts[i] = new PointF(x, y);
            }
        }

        public void TransformVectors(ReadOnlySpan<Point> pts)
        {
            var tmp = pts.ToArray();
            TransformVectors(tmp);
            tmp.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(pts)), tmp.Length));
        }

        public void TransformVectors(ReadOnlySpan<PointF> pts)
        {
            var tmp = pts.ToArray();
            TransformVectors(tmp);
            tmp.CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in MemoryMarshal.GetReference(pts)), tmp.Length));
        }

        public void VectorTransformPoints(Point[] pts) => TransformVectors(pts);

        public void VectorTransformPoints(ReadOnlySpan<Point> pts) => TransformVectors(pts);

        public override bool Equals(object? obj) => obj is Matrix other
            && _m11 == other._m11 && _m12 == other._m12 && _m21 == other._m21
            && _m22 == other._m22 && _dx == other._dx && _dy == other._dy;

        public override int GetHashCode() =>
            HashCode.Combine(_m11, _m12, _m21, _m22, _dx, _dy);

        ~Matrix()
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

        internal void TransformPoint(float x, float y, out float tx, out float ty)
        {
            tx = (x * _m11) + (y * _m21) + _dx;
            ty = (x * _m12) + (y * _m22) + _dy;
        }

        internal void TransformVector(float x, float y, out float tx, out float ty)
        {
            tx = (x * _m11) + (y * _m21);
            ty = (x * _m12) + (y * _m22);
        }

        private void Fit(float x, float y, float width, float height, PointF p0, PointF p1, PointF p2)
        {
            _m11 = width == 0f ? 0f : (p1.X - p0.X) / width;
            _m12 = width == 0f ? 0f : (p1.Y - p0.Y) / width;
            _m21 = height == 0f ? 0f : (p2.X - p0.X) / height;
            _m22 = height == 0f ? 0f : (p2.Y - p0.Y) / height;
            _dx = p0.X - (_m11 * x) - (_m21 * y);
            _dy = p0.Y - (_m12 * x) - (_m22 * y);
        }

        private void Set(Matrix source)
        {
            _m11 = source._m11;
            _m12 = source._m12;
            _m21 = source._m21;
            _m22 = source._m22;
            _dx = source._dx;
            _dy = source._dy;
        }

        private static Matrix MultiplyOf(Matrix left, Matrix right) => new(
            (left._m11 * right._m11) + (left._m12 * right._m21),
            (left._m11 * right._m12) + (left._m12 * right._m22),
            (left._m21 * right._m11) + (left._m22 * right._m21),
            (left._m21 * right._m12) + (left._m22 * right._m22),
            (left._dx * right._m11) + (left._dy * right._m21) + right._dx,
            (left._dx * right._m12) + (left._dy * right._m22) + right._dy);
    }
}
