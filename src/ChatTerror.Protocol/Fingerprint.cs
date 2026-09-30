using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ChatTerror.Protocol;

public static class Fingerprint
{
    public static string Compute(byte[] pluginPublicRaw, byte[] devicePublicRaw)
    {
        var hash = SHA256.HashData(pluginPublicRaw.Concat(devicePublicRaw).ToArray());
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000;
        var digits = value.ToString("D6");
        return $"{digits[..3]} {digits[3..]}";
    }
}
