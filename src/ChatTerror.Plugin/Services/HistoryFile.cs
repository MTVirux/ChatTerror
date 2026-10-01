using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Services;

// Chat is only ever written encrypted. Without DPAPI the history simply isn't saved.
public static class HistoryFile
{
    public const string Name = "history.dpapi";

    // Empty when there is no file yet, null when the file can't be decrypted or parsed.
    public static List<ChatItem>? Load(string path)
    {
        if (!File.Exists(path))
            return [];
        if (Secrets.Unprotect(File.ReadAllBytes(path)) is not { } data)
            return null;
        try
        {
            return ProtocolJson.Deserialize<List<ChatItem>>(Encoding.UTF8.GetString(data));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool Save(string path, IReadOnlyList<ChatItem> items)
    {
        if (Secrets.Protect(Encoding.UTF8.GetBytes(ProtocolJson.Serialize(items))) is not { } data)
            return false;
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, true);
        return true;
    }
}
