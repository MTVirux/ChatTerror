using System.Security.Cryptography;

namespace ChatTerror.Protocol;

// The install identity key is an ECDH key, reused here for ECDSA P-256 signatures.
public static class InstallSignature
{
    public static string Sign(ECDiffieHellman identity, byte[] data)
    {
        using var ecdsa = ECDsa.Create(identity.ExportParameters(true));
        return Base64Url.Encode(ecdsa.SignData(data, HashAlgorithmName.SHA256));
    }

    public static bool Verify(string publicKey, byte[] data, string signature)
    {
        try
        {
            var raw = Base64Url.Decode(publicKey);
            P256.ImportPublicRaw(raw).Dispose();
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = raw[1..33], Y = raw[33..65] },
            });
            return ecdsa.VerifyData(data, Base64Url.Decode(signature), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
