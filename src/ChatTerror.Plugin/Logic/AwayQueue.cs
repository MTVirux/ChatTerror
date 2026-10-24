using System;
using System.Collections.Generic;

namespace ChatTerror.Plugin.Logic;

// Notifications held back until the wait ends, to see whether the player stays away from the game.
public sealed class AwayQueue(int capacity)
{
    private readonly List<(long StartedAt, long DueAt, Action Notify)> entries = new();

    public int Count => entries.Count;

    public bool TryAdd(long startedAt, long dueAt, Action notify)
    {
        if (entries.Count >= capacity)
            return false;
        entries.Add((startedAt, dueAt, notify));
        return true;
    }

    public List<(long StartedAt, Action Notify)> TakeDue(long now)
    {
        var due = new List<(long StartedAt, Action Notify)>();
        foreach (var entry in entries)
        {
            if (entry.DueAt <= now)
                due.Add((entry.StartedAt, entry.Notify));
        }

        entries.RemoveAll(e => e.DueAt <= now);
        return due;
    }

    public void Clear() => entries.Clear();
}
