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

public enum SenderTrust { Trusted, Pinned, KeyChanged }

public static class TellContacts
{
    // Only the logged-in character sends, a tell typed on one character must never go out from an alt.
    public static TellRoute? Route(IReadOnlyList<TellCharacter> characters, string? currentHash, TellTarget target)
    {
        var character = characters.FirstOrDefault(c => c.Hash == currentHash);
        var friend = character?.Friends.FirstOrDefault(f => character.Registered.Contains(f.Hash) && target.Matches(f.Name, f.World));
        return friend == null ? null : new TellRoute(character!, friend);
    }

    public static TellFriend? Friend(IReadOnlyList<TellCharacter> characters, string ownHash, string friendHash) =>
        characters.FirstOrDefault(c => c.Hash == ownHash)?.Friends.FirstOrDefault(f => f.Hash == friendHash);

    public static List<TellContact> ForDevices(IReadOnlyList<TellCharacter> characters, IReadOnlyDictionary<string, string> pins) =>
        characters
            .SelectMany(c => c.Friends
                .Where(f => c.Registered.Contains(f.Hash))
                .Select(f => new TellContact(c.Name, c.World, c.Hash, f.Name, f.World, f.Hash, pins.GetValueOrDefault(f.Hash))))
            .ToList();

    // The same pins guard both directions: a friend's character must keep sending from the install we first saw.
    public static SenderTrust TrustSender(IDictionary<string, string> pins, string fromHash, string fromKey)
    {
        if (!pins.TryGetValue(fromHash, out var pinned))
        {
            pins[fromHash] = fromKey;
            return SenderTrust.Pinned;
        }
        return pinned == fromKey ? SenderTrust.Trusted : SenderTrust.KeyChanged;
    }

    // Refuses a bundle older than the newest one accepted for that character, so the relay can't roll back to dropped keys.
    public static bool AcceptBundle(IDictionary<string, long> issued, string hash, long issuedAt)
    {
        if (issued.TryGetValue(hash, out var newest) && issuedAt < newest)
            return false;
        issued[hash] = issuedAt;
        return true;
    }

    public static List<string> Uploadable(TellCharacter character, RelaySettings settings) =>
        character.Friends.Where(f => !ChatFilter.IsIgnored(f.Name, f.World, settings)).Select(f => f.Hash).Distinct().ToList();
}
