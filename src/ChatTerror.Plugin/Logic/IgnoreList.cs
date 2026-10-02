using System;
using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

public static class IgnoreList
{
    // Skips names already listed with or without a world, so an edited "Name@World" entry survives a re-import.
    public static int Merge(List<string> ignored, IEnumerable<string> names)
    {
        var added = 0;
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.Length == 0 || ignored.Exists(entry => string.Equals(NamePart(entry), name, StringComparison.OrdinalIgnoreCase)))
                continue;

            ignored.Add(name);
            added++;
        }

        return added;
    }

    private static string NamePart(string entry) => entry.Split('@')[0].Trim();
}
