using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Protocol;

// The install identity key is an ECDH key, reused here for ECDSA P-256 signatures.
public static class InstallSignature
{
    public static string Sign(ECDiffieHellman identity, byte[] data)
    {
        using var ecdsa = ECDsa.Create(identity.ExportParameters(true));
        return Base64Url.Encode(ecdsa.SignData(data, HashAlgorithmName.SHA256));
    }

    // Proves to the relay that an install registering a key holds its private half.
    public static string Proof(ECDiffieHellman identity) => Sign(identity, ProofData(Base64Url.Encode(P256.PublicRaw(identity))));

    public static bool VerifyProof(string publicKey, string proof) => Verify(publicKey, ProofData(publicKey), proof);

    private static byte[] ProofData(string publicKey) => Encoding.UTF8.GetBytes("ct1:install-proof:" + publicKey);

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
