using System;
using System.Collections.Generic;
using System.Linq;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

// Relayed tells wait here until a character is logged in, since chat printed at the title screen is lost.
// When full, new tells are dropped: they stay unacked on the relay, which delivers them again on reconnect.
public sealed class TellInbox(int capacity)
{
    private readonly Queue<TellFrame> held = new();

    public void Add(TellFrame frame)
    {
        if (held.Count < capacity)
            held.Enqueue(frame);
    }

    public List<TellFrame> Drain(bool loggedIn, int max)
    {
        var frames = new List<TellFrame>();
        while (loggedIn && frames.Count < max && held.TryDequeue(out var frame))
            frames.Add(frame);
        return frames;
    }
}

public sealed class InFlightTells<T> where T : class
{
    private readonly Dictionary<string, T> pending = new();

    public void Add(string id, T value) => pending[id] = value;

    public T? Complete(string id) => pending.Remove(id, out var value) ? value : null;

    public List<T> DropAll()
    {
        var values = pending.Values.ToList();
        pending.Clear();
        return values;
    }
}

public static class TellItems
{
    // The relay drops tells after TellTtl, so an older one can only be a replay.
    public static bool IsExpired(TellBody body, long now) => now - body.Ts > (long)Limits.TellTtl.TotalMilliseconds;

    // The sender sets Ts, so it is never allowed past our own clock where it would move the phones' sync point.
    public static ChatItem? Incoming(TellBody body, string from, IReadOnlyList<TellCharacter> characters, long now)
    {
        var own = characters.FirstOrDefault(c => c.Hash == body.ToHash);
        var friend = TellContacts.Friend(characters, body.ToHash, from);
        if (own == null || friend == null)
            return null;
        return new ChatItem(body.Id, Math.Min(body.Ts, now), ChatChannel.Tell, friend.Name, friend.World, body.Text, own.Name, false);
    }

    public static ChatItem Outgoing(TellBody body, long now) =>
        new(body.Id, Math.Min(body.Ts, now), ChatChannel.Tell, body.ToName, body.ToWorld, body.Text, body.FromName, true);
}
