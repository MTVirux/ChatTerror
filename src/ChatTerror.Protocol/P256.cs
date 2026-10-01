using System.Numerics;
using System.Security.Cryptography;

namespace ChatTerror.Protocol;

public static class P256
{
    private static readonly BigInteger Prime = Parse("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
    private static readonly BigInteger B = Parse("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b");

    private static BigInteger Parse(string hex) => BigInteger.Parse("0" + hex, System.Globalization.NumberStyles.HexNumber);

    public static ECDiffieHellman Generate() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static byte[] ExportPrivate(ECDiffieHellman key) => key.ExportECPrivateKey();

    public static ECDiffieHellman ImportPrivate(byte[] der)
    {
        var key = ECDiffieHellman.Create();
        key.ImportECPrivateKey(der, out _);
        return key;
    }

    public static byte[] PublicRaw(ECDiffieHellman key)
    {
        var q = key.ExportParameters(false).Q;
        var raw = new byte[65];
        raw[0] = 0x04;
        q.X!.CopyTo(raw, 1);
        q.Y!.CopyTo(raw, 33);
        return raw;
    }

    public static ECDiffieHellmanPublicKey ImportPublicRaw(byte[] raw)
    {
        if (raw.Length != 65 || raw[0] != 0x04)
            throw new CryptographicException("Public key must be a 65 byte uncompressed P-256 point.");
        if (!IsOnCurve(raw.AsSpan(1, 32), raw.AsSpan(33, 32)))
            throw new CryptographicException("Public key is not on the P-256 curve.");

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = raw[1..33], Y = raw[33..65] },
        };
        using var key = ECDiffieHellman.Create(parameters);
        return key.PublicKey;
    }

    // Checked here because Windows reports off-curve points as PlatformNotSupportedException.
    private static bool IsOnCurve(ReadOnlySpan<byte> xBytes, ReadOnlySpan<byte> yBytes)
    {
        var x = new BigInteger(xBytes, isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(yBytes, isUnsigned: true, isBigEndian: true);
        if (x >= Prime || y >= Prime)
            return false;

        var left = y * y % Prime;
        var right = ((x * x * x - 3 * x + B) % Prime + Prime) % Prime;
        return left == right;
    }
}
