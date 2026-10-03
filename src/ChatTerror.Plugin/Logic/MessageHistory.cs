using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

// Each character keeps its own newest `capacity` items per channel, and per partner for tells,
// so a busy channel never pushes out another channel's backlog.
public sealed class MessageHistory
{
    private readonly record struct Bucket(string Character, ChatChannel Channel, string? Partner);

    private readonly Lock gate = new();
    private readonly Dictionary<Bucket, Queue<ChatItem>> buckets = new();
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
                foreach (var items in buckets.Values)
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
            var bucket = BucketOf(item);
            if (!buckets.TryGetValue(bucket, out var items))
                buckets[bucket] = items = new Queue<ChatItem>();
            items.Enqueue(item);
            Trim(items);
            version++;
        }
    }

    public IReadOnlyList<ChatItem> Since(long ts)
    {
        lock (gate)
            return buckets.Values.SelectMany(items => items).Where(i => i.Ts > ts).OrderBy(i => i.Ts).ToList();
    }

    public IReadOnlyList<string> Characters
    {
        get
        {
            lock (gate)
                return buckets.Keys.Select(b => b.Character).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public IReadOnlyList<ChatItem> For(string character)
    {
        lock (gate)
            return buckets.Where(b => b.Key.Character == character).SelectMany(b => b.Value).OrderBy(i => i.Ts).ToList();
    }

    public void Clear()
    {
        lock (gate)
        {
            buckets.Clear();
            version++;
        }
    }

    // For tells the sender is always the other person, outgoing or not.
    private static Bucket BucketOf(ChatItem item) =>
        new(item.Character, item.Channel, item.Channel == ChatChannel.Tell ? $"{item.Sender}@{item.SenderWorld}".ToLowerInvariant() : null);

    private void Trim(Queue<ChatItem> items)
    {
        while (items.Count > capacity)
            items.Dequeue();
    }
}
