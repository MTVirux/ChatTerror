using System.Security.Cryptography;
using System.Text;
using ChatTerror.Protocol;

namespace ChatTerror.Server.Auth;

public static class Tokens
{
    public const string InstallPrefix = "i";
    public const string DevicePrefix = "d";

    public static string NewId() => Base64Url.Encode(RandomNumberGenerator.GetBytes(16));

    public static (string Token, byte[] Hash) Create(string prefix, string id)
    {
        var secret = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
        return ($"{prefix}.{id}.{secret}", Hash(secret));
    }

    public static bool TryParse(string? token, string prefix, out string id, out string secret)
    {
        id = secret = "";
        if (token?.Split('.') is not [var tokenPrefix, var tokenId, var tokenSecret])
            return false;
        if (tokenPrefix != prefix || tokenId.Length == 0 || tokenSecret.Length == 0)
            return false;

        id = tokenId;
        secret = tokenSecret;
        return true;
    }

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public static bool Matches(byte[] storedHash, string secret) =>
        CryptographicOperations.FixedTimeEquals(storedHash, Hash(secret));
}
