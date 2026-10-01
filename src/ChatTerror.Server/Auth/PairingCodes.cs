using System.Security.Cryptography;

namespace ChatTerror.Server.Auth;

public static class PairingCodes
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate() => Format(RandomNumberGenerator.GetItems<char>(Alphabet, 8));

    // Accepts lowercase, missing hyphen and the Crockford look-alikes O, I and L.
    public static string? Normalize(string input)
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

        if (chars.Length != 8 || chars.Any(c => !Alphabet.Contains(c)))
            return null;
        return Format(chars);
    }

    private static string Format(char[] chars) => $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
}
