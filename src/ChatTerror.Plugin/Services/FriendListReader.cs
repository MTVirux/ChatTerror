using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace ChatTerror.Plugin.Services;

public sealed record GameFriend(ulong ContentId, string Name, ushort HomeWorld);

public static unsafe class FriendListReader
{
    // Empty until the game has loaded the friend list, which happens on login or when the friend list is opened.
    public static List<GameFriend> Read()
    {
        var friends = new List<GameFriend>();
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
            return friends;

        foreach (var entry in proxy->CharDataSpan)
        {
            if (entry.ContentId != 0 && entry.NameString.Length > 0)
                friends.Add(new GameFriend(entry.ContentId, entry.NameString, entry.HomeWorld));
        }
        return friends;
    }
}
