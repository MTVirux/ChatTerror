using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChatTerror.Protocol;

public static class FriendScopes
{
    public const string Account = "*";

    public static bool IsValid(string? scope) => scope == Account || TellHash.IsValid(scope);

    public static bool Includes(string scope, string characterHash) => scope == Account || scope == characterHash;
}

// Id is the relay's lookup id ("XXXX-XXXX-XXXX"), Secret never leaves the two plugins.
public sealed record FriendCode(string Id, string Secret)
{
    public const int IdLength = 12;
    public const int SecretLength = 16;

    public static string NewSecret() => new(RandomNumberGenerator.GetItems<char>(PairingSecret.Alphabet, SecretLength));

    public static FriendCode? Parse(string input)
    {
        if (PairingSecret.NormalizeChars(input, IdLength + SecretLength) is not { } chars)
            return null;
        return new FriendCode($"{chars[..4]}-{chars[4..8]}-{chars[8..12]}", chars[IdLength..]);
    }

    public string Format()
    {
        var chars = Id.Replace("-", "") + Secret;
        return string.Join('-', Enumerable.Range(0, chars.Length / 4).Select(i => chars.Substring(i * 4, 4)));
    }
}

public static class FriendProof
{
    private static readonly byte[] InviteDomain = Encoding.UTF8.GetBytes("ChatTerror friend invite v1");
    private static readonly byte[] ClaimDomain = Encoding.UTF8.GetBytes("ChatTerror friend claim v1");

    public static string InviteTag(string secret, string inviteKey, string inviteScope) =>
        Mac(secret, [.. InviteDomain, .. Part(inviteKey, inviteScope)]);

    public static string ClaimMac(string secret, string inviteKey, string inviteScope, string claimKey, string claimScope) =>
        Mac(secret, [.. ClaimDomain, .. Part(inviteKey, inviteScope), .. Part(claimKey, claimScope)]);

    public static bool Matches(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Base64Url.Decode(expected), Base64Url.Decode(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Mac(string secret, byte[] data) => Base64Url.Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data));

    private static byte[] Part(string key, string scope)
    {
        var raw = Base64Url.Decode(key);
        if (raw.Length != 65)
            throw new ArgumentException("Install key must be a raw P-256 point.", nameof(key));
        var scopeBytes = Encoding.UTF8.GetBytes(scope);
        return [.. raw, (byte)scopeBytes.Length, .. scopeBytes];
    }
}

public sealed record FriendClaim(string InstallPublicKey, string Scope, string Mac);

public static class FriendClaims
{
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("ct1:friend-claim");

    public static string Seal(string inviterKey, FriendClaim claim) =>
        Base64Url.Encode(SealedBox.Seal(Base64Url.Decode(inviterKey), Encoding.UTF8.GetBytes(ProtocolJson.Serialize(claim)), Aad));

    public static FriendClaim? Open(ECDiffieHellman identity, string sealedClaim)
    {
        try
        {
            var plain = SealedBox.Open(identity, Base64Url.Decode(sealedClaim), Aad);
            return ProtocolJson.Deserialize<FriendClaim>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}

public sealed record FriendProfile(IReadOnlyList<string> Characters, long IssuedAt);

// Profile is the base64url UTF-8 JSON of a FriendProfile, so the signed bytes survive any re-serialization.
public sealed record SignedFriendProfile(string Profile, string Signature);

public static class FriendProfiles
{
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("ct1:friend-profile");

    public static string Seal(ECDiffieHellman identity, string friendKey, FriendProfile profile)
    {
        var bytes = Encoding.UTF8.GetBytes(ProtocolJson.Serialize(profile));
        var signed = new SignedFriendProfile(Base64Url.Encode(bytes), InstallSignature.Sign(identity, bytes));
        return Base64Url.Encode(SealedBox.Seal(Base64Url.Decode(friendKey), Encoding.UTF8.GetBytes(ProtocolJson.Serialize(signed)), Aad));
    }

    // Null unless sealed to identity, signed by signerKey and listing at most MaxTellCharacters valid hashes.
    public static FriendProfile? Open(ECDiffieHellman identity, string envelope, string signerKey)
    {
        try
        {
            var plain = SealedBox.Open(identity, Base64Url.Decode(envelope), Aad);
            var signed = ProtocolJson.Deserialize<SignedFriendProfile>(Encoding.UTF8.GetString(plain));
            if (signed == null)
                return null;
            var bytes = Base64Url.Decode(signed.Profile);
            if (!InstallSignature.Verify(signerKey, bytes, signed.Signature))
                return null;
            var profile = ProtocolJson.Deserialize<FriendProfile>(Encoding.UTF8.GetString(bytes));
            return profile is { Characters: not null } && profile.Characters.Count <= Limits.MaxTellCharacters && profile.Characters.All(TellHash.IsValid)
                ? profile
                : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
