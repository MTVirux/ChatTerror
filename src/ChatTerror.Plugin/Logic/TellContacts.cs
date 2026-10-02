using System.Collections.Generic;
using System.Linq;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed class TellFriend
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
}

// One of this install's characters, as last seen in game.
public sealed class TellCharacter
{
    public string Hash { get; set; } = "";
    public string Name { get; set; } = "";
    public string World { get; set; } = "";
    public List<TellFriend> Friends { get; set; } = new();

    // Friend hashes the relay reported as ChatTerror users.
    public List<string> Registered { get; set; } = new();
}

public sealed record TellRoute(TellCharacter From, TellFriend To);

public static class TellContacts
{
    // The logged-in character sends when it is friends with the target, otherwise an alt that is.
    public static TellRoute? Route(IReadOnlyList<TellCharacter> characters, string? currentHash, TellTarget target)
    {
        foreach (var character in characters.OrderBy(c => c.Hash == currentHash ? 0 : 1))
        {
            var friend = character.Friends.FirstOrDefault(f => character.Registered.Contains(f.Hash) && target.Matches(f.Name, f.World));
            if (friend != null)
                return new TellRoute(character, friend);
        }
        return null;
    }

    public static TellFriend? Friend(IReadOnlyList<TellCharacter> characters, string ownHash, string friendHash) =>
        characters.FirstOrDefault(c => c.Hash == ownHash)?.Friends.FirstOrDefault(f => f.Hash == friendHash);

    public static List<TellContact> ForDevices(IReadOnlyList<TellCharacter> characters, IReadOnlyDictionary<string, string> pins) =>
        characters
            .SelectMany(c => c.Friends
                .Where(f => c.Registered.Contains(f.Hash))
                .Select(f => new TellContact(c.Name, c.World, c.Hash, f.Name, f.World, f.Hash, pins.GetValueOrDefault(f.Hash))))
            .ToList();

    public static List<string> Uploadable(TellCharacter character, RelaySettings settings) =>
        character.Friends.Where(f => !ChatFilter.IsIgnored(f.Name, f.World, settings)).Select(f => f.Hash).Distinct().ToList();
}
