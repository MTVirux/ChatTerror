using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace ChatTerror.Plugin.Services;

public static unsafe class BlacklistReader
{
    // The game only fills the blacklist after login or after the Blacklist window is opened, and stores no world.
    public static List<string> ReadNames()
    {
        var names = new List<string>();
        var blacklist = InfoProxyBlacklist.Instance();
        if (blacklist == null)
            return names;

        var count = blacklist->BlockedCharactersCount;
        var entries = blacklist->BlockedCharacters;
        for (var i = 0; i < count && i < entries.Length; i++)
        {
            var name = entries[i].Name;
            if (name.HasValue && name.ToString() is { Length: > 0 } value)
                names.Add(value);
        }

        return names;
    }
}
