using System.Security.Cryptography;
using WfWasm.CanvasHost;

public sealed class DemandAssemblyPayloadTests
{
    [Fact]
    public void Resolution_Miss_Returns_No_Assembly()
        => Assert.Null(DemandAssemblyPayload.Decode([]));

    [Fact]
    public void Verified_Response_Preserves_All_Binary_Values()
    {
        byte[] bytes = Enumerable.Range(0, 256).Select(n => (byte)n).ToArray();
        Assert.Equal(bytes, DemandAssemblyPayload.Decode(SHA256.HashData(bytes).Concat(bytes).ToArray()));
    }

    [Fact]
    public void Corrupt_Or_Truncated_Response_Cannot_Be_Loaded()
    {
        Assert.Throws<InvalidDataException>(() => DemandAssemblyPayload.Decode(new byte[32]));
        byte[] original = [0, 128, 255];
        var packet = SHA256.HashData(original).Concat(original).ToArray();
        packet[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => DemandAssemblyPayload.Decode(packet));
    }
}
