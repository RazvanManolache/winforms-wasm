using System.Security.Cryptography;

namespace WfWasm.CanvasHost;

internal static class DemandAssemblyPayload
{
    // The sync browser bridge prefixes the HTTP response's SHA-256 header
    // as 32 binary bytes. An empty response is an ordinary resolution miss.
    internal static byte[]? Decode(byte[] packet)
    {
        if (packet.Length == 0) return null;
        if (packet.Length <= 32) throw new InvalidDataException("Truncated assembly response.");
        byte[] bytes = packet.AsSpan(32).ToArray();
        if (!CryptographicOperations.FixedTimeEquals(packet.AsSpan(0, 32), SHA256.HashData(bytes)))
            throw new InvalidDataException("Runtime assembly integrity check failed.");
        return bytes;
    }
}
