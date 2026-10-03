using System.Security.Cryptography;

namespace ChatTerror.Protocol;

// The second half of the pairing code. It never leaves the plugin and the phone, so the relay cannot
// predict the fingerprint and cannot grind substitute keys to match it.
public static class PairingSecret
{
    public const int Length = 8;
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));

    // Accepts lowercase, hyphens, spaces and the Crockford look-alikes O, I and L.
    public static string? Normalize(string input) => NormalizeChars(input, Length);

    public static string? NormalizeChars(string input, int length)
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
        return new string(chars);
    }

    public static string Format(string secret) => $"{secret[..4]}-{secret[4..]}";
}
