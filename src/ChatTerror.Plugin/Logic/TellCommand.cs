using System;
using System.Text.RegularExpressions;

namespace ChatTerror.Plugin.Logic;

public sealed record TellTarget(string Name, string World)
{
    public override string ToString() => $"{Name}@{World}";

    // Echoes of same-world tells carry no world.
    public bool Matches(string name, string? world) =>
        string.Equals(Name, name, StringComparison.OrdinalIgnoreCase)
        && (world == null || string.Equals(World, world, StringComparison.OrdinalIgnoreCase));
}

public static class TellCommand
{
    private static readonly Regex Tell = new(
        @"^/(?:tell|t)\s+([A-Za-z'\-]{1,15} [A-Za-z'\-]{1,15})(?:@([A-Za-z]{3,16}))?\s+(\S.*)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Reply = new(@"^/(?:r|reply)\s+(\S.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static (TellTarget Target, string Text)? ParseTell(string line, string currentWorld)
    {
        var match = Tell.Match(line);
        if (!match.Success)
            return null;
        var world = match.Groups[2].Success ? match.Groups[2].Value : currentWorld;
        return (new TellTarget(match.Groups[1].Value, world), match.Groups[3].Value.TrimEnd());
    }

    public static string? ParseReply(string line)
    {
        var match = Reply.Match(line);
        return match.Success ? match.Groups[1].Value.TrimEnd() : null;
    }
}
