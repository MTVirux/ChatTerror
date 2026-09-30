using System.Security.Cryptography;
using System.Text;

namespace ChatTerror.Protocol;

public enum Direction { PluginToDevice, DeviceToPlugin }

public static class E2eCrypto
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize;

    private static readonly byte[] Info = Encoding.UTF8.GetBytes("ChatTerror v1");
    private static readonly byte[] AadP2d = Encoding.UTF8.GetBytes("ct1:p2d");
    private static readonly byte[] AadD2p = Encoding.UTF8.GetBytes("ct1:d2p");

    private static byte[] Aad(Direction direction) => direction == Direction.PluginToDevice ? AadP2d : AadD2p;

    public static byte[] DeriveKey(ECDiffieHellman own, byte[] peerPublicRaw, byte[] pluginPublicRaw, byte[] devicePublicRaw)
    {
        using var peer = P256.ImportPublicRaw(peerPublicRaw);
        var z = own.DeriveRawSecretAgreement(peer);
        var salt = pluginPublicRaw.Concat(devicePublicRaw).ToArray();
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, z, 32, salt, Info);
    }

    public static byte[] Seal(byte[] key, Direction direction, byte[] plaintext) =>
        Seal(key, direction, plaintext, RandomNumberGenerator.GetBytes(NonceSize));

    public static byte[] Seal(byte[] key, Direction direction, byte[] plaintext, byte[] nonce)
    {
        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        output[0] = Version;
        nonce.CopyTo(output, 1);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderSize, plaintext.Length), output.AsSpan(HeaderSize + plaintext.Length), Aad(direction));
        return output;
    }

    public static byte[] Open(byte[] key, Direction direction, byte[] envelope)
    {
        if (envelope.Length < HeaderSize + TagSize)
            throw new CryptographicException("Envelope too short.");
        if (envelope[0] != Version)
            throw new CryptographicException("Unsupported envelope version.");

        var cipherLength = envelope.Length - HeaderSize - TagSize;
        var plaintext = new byte[cipherLength];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            envelope.AsSpan(1, NonceSize),
            envelope.AsSpan(HeaderSize, cipherLength),
            envelope.AsSpan(HeaderSize + cipherLength),
            plaintext,
            Aad(direction));
        return plaintext;
    }
}
