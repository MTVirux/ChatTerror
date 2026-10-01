using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

// Each character keeps its own newest `capacity` items, so a busy alt never pushes out another character's backlog.
public sealed class MessageHistory
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Queue<ChatItem>> byCharacter = new();
    private int capacity;
    private long version;

    public MessageHistory(int capacity, IEnumerable<ChatItem>? restored = null)
    {
        this.capacity = Math.Max(0, capacity);
        foreach (var item in (restored ?? []).OrderBy(i => i.Ts))
            Add(item);
    }

    public int Capacity
    {
        get { lock (gate) return capacity; }
        set
        {
            lock (gate)
            {
                capacity = Math.Max(0, value);
                foreach (var items in byCharacter.Values)
                    Trim(items);
                version++;
            }
        }
    }

    // Bumped on every change, so callers can tell when there is something new to save.
    public long Version
    {
        get { lock (gate) return version; }
    }

    public void Add(ChatItem item)
    {
        lock (gate)
        {
            if (!byCharacter.TryGetValue(item.Character, out var items))
                byCharacter[item.Character] = items = new Queue<ChatItem>();
            items.Enqueue(item);
            Trim(items);
            version++;
        }
    }

    public IReadOnlyList<ChatItem> Since(long ts)
    {
        lock (gate)
            return byCharacter.Values.SelectMany(items => items).Where(i => i.Ts > ts).OrderBy(i => i.Ts).ToList();
    }

    public void Clear()
    {
        lock (gate)
        {
            byCharacter.Clear();
            version++;
        }
    }

    private void Trim(Queue<ChatItem> items)
    {
        while (items.Count > capacity)
            items.Dequeue();
    }
}
