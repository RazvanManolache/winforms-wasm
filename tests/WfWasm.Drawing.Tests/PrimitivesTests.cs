// Behavioral tests for WfWasm.Drawing primitives. Expected strings, sizes,
// and rounding rules were captured from the real .NET 9 framework
// (enum-oracle ##PRIMS/##OPS). Hash asserts are consistency-only:
// System.HashCode is process-seeded, so exact values are not a compat
// contract even in real .NET.
using System.Numerics;

public sealed class PrimitivesTests
{
    [Fact]
    public void Point_Basics()
    {
        var p = new Point(3, 4);
        Assert.Equal(3, p.X);
        Assert.Equal(4, p.Y);
        Assert.False(p.IsEmpty);
        Assert.True(Point.Empty.IsEmpty);
        Assert.Equal("{X=3,Y=4}", p.ToString());
        Assert.Equal(p.GetHashCode(), new Point(3, 4).GetHashCode());
    }

    [Fact]
    public void Point_DwordCtor_SplitsSignedShorts()
    {
        var p = new Point(0x00020001);
        Assert.Equal(new Point(1, 2), p);
        var neg = new Point(unchecked((int)0xFFFF8000));
        Assert.Equal(new Point(-32768, -1), neg);
    }

    [Fact]
    public void Point_Arithmetic_And_Operators()
    {
        var p = new Point(10, 20);
        Assert.Equal(new Point(13, 24), Point.Add(p, new Size(3, 4)));
        Assert.Equal(new Point(13, 24), p + new Size(3, 4));
        Assert.Equal(new Point(7, 16), Point.Subtract(p, new Size(3, 4)));
        Assert.Equal(new Point(7, 16), p - new Size(3, 4));
        Assert.True(new Point(1, 2) == new Point(1, 2));
        Assert.True(new Point(1, 2) != new Point(1, 3));
        p.Offset(1, -2);
        Assert.Equal(new Point(11, 18), p);
        p.Offset(new Point(-1, 2));
        Assert.Equal(new Point(10, 20), p);
    }

    [Fact]
    public void Point_Rounding_IsBankers()
    {
        Assert.Equal(new Point(0, 2), Point.Round(new PointF(0.5f, 1.5f)));
        Assert.Equal(new Point(2, 4), Point.Round(new PointF(2.5f, 3.5f)));
        Assert.Equal(new Point(1, 2), Point.Ceiling(new PointF(0.5f, 1.5f)));
        Assert.Equal(new Point(0, 1), Point.Truncate(new PointF(0.5f, 1.5f)));
    }

    [Fact]
    public void Point_Conversions()
    {
        PointF f = new Point(3, 4);
        Assert.Equal(new PointF(3f, 4f), f);
        Assert.Equal(new Point(3, 4), (Point)new PointF(3.9f, 4.1f));
        Assert.Equal(new Size(3, 4), (Size)new Point(3, 4));
        Assert.Equal(new Point(5, 6), new Point(new Size(5, 6)));
    }

    [Fact]
    public void PointF_Basics()
    {
        var p = new PointF(1.5f, 2.5f);
        Assert.Equal("{X=1.5, Y=2.5}", p.ToString());
        Assert.Equal(p.GetHashCode(), new PointF(1.5f, 2.5f).GetHashCode());
        Assert.Equal(new Vector2(1.5f, 2.5f), p.ToVector2());
        Assert.Equal(p, (PointF)new Vector2(1.5f, 2.5f));
        Assert.Equal(new Vector2(1.5f, 2.5f), (Vector2)p);
        Assert.Equal(new PointF(4.5f, 6.5f), p + new Size(3, 4));
        Assert.Equal(new PointF(4.5f, 6.5f), p + new SizeF(3f, 4f));
        Assert.Equal(new PointF(-1.5f, -1.5f), p - new Size(3, 4));
        Assert.True(new PointF(1, 2) == new PointF(1, 2));
        Assert.True(new PointF(1, 2) != new PointF(1, 3));
    }

    [Fact]
    public void Size_Arithmetic_And_Operators()
    {
        var s = new Size(3, 4);
        Assert.Equal("{Width=3, Height=4}", s.ToString());
        Assert.Equal(s.GetHashCode(), new Size(3, 4).GetHashCode());
        Assert.Equal(new Size(6, 8), s + s);
        Assert.Equal(new Size(0, 0), s - s);
        Assert.Equal(new Size(6, 8), 2 * s);
        Assert.Equal(new Size(6, 8), s * 2);
        Assert.Equal(new Size(1, 2), s / 2);
        Assert.Equal(new Size(5, 6), new Size(new Point(5, 6)));
        SizeF f = s;
        Assert.Equal(new SizeF(3f, 4f), f);
        Assert.Equal(new Point(3, 4), (Point)s);
        Assert.True(new Size(1, 2) == new Size(1, 2));
    }

    [Fact]
    public void SizeF_Conversions_And_Operators()
    {
        var s = new SizeF(1.5f, 2.5f);
        Assert.Equal("{Width=1.5, Height=2.5}", s.ToString());
        Assert.Equal(s.GetHashCode(), new SizeF(1.5f, 2.5f).GetHashCode());
        Assert.Equal(new PointF(1.5f, 2.5f), s.ToPointF());
        Assert.Equal(new PointF(1.5f, 2.5f), (PointF)s);
        Assert.Equal(new Size(1, 2), s.ToSize());
        Assert.Equal(new Vector2(1.5f, 2.5f), s.ToVector2());
        Assert.Equal(new SizeF(3f, 5f), s + s);
        Assert.Equal(new SizeF(3f, 5f), 2f * s);
        Assert.Equal(new SizeF(3f, 5f), s * 2f);
        Assert.Equal(new SizeF(0.75f, 1.25f), s / 2f);
        Assert.Equal(new SizeF(1.5f, 2.5f), new SizeF(new PointF(1.5f, 2.5f)));
    }

    [Fact]
    public void Rectangle_Geometry()
    {
        var r = new Rectangle(1, 2, 3, 4);
        Assert.Equal("{X=1,Y=2,Width=3,Height=4}", r.ToString());
        Assert.Equal(r.GetHashCode(), new Rectangle(1, 2, 3, 4).GetHashCode());
        Assert.Equal(1, r.Left);
        Assert.Equal(2, r.Top);
        Assert.Equal(4, r.Right);
        Assert.Equal(6, r.Bottom);
        Assert.Equal(new Point(1, 2), r.Location);
        Assert.Equal(new Size(3, 4), r.Size);
        Assert.True(r.Contains(1, 2));
        Assert.True(r.Contains(new Point(3, 5)));
        Assert.False(r.Contains(4, 6));
        Assert.False(r.Contains(new Rectangle(0, 0, 10, 10)));
        Assert.True(new Rectangle(0, 0, 10, 10).Contains(r));
        Assert.True(r.IntersectsWith(new Rectangle(3, 4, 5, 6)));
        Assert.False(r.IntersectsWith(new Rectangle(10, 10, 2, 2)));
        Assert.Equal(new Rectangle(3, 4, 1, 2), Rectangle.Intersect(r, new Rectangle(3, 4, 5, 6)));
        Assert.Equal(Rectangle.Empty, Rectangle.Intersect(r, new Rectangle(10, 10, 2, 2)));
        Assert.Equal(new Rectangle(0, 0, 10, 10), Rectangle.Union(r, new Rectangle(0, 0, 10, 10)));
        Assert.Equal(Rectangle.FromLTRB(0, 1, 5, 6), new Rectangle(0, 1, 5, 5));
        var inf = Rectangle.Inflate(r, 1, 1);
        Assert.Equal(new Rectangle(0, 1, 5, 6), inf);
        r.Offset(1, 1);
        Assert.Equal(new Rectangle(2, 3, 3, 4), r);
        Assert.True(new Rectangle(1, 2, 3, 4) == new Rectangle(1, 2, 3, 4));
        Assert.True(Rectangle.Empty.IsEmpty);
    }

    [Fact]
    public void RectangleF_Geometry_And_Vectors()
    {
        var r = new RectangleF(1.5f, 2.5f, 3.5f, 4.5f);
        Assert.Equal("{X=1.5,Y=2.5,Width=3.5,Height=4.5}", r.ToString());
        Assert.Equal(r.GetHashCode(), new RectangleF(1.5f, 2.5f, 3.5f, 4.5f).GetHashCode());
        Assert.Equal(new Vector4(1.5f, 2.5f, 3.5f, 4.5f), r.ToVector4());
        Assert.Equal(r, (RectangleF)new Vector4(1.5f, 2.5f, 3.5f, 4.5f));
        RectangleF fromInt = new Rectangle(1, 2, 3, 4);
        Assert.Equal(new RectangleF(1f, 2f, 3f, 4f), fromInt);
        Assert.True(r.Contains(1.5f, 2.5f));
        Assert.False(r.Contains(9f, 9f));
        Assert.Equal(new RectangleF(0, 0, 10, 10), RectangleF.Union(r, new RectangleF(0, 0, 10, 10)));
        Assert.True(new RectangleF(1, 2, 3, 4) == new RectangleF(1, 2, 3, 4));
        Assert.True(new RectangleF(1, 2, 3, 4) != new RectangleF(1, 2, 3, 5));
    }

    [Fact]
    public void CharacterRange_Basics()
    {
        var cr = new CharacterRange(2, 5);
        Assert.Equal(2, cr.First);
        Assert.Equal(5, cr.Length);
        Assert.Equal(cr.GetHashCode(), new CharacterRange(2, 5).GetHashCode());
        Assert.True(new CharacterRange(2, 5) == cr);
        Assert.True(new CharacterRange(2, 6) != cr);
        Assert.True(cr.Equals((object)new CharacterRange(2, 5)));
    }
}
