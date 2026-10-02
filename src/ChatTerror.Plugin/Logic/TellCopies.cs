using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public static class TellCopies
{
    // ownTarget is the sending client, which already knows the tell.
    public static List<TellCopy> Build(TellBody body, TellBundle recipient, TellBundle? own, string ownTarget)
    {
        var copies = new List<TellCopy>();
        Add(copies, body, recipient, false, null);
        if (own != null)
            Add(copies, body, own, true, ownTarget);
        return copies;
    }

    private static void Add(List<TellCopy> copies, TellBody body, TellBundle bundle, bool self, string? skip)
    {
        foreach (var entry in bundle.Entries)
        {
            if (entry.Target == skip)
                continue;
            try
            {
                copies.Add(new TellCopy(self, entry.Target, SealedTell.SealBody(Base64Url.Decode(entry.Key), body)));
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
            }
        }
    }
}
