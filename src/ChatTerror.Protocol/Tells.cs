using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChatTerror.Protocol;

public static class TellHash
{
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("ChatTerror tell v1");

    public static string Compute(ulong contentId)
    {
        var bytes = new byte[Domain.Length + 8];
        Domain.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(Domain.Length), contentId);
        return Base64Url.Encode(SHA256.HashData(bytes));
    }

    public static bool IsValid(string? hash)
    {
        if (hash is not { Length: 43 })
            return false;
        try
        {
            return Base64Url.Decode(hash).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed record TellBody(string Id, string FromHash, string FromName, string FromWorld, string ToHash, string ToName, string ToWorld, string Text, long Ts);

// Anyone can seal to a public key, so the sender is only known from the relay-checked TellFrame.From.
public static class SealedTell
{
    private const byte Version = 2;
    private const int KeySize = 65;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + KeySize + NonceSize;

    private static readonly byte[] Info = Encoding.UTF8.GetBytes("ChatTerror tell v1");
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("ct1:tell");

    public static byte[] Seal(byte[] recipientPublicRaw, byte[] plaintext)
    {
        using var ephemeral = P256.Generate();
        return Seal(ephemeral, recipientPublicRaw, plaintext, RandomNumberGenerator.GetBytes(NonceSize));
    }

    public static byte[] Seal(ECDiffieHellman ephemeral, byte[] recipientPublicRaw, byte[] plaintext, byte[] nonce)
    {
        var ephemeralRaw = P256.PublicRaw(ephemeral);
        var key = DeriveKey(ephemeral, recipientPublicRaw, ephemeralRaw, recipientPublicRaw);
        var output = new byte[HeaderSize + plaintext.Length + TagSize];
        output[0] = Version;
        ephemeralRaw.CopyTo(output, 1);
        nonce.CopyTo(output, 1 + KeySize);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderSize, plaintext.Length), output.AsSpan(HeaderSize + plaintext.Length), Aad);
        return output;
    }

    public static byte[] Open(ECDiffieHellman recipient, byte[] envelope)
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
            Aad);
        return plaintext;
    }

    public static string SealBody(byte[] recipientPublicRaw, TellBody body) =>
        Base64Url.Encode(Seal(recipientPublicRaw, Encoding.UTF8.GetBytes(ProtocolJson.Serialize(body))));

    // Throws CryptographicException when decryption fails and JsonException when the plaintext is not a tell.
    public static TellBody OpenBody(ECDiffieHellman recipient, string envelope)
    {
        byte[] bytes;
        try
        {
            bytes = Base64Url.Decode(envelope);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Envelope is not base64url.", ex);
        }

        var json = Encoding.UTF8.GetString(Open(recipient, bytes));
        return ProtocolJson.Deserialize<TellBody>(json) ?? throw new JsonException("Empty tell.");
    }

    private static byte[] DeriveKey(ECDiffieHellman own, byte[] peerRaw, byte[] ephemeralRaw, byte[] recipientRaw)
    {
        using var peer = P256.ImportPublicRaw(peerRaw);
        var z = own.DeriveRawSecretAgreement(peer);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, z, 32, [.. ephemeralRaw, .. recipientRaw], Info);
    }
}

public static class TellTargets
{
    public const string Plugin = "plugin";
}

public sealed record TellBundleEntry(string Target, string Key, bool Push);

public sealed record TellBundle(string InstallPublicKey, IReadOnlyList<TellBundleEntry> Entries, long IssuedAt);

// Bundle is the base64url UTF-8 JSON of a TellBundle, so the signed bytes survive any re-serialization.
public sealed record SignedTellBundle(string Bundle, string Signature);

public static class TellBundles
{
    public static SignedTellBundle Sign(ECDiffieHellman identity, TellBundle bundle)
    {
        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(bundle));
        using var ecdsa = ECDsa.Create(identity.ExportParameters(true));
        return new SignedTellBundle(Base64Url.Encode(bytes), Base64Url.Encode(ecdsa.SignData(bytes, HashAlgorithmName.SHA256)));
    }

    // Null when malformed, not signed by its own install key, or that key is not the expected one.
    public static TellBundle? Verify(SignedTellBundle signed, string? expectedInstallKey)
    {
        try
        {
            var bytes = Base64Url.Decode(signed.Bundle);
            var bundle = ProtocolJson.Deserialize<TellBundle>(Encoding.UTF8.GetString(bytes));
            if (bundle == null || (expectedInstallKey != null && bundle.InstallPublicKey != expectedInstallKey))
                return null;

            var raw = Base64Url.Decode(bundle.InstallPublicKey);
            P256.ImportPublicRaw(raw).Dispose();
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = raw[1..33], Y = raw[33..65] },
            });
            return ecdsa.VerifyData(bytes, Base64Url.Decode(signed.Signature), HashAlgorithmName.SHA256) ? bundle : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    // The relay only routes by the bundle, the clients check its signature.
    public static TellBundle? Read(SignedTellBundle signed)
    {
        try
        {
            return ProtocolJson.Deserialize<TellBundle>(Encoding.UTF8.GetString(Base64Url.Decode(signed.Bundle)));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}

public static class TellErrors
{
    public const string NotOwner = "notOwner";
    public const string NotChatTerror = "notChatTerror";
    public const string NotFriend = "notFriend";
    public const string BadCopies = "badCopies";
    public const string KeyChanged = "keyChanged";
}
