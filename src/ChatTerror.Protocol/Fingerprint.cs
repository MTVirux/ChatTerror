using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Protocol;

public static class Fingerprint
{
    public static string Compute(string secret, byte[] pluginPublicRaw, byte[] devicePublicRaw)
    {
        var normalized = PairingSecret.Normalize(secret) ?? throw new ArgumentException("Invalid pairing secret.", nameof(secret));
        var hash = SHA256.HashData([.. Encoding.UTF8.GetBytes(normalized), .. pluginPublicRaw, .. devicePublicRaw]);
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000;
        var digits = value.ToString("D6");
        return $"{digits[..3]} {digits[3..]}";
    }
}
