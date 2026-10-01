using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed class MessageHistory
{
    private readonly Lock gate = new();
    private readonly Queue<ChatItem> items = new();
    private int capacity;

    public MessageHistory(int capacity)
    {
        this.capacity = Math.Max(0, capacity);
    }

    public int Capacity
    {
        get { lock (gate) return capacity; }
        set
        {
            lock (gate)
            {
                capacity = Math.Max(0, value);
                Trim();
            }
        }
    }

    public void Add(ChatItem item)
    {
        lock (gate)
        {
            items.Enqueue(item);
            Trim();
        }
    }

    public IReadOnlyList<ChatItem> Since(long ts)
    {
        lock (gate)
            return items.Where(i => i.Ts > ts).ToList();
    }

    public void Clear()
    {
        lock (gate)
            items.Clear();
    }

    private void Trim()
    {
        while (items.Count > capacity)
            items.Dequeue();
    }
}
