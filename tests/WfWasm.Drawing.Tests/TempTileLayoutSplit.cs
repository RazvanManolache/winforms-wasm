extern alias WfDraw;

using Xunit;

// CHARACTERIZATION: tile-layout imageStreamer payload shape. The fmt-1
// envelope strips cleanly (363B NRBF) but the MSFt Walker rejects it —
// the payload is a List<Image> NRBF, not an ImageListStreamer stream.
// This pins the shape that motivated the SafeGetObject empty-list
// recovery (TempTileLayoutResolve). Must stay green as documentation.
public sealed class TempTileLayoutSplit
{
    [Fact]
    public void Temp_TileLayout_Envelope_Strips_But_Not_MSIm()
    {
        byte[] blob = System.IO.File.ReadAllBytes(@"E:\tmp\tl-entry1-blob.bin");
        Assert.Equal(366, blob.Length);
        Assert.True(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(blob, out byte[]? inner));
        Assert.NotNull(inner);
        Assert.Equal(363, inner.Length);
        Assert.False(WfDraw::WfWasm.Drawing.NrbfImage.TryExtractImageListBytes(inner, out _));
    }
}
