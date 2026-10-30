extern alias WfDraw;

using Xunit;

// Pins TryMaterializeInline for every ResourceTypeCode wire shape
// (payloads exactly as RawReadValue produces them) plus rejections.
public sealed class TempInlineMaterialize
{
    private static bool Try(string? t, byte[]? p, out object? v) =>
        WfDraw::WfWasm.Drawing.ResourceHelper.TryMaterializeInline(t, p, out v);

    [Fact]
    public void Inline_String_Primitive_Bool_Char()
    {
        Assert.True(Try("ResourceTypeCode.String", new byte[] { 104, 105 }, out object? s));
        Assert.Equal("hi", s);
        Assert.True(Try("ResourceTypeCode.Int32", new byte[] { 42, 0, 0, 0 }, out object? i));
        Assert.Equal(42, i);
        Assert.True(Try("ResourceTypeCode.Boolean", new byte[] { 1 }, out object? b));
        Assert.Equal(true, b);
        Assert.True(Try("ResourceTypeCode.Char", new byte[] { 65, 0 }, out object? c));
        Assert.Equal('A', c);
    }

    [Fact]
    public void Inline_Int64_Double_Decimal_DateTime()
    {
        Assert.True(Try("ResourceTypeCode.Int64",
            System.BitConverter.GetBytes(123456789012345L), out object? l));
        Assert.Equal(123456789012345L, l);
        Assert.True(Try("ResourceTypeCode.Double",
            System.BitConverter.GetBytes(1.5), out object? d));
        Assert.Equal(1.5, d);
        decimal m = 123.456m;
        int[] bits = decimal.GetBits(m);
        byte[] raw = new byte[16];
        for (int k = 0; k < 4; k++)
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(bits[k]), 0, raw, k * 4, 4);
        Assert.True(Try("ResourceTypeCode.Decimal", raw, out object? dm));
        Assert.Equal(m, dm);
        long ticks = System.DateTime.UtcNow.Ticks;
        Assert.True(Try("ResourceTypeCode.DateTime",
            System.BitConverter.GetBytes(ticks), out object? dt));
        Assert.Equal(new System.DateTime(ticks), dt);
    }

    [Fact]
    public void Inline_ByteArray_Stream()
    {
        // Legacy GetResourceData framing: Int32 LE length + bytes
        // (DRR blobs never emit codes 32/33, so a bare payload has no
        // defined shape here and must fall back to the BCL path).
        Assert.True(Try("ResourceTypeCode.ByteArray",
            new byte[] { 3, 0, 0, 0, 9, 8, 7 }, out object? ba));
        Assert.Equal(new byte[] { 9, 8, 7 }, ba);
        Assert.True(Try("ResourceTypeCode.Stream",
            new byte[] { 3, 0, 0, 0, 1, 2, 3 }, out object? st));
        var ms = Assert.IsType<System.IO.MemoryStream>(st);
        Assert.Equal(new byte[] { 1, 2, 3 }, ms.ToArray());
        Assert.False(Try("ResourceTypeCode.ByteArray", new byte[] { 9, 8, 7 }, out _));
        Assert.False(Try("ResourceTypeCode.Stream", new byte[] { 1, 2, 3 }, out _));
    }

    [Fact]
    public void Inline_Rejects_Null_BadSize_Unknown()
    {
        Assert.False(Try("ResourceTypeCode.Null", System.Array.Empty<byte>(), out _));
        Assert.False(Try("ResourceTypeCode.Int32", new byte[] { 1, 2 }, out _));
        Assert.False(Try("ResourceTypeCode.String", null, out _));
        Assert.False(Try("Nope", new byte[] { 1 }, out _));
        Assert.False(Try(null, new byte[] { 1 }, out _));
        Assert.False(Try("ResourceTypeCode.Int32", System.Array.Empty<byte>(), out _));
    }
}
