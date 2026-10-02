using System.Text;

namespace ChatTerror.Plugin.Logic;

// Text from a peer is printed in game as plain text, but control bytes would still be read as SeString macros or line breaks.
public static class TellText
{
    public const int MaxNameBytes = 64;

    public static string Clean(string text, int maxBytes)
    {
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.IsBmp && SendValidator.IsLineBreakOrControl((char)rune.Value))
                continue;
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
                break;
            builder.Append(rune.ToString());
        }
        return builder.ToString().Trim();
    }
}
