using System.Security.Cryptography;

namespace ChatTerror.Server.Auth;

public static class PairingCodes
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public const int DeviceLength = 8;
    public const int FriendInviteLength = 12;

    public static string Generate(int length = DeviceLength) => Format(RandomNumberGenerator.GetItems<char>(Alphabet, length));

    // Accepts lowercase, missing hyphen and the Crockford look-alikes O, I and L.
    public static string? Normalize(string input, int length = DeviceLength)
    {
        var chars = input.ToUpperInvariant()
            .Where(c => c != '-' && c != ' ')
            .Select(c => c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            })
            .ToArray();

        if (chars.Length != length || chars.Any(c => !Alphabet.Contains(c)))
            return null;
        return Format(chars);
    }

    private static string Format(char[] chars) => string.Join('-', chars.Chunk(4).Select(group => new string(group)));
}
