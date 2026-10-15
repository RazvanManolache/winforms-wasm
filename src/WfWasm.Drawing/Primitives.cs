// Hand-written core primitives: exact .NET behavior, web-compatible
// (no native calls). Signatures are verified by the ApiScan parity gate.
using System.ComponentModel;
using System.Numerics;

namespace System.Drawing;

[TypeConverter(typeof(PointConverter))]
public struct Point : IEquatable<Point>
{
    private int _x;
    private int _y;

    public static readonly Point Empty;

    public Point(int x, int y)
    {
        _x = x;
        _y = y;
    }

    public Point(Size sz)
    {
        _x = sz.Width;
        _y = sz.Height;
    }

    public Point(int dw)
    {
        _x = (short)(dw & 0xFFFF);
        _y = (short)((dw >> 16) & 0xFFFF);
    }

    public bool IsEmpty => _x == 0 && _y == 0;
    public int X { readonly get => _x; set => _x = value; }
    public int Y { readonly get => _y; set => _y = value; }

    public void Offset(int dx, int dy)
    {
        unchecked
        {
            X += dx;
            Y += dy;
        }
    }

    public void Offset(Point p) => Offset(p.X, p.Y);

    public static Point Add(Point pt, Size sz) => new(pt.X + sz.Width, pt.Y + sz.Height);
    public static Point Subtract(Point pt, Size sz) => new(pt.X - sz.Width, pt.Y - sz.Height);
    public static Point Ceiling(PointF value) => new((int)Math.Ceiling(value.X), (int)Math.Ceiling(value.Y));
    public static Point Truncate(PointF value) => new((int)value.X, (int)value.Y);
    public static Point Round(PointF value) => new((int)Math.Round(value.X), (int)Math.Round(value.Y));

    public static Point operator +(Point pt, Size sz) => Add(pt, sz);
    public static Point operator -(Point pt, Size sz) => Subtract(pt, sz);
    public static bool operator ==(Point left, Point right) => left.Equals(right);
    public static bool operator !=(Point left, Point right) => !left.Equals(right);
    public static implicit operator PointF(Point p) => new(p.X, p.Y);
    public static explicit operator Size(Point p) => new(p);

    public readonly bool Equals(Point other) => _x == other._x && _y == other._y;
    public override readonly bool Equals(object? obj) => obj is Point other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_x, _y);
    public override readonly string ToString() => $"{{X={_x},Y={_y}}}";
}

[TypeConverter(typeof(PointFConverter))]
public struct PointF : IEquatable<PointF>
{
    private float _x;
    private float _y;

    public static readonly PointF Empty;

    public PointF(float x, float y)
    {
        _x = x;
        _y = y;
    }

    public PointF(Vector2 vector)
    {
        _x = vector.X;
        _y = vector.Y;
    }

    public bool IsEmpty => _x == 0f && _y == 0f;
    public float X { readonly get => _x; set => _x = value; }
    public float Y { readonly get => _y; set => _y = value; }

    public Vector2 ToVector2() => new(_x, _y);

    public static PointF Add(PointF pt, Size sz) => new(pt.X + sz.Width, pt.Y + sz.Height);
    public static PointF Add(PointF pt, SizeF sz) => new(pt.X + sz.Width, pt.Y + sz.Height);
    public static PointF Subtract(PointF pt, Size sz) => new(pt.X - sz.Width, pt.Y - sz.Height);
    public static PointF Subtract(PointF pt, SizeF sz) => new(pt.X - sz.Width, pt.Y - sz.Height);

    public static PointF operator +(PointF pt, Size sz) => Add(pt, sz);
    public static PointF operator +(PointF pt, SizeF sz) => Add(pt, sz);
    public static PointF operator -(PointF pt, Size sz) => Subtract(pt, sz);
    public static PointF operator -(PointF pt, SizeF sz) => Subtract(pt, sz);
    public static bool operator ==(PointF left, PointF right) => left.Equals(right);
    public static bool operator !=(PointF left, PointF right) => !left.Equals(right);
    public static explicit operator Point(PointF value) => new((int)value.X, (int)value.Y);
    public static explicit operator Vector2(PointF value) => value.ToVector2();
    public static explicit operator PointF(Vector2 value) => new(value);

    public readonly bool Equals(PointF other) => _x == other._x && _y == other._y;
    public override readonly bool Equals(object? obj) => obj is PointF other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_x, _y);
    public override readonly string ToString() => $"{{X={_x}, Y={_y}}}";
}

[TypeConverter(typeof(SizeConverter))]
public struct Size : IEquatable<Size>
{
    private int _width;
    private int _height;

    public static readonly Size Empty;

    public Size(Point pt)
    {
        _width = pt.X;
        _height = pt.Y;
    }

    public Size(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public bool IsEmpty => _width == 0 && _height == 0;
    public int Width { readonly get => _width; set => _width = value; }
    public int Height { readonly get => _height; set => _height = value; }

    public static Size Add(Size sz1, Size sz2) => new(sz1.Width + sz2.Width, sz1.Height + sz2.Height);
    public static Size Subtract(Size sz1, Size sz2) => new(sz1.Width - sz2.Width, sz1.Height - sz2.Height);
    public static Size Ceiling(SizeF value) => new((int)Math.Ceiling(value.Width), (int)Math.Ceiling(value.Height));
    public static Size Truncate(SizeF value) => new((int)value.Width, (int)value.Height);
    public static Size Round(SizeF value) => new((int)Math.Round(value.Width), (int)Math.Round(value.Height));

    public static Size operator +(Size sz1, Size sz2) => Add(sz1, sz2);
    public static Size operator -(Size sz1, Size sz2) => Subtract(sz1, sz2);
    public static Size operator *(int left, Size right) => new(left * right.Width, left * right.Height);
    public static Size operator *(Size left, int right) => new(left.Width * right, left.Height * right);
    public static Size operator /(Size left, int right) => new(left.Width / right, left.Height / right);
    public static Size operator *(float left, Size right) => new((int)(left * right.Width), (int)(left * right.Height));
    public static Size operator *(Size left, float right) => new((int)(left.Width * right), (int)(left.Height * right));
    public static Size operator /(Size left, float right) => new((int)(left.Width / right), (int)(left.Height / right));
    public static bool operator ==(Size left, Size right) => left.Equals(right);
    public static bool operator !=(Size left, Size right) => !left.Equals(right);
    public static implicit operator SizeF(Size p) => new(p.Width, p.Height);
    public static explicit operator Point(Size size) => new(size.Width, size.Height);

    public readonly bool Equals(Size other) => _width == other._width && _height == other._height;
    public override readonly bool Equals(object? obj) => obj is Size other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_width, _height);
    public override readonly string ToString() => $"{{Width={_width}, Height={_height}}}";
}

[TypeConverter(typeof(SizeFConverter))]
public struct SizeF : IEquatable<SizeF>
{
    private float _width;
    private float _height;

    public static readonly SizeF Empty;

    public SizeF(SizeF size)
    {
        _width = size._width;
        _height = size._height;
    }

    public SizeF(PointF pt)
    {
        _width = pt.X;
        _height = pt.Y;
    }

    public SizeF(float width, float height)
    {
        _width = width;
        _height = height;
    }

    public SizeF(Vector2 vector)
    {
        _width = vector.X;
        _height = vector.Y;
    }

    public bool IsEmpty => _width == 0f && _height == 0f;
    public float Width { readonly get => _width; set => _width = value; }
    public float Height { readonly get => _height; set => _height = value; }

    public PointF ToPointF() => new(_width, _height);
    public Size ToSize() => Size.Truncate(this);
    public Vector2 ToVector2() => new(_width, _height);

    public static SizeF Add(SizeF sz1, SizeF sz2) => new(sz1.Width + sz2.Width, sz1.Height + sz2.Height);
    public static SizeF Subtract(SizeF sz1, SizeF sz2) => new(sz1.Width - sz2.Width, sz1.Height - sz2.Height);

    public static SizeF operator +(SizeF sz1, SizeF sz2) => Add(sz1, sz2);
    public static SizeF operator -(SizeF sz1, SizeF sz2) => Subtract(sz1, sz2);
    public static SizeF operator *(float left, SizeF right) => new(left * right.Width, left * right.Height);
    public static SizeF operator *(SizeF left, float right) => new(left.Width * right, left.Height * right);
    public static SizeF operator /(SizeF left, float right) => new(left.Width / right, left.Height / right);
    public static bool operator ==(SizeF left, SizeF right) => left.Equals(right);
    public static bool operator !=(SizeF left, SizeF right) => !left.Equals(right);
    public static explicit operator PointF(SizeF size) => new(size.Width, size.Height);
    public static explicit operator Vector2(SizeF size) => size.ToVector2();
    public static explicit operator SizeF(Vector2 value) => new(value);

    public readonly bool Equals(SizeF other) => _width == other._width && _height == other._height;
    public override readonly bool Equals(object? obj) => obj is SizeF other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_width, _height);
    public override readonly string ToString() => $"{{Width={_width}, Height={_height}}}";
}

[TypeConverter(typeof(RectangleConverter))]
public struct Rectangle : IEquatable<Rectangle>
{
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public static readonly Rectangle Empty;

    public Rectangle(int x, int y, int width, int height)
    {
        _x = x;
        _y = y;
        _width = width;
        _height = height;
    }

    public Rectangle(Point location, Size size)
    {
        _x = location.X;
        _y = location.Y;
        _width = size.Width;
        _height = size.Height;
    }

    public Point Location
    {
        readonly get => new(_x, _y);
        set { _x = value.X; _y = value.Y; }
    }

    public Size Size
    {
        readonly get => new(_width, _height);
        set { _width = value.Width; _height = value.Height; }
    }

    public int X { readonly get => _x; set => _x = value; }
    public int Y { readonly get => _y; set => _y = value; }
    public int Width { readonly get => _width; set => _width = value; }
    public int Height { readonly get => _height; set => _height = value; }
    public readonly int Left => _x;
    public readonly int Top => _y;
    public readonly int Right => unchecked(_x + _width);
    public readonly int Bottom => unchecked(_y + _height);
    public readonly bool IsEmpty => _height <= 0 || _width <= 0;

    public static Rectangle FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, unchecked(right - left), unchecked(bottom - top));

    public static Rectangle Ceiling(RectangleF value) => new(
        (int)Math.Ceiling(value.X), (int)Math.Ceiling(value.Y),
        (int)Math.Ceiling(value.Width), (int)Math.Ceiling(value.Height));

    public static Rectangle Round(RectangleF value) => new(
        (int)Math.Round(value.X), (int)Math.Round(value.Y),
        (int)Math.Round(value.Width), (int)Math.Round(value.Height));

    public static Rectangle Truncate(RectangleF value) => new(
        (int)value.X, (int)value.Y, (int)value.Width, (int)value.Height);

    public readonly bool Contains(int x, int y) =>
        _x <= x && x < _x + _width && _y <= y && y < _y + _height;

    public readonly bool Contains(Point pt) => Contains(pt.X, pt.Y);

    public readonly bool Contains(Rectangle rect) =>
        _x <= rect._x && rect.Right <= Right && _y <= rect._y && rect.Bottom <= Bottom;

    public void Inflate(int width, int height)
    {
        unchecked
        {
            _x -= width;
            _y -= height;
            _width += 2 * width;
            _height += 2 * height;
        }
    }

    public void Inflate(Size size) => Inflate(size.Width, size.Height);

    public static Rectangle Inflate(Rectangle rect, int x, int y)
    {
        rect.Inflate(x, y);
        return rect;
    }

    public void Intersect(Rectangle rect) => this = Intersect(this, rect);

    public static Rectangle Intersect(Rectangle a, Rectangle b)
    {
        int x1 = Math.Max(a.X, b.X);
        int x2 = Math.Min(a.Right, b.Right);
        int y1 = Math.Max(a.Y, b.Y);
        int y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 >= x1 && y2 >= y1)
            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        return Empty;
    }

    public readonly bool IntersectsWith(Rectangle rect) =>
        rect.X < Right && _x < rect.Right && rect.Y < Bottom && _y < rect.Bottom;

    public static Rectangle Union(Rectangle a, Rectangle b)
    {
        int x1 = Math.Min(a.X, b.X);
        int x2 = Math.Max(a.Right, b.Right);
        int y1 = Math.Min(a.Y, b.Y);
        int y2 = Math.Max(a.Bottom, b.Bottom);
        return new Rectangle(x1, y1, x2 - x1, y2 - y1);
    }

    public void Offset(Point pos) => Offset(pos.X, pos.Y);

    public void Offset(int x, int y)
    {
        unchecked
        {
            _x += x;
            _y += y;
        }
    }

    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);
    public static bool operator !=(Rectangle left, Rectangle right) => !left.Equals(right);

    public readonly bool Equals(Rectangle other) =>
        _x == other._x && _y == other._y && _width == other._width && _height == other._height;

    public override readonly bool Equals(object? obj) => obj is Rectangle other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_x, _y, _width, _height);
    public override readonly string ToString() => $"{{X={_x},Y={_y},Width={_width},Height={_height}}}";
}

[TypeConverter(typeof(RectangleFConverter))]
public struct RectangleF : IEquatable<RectangleF>
{
    private float _x;
    private float _y;
    private float _width;
    private float _height;

    public static readonly RectangleF Empty;

    public RectangleF(float x, float y, float width, float height)
    {
        _x = x;
        _y = y;
        _width = width;
        _height = height;
    }

    public RectangleF(PointF location, SizeF size)
    {
        _x = location.X;
        _y = location.Y;
        _width = size.Width;
        _height = size.Height;
    }

    public RectangleF(Vector4 vector)
    {
        _x = vector.X;
        _y = vector.Y;
        _width = vector.Z;
        _height = vector.W;
    }

    public PointF Location
    {
        readonly get => new(_x, _y);
        set { _x = value.X; _y = value.Y; }
    }

    public SizeF Size
    {
        readonly get => new(_width, _height);
        set { _width = value.Width; _height = value.Height; }
    }

    public float X { readonly get => _x; set => _x = value; }
    public float Y { readonly get => _y; set => _y = value; }
    public float Width { readonly get => _width; set => _width = value; }
    public float Height { readonly get => _height; set => _height = value; }
    public readonly float Left => _x;
    public readonly float Top => _y;
    public readonly float Right => unchecked(_x + _width);
    public readonly float Bottom => unchecked(_y + _height);
    public readonly bool IsEmpty => _width <= 0 || _height <= 0;

    public static RectangleF FromLTRB(float left, float top, float right, float bottom) =>
        new(left, top, right - left, bottom - top);

    public readonly bool Contains(float x, float y) =>
        _x <= x && x < _x + _width && _y <= y && y < _y + _height;

    public readonly bool Contains(PointF pt) => Contains(pt.X, pt.Y);

    public readonly bool Contains(RectangleF rect) =>
        _x <= rect._x && rect.Right <= Right && _y <= rect._y && rect.Bottom <= Bottom;

    public void Inflate(float x, float y)
    {
        unchecked
        {
            _x -= x;
            _y -= y;
            _width += 2 * x;
            _height += 2 * y;
        }
    }

    public void Inflate(SizeF size) => Inflate(size.Width, size.Height);

    public static RectangleF Inflate(RectangleF rect, float x, float y)
    {
        rect.Inflate(x, y);
        return rect;
    }

    public void Intersect(RectangleF rect) => this = Intersect(this, rect);

    public static RectangleF Intersect(RectangleF a, RectangleF b)
    {
        float x1 = Math.Max(a.X, b.X);
        float x2 = Math.Min(a.Right, b.Right);
        float y1 = Math.Max(a.Y, b.Y);
        float y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 >= x1 && y2 >= y1)
            return new RectangleF(x1, y1, x2 - x1, y2 - y1);
        return Empty;
    }

    public readonly bool IntersectsWith(RectangleF rect) =>
        rect.X < Right && _x < rect.Right && rect.Y < Bottom && _y < rect.Bottom;

    public static RectangleF Union(RectangleF a, RectangleF b)
    {
        float x1 = Math.Min(a.X, b.X);
        float x2 = Math.Max(a.Right, b.Right);
        float y1 = Math.Min(a.Y, b.Y);
        float y2 = Math.Max(a.Bottom, b.Bottom);
        return new RectangleF(x1, y1, x2 - x1, y2 - y1);
    }

    public void Offset(PointF pos) => Offset(pos.X, pos.Y);

    public void Offset(float x, float y)
    {
        unchecked
        {
            _x += x;
            _y += y;
        }
    }

    public Vector4 ToVector4() => new(_x, _y, _width, _height);

    public static bool operator ==(RectangleF left, RectangleF right) => left.Equals(right);
    public static bool operator !=(RectangleF left, RectangleF right) => !left.Equals(right);
    public static implicit operator RectangleF(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    public static explicit operator Vector4(RectangleF value) => value.ToVector4();
    public static explicit operator RectangleF(Vector4 value) => new(value);

    public readonly bool Equals(RectangleF other) =>
        _x == other._x && _y == other._y && _width == other._width && _height == other._height;

    public override readonly bool Equals(object? obj) => obj is RectangleF other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_x, _y, _width, _height);
    public override readonly string ToString() => $"{{X={_x},Y={_y},Width={_width},Height={_height}}}";
}

public struct CharacterRange : IEquatable<CharacterRange>
{
    private int _first;
    private int _length;

    public CharacterRange(int first, int length)
    {
        _first = first;
        _length = length;
    }

    public int First { readonly get => _first; set => _first = value; }
    public int Length { readonly get => _length; set => _length = value; }

    public static bool operator ==(CharacterRange left, CharacterRange right) => left.Equals(right);
    public static bool operator !=(CharacterRange left, CharacterRange right) => !left.Equals(right);

    public readonly bool Equals(CharacterRange other) => _first == other._first && _length == other._length;
    public override readonly bool Equals(object? obj) => obj is CharacterRange other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(_first, _length);
}
