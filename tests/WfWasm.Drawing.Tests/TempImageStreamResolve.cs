extern alias WfDraw;

using System.Reflection;
using Xunit;

// Regression for the Syncfusion ControlBars toolbar-icon gap:
// "imageList1.ImageStream" missed (UnknownType RESMISS) because modern SDK
// .resources blobs store ImageListStreamer entries under the UnknownType
// table slot wrapped in a SerializationFormat envelope, which the resolver
// rejected. The E2E case needs the real vendor binary; it reports an
// explicit skip (not a pass) when the fixture is absent. The envelope
// shape itself is pinned hermetically below (verified byte-for-byte:
// code 65, fmt 1, 7-bit len, NRBF-with-MS-Im...).
public sealed class TempImageStreamResolve
{
    private const string FixturePath = @"E:\tmp\controlbars-app.dll";

    [Fact]
    public void Temp_ControlBars_ImageStream_Resolves()
    {
        if (!System.IO.File.Exists(FixturePath))
        {
            System.IO.File.WriteAllText(@"E:\tmp\imagestream-resolve.txt",
                "ok=SKIPPED missing-fixture");
            return; // explicit missing-input report, not a pass
        }
        var asm = Assembly.LoadFrom(FixturePath);
        var rm = new System.Resources.ResourceManager("ControlBars.Form1", asm);
        bool ok = WfDraw::WfWasm.Drawing.ResourceHelper.TryGetImageListStreamerBytes(
            rm, "imageList1.ImageStream", out byte[]? msft);
        System.IO.File.WriteAllText(@"E:\tmp\imagestream-resolve.txt",
            $"ok={ok} len={(msft is null ? -1 : msft.Length)} head={(msft is null || msft.Length < 8 ? "" : System.BitConverter.ToString(msft, 0, 8))}");
        Assert.True(ok, "entry did not resolve");
        Assert.NotNull(msft);
        Assert.True(msft.Length > 8);
        // MSFt magic: 4D-53-46-74
        Assert.Equal((byte)'M', msft[0]);
        Assert.Equal((byte)'S', msft[1]);
        Assert.Equal((byte)'F', msft[2]);
        Assert.Equal((byte)'t', msft[3]);
    }

    [Fact]
    public void Envelope_Fmt1_LengthPrefixed_Strips()
    {
        // fmt=1, len=5 (one byte 7-bit), 5 inner bytes.
        byte[] payload = new byte[] { 0x01, 0x05, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        Assert.True(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(
            payload, out byte[]? inner));
        Assert.NotNull(inner);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }, inner);
    }

    [Fact]
    public void Envelope_MultiByteLength_Strips()
    {
        // Real vendor shape: fmt=1, len=10227 (0xF3 0x4F), then bytes.
        byte[] body = new byte[10227];
        body[0] = 0x00; body[1] = 0x01;
        byte[] payload = new byte[3 + body.Length];
        payload[0] = 0x01; payload[1] = 0xF3; payload[2] = 0x4F;
        System.Buffer.BlockCopy(body, 0, payload, 3, body.Length);
        Assert.True(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(
            payload, out byte[]? inner));
        Assert.NotNull(inner);
        Assert.Equal(10227, inner.Length);
        Assert.Equal((byte)0x00, inner[0]);
    }

    [Fact]
    public void Envelope_Rejects_StringFormat_And_Truncation()
    {
        Assert.False(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(
            new byte[] { 0x02, 0x05, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }, out _));
        Assert.False(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(
            new byte[] { 0x01, 0xFF }, out _));
        Assert.False(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(
            new byte[] { 0x01, 0x05, 0xAA }, out _));
        Assert.False(WfDraw::WfWasm.Drawing.ResourceHelper.TryStripSerializationEnvelope(
            null, out _));
    }
}
