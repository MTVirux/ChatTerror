using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Protocol;

// ECIES to a raw P-256 public key. The aad separates purposes, so an envelope sealed for one never opens as another.
public static class SealedBox
{
    private const byte Version = 2;
    private const int KeySize = 65;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + KeySize + NonceSize;

    private static readonly byte[] Info = Encoding.UTF8.GetBytes("ChatTerror tell v1");

    public static byte[] Seal(byte[] recipientPublicRaw, byte[] plaintext, byte[] aad)
    {
        using var ephemeral = P256.Generate();
        return Seal(ephemeral, recipientPublicRaw, plaintext, RandomNumberGenerator.GetBytes(NonceSize), aad);
    }

    public static byte[] Seal(ECDiffieHellman ephemeral, byte[] recipientPublicRaw, byte[] plaintext, byte[] nonce, byte[] aad)
    {
        var ephemeralRaw = P256.PublicRaw(ephemeral);
        var key = DeriveKey(ephemeral, recipientPublicRaw, ephemeralRaw, recipientPublicRaw);
        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        output[0] = Version;
        ephemeralRaw.CopyTo(output, 1);
        nonce.CopyTo(output, 1 + KeySize);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderSize, plaintext.Length), output.AsSpan(HeaderSize + plaintext.Length), aad);
        return output;
    }

    public static byte[] Open(ECDiffieHellman recipient, byte[] envelope, byte[] aad)
    {
        if (envelope.Length < HeaderSize + TagSize)
            throw new CryptographicException("Envelope too short.");
        if (envelope[0] != Version)
            throw new CryptographicException("Unsupported envelope version.");

        var ephemeralRaw = envelope[1..(1 + KeySize)];
        var key = DeriveKey(recipient, ephemeralRaw, ephemeralRaw, P256.PublicRaw(recipient));
        var cipherLength = envelope.Length - HeaderSize - TagSize;
        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            envelope.AsSpan(1 + KeySize, NonceSize),
            envelope.AsSpan(HeaderSize, cipherLength),
            envelope.AsSpan(HeaderSize + cipherLength),
            plaintext,
            aad);
        return plaintext;
    }

    private static byte[] DeriveKey(ECDiffieHellman own, byte[] peerRaw, byte[] ephemeralRaw, byte[] recipientRaw)
    {
        using var peer = P256.ImportPublicRaw(peerRaw);
        var z = own.DeriveRawSecretAgreement(peer);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, z, 32, [.. ephemeralRaw, .. recipientRaw], Info);
    }
}
