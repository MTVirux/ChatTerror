using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

// Relayed tells this plugin printed, so a real tell is never taken for one because of its text. Each print is
// matched once, and prints whose chat message never showed up are forgotten oldest first.
public sealed class PrintedTells(int capacity)
{
    private readonly List<string> pending = new();

    public static string Key(bool outgoing, string text) => (outgoing ? ">" : "<") + text;

    public void Add(string key)
    {
        pending.Add(key);
        if (pending.Count > capacity)
            pending.RemoveAt(0);
    }

    public bool Take(string key) => pending.Remove(key);
}
