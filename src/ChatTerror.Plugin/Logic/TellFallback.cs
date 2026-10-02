using System.Collections.Generic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Logic;

public sealed record PendingTell(TellTarget Target, string Text, long At);

// The game echoes a tell only once the server accepted it, so a tell without an echo did not reach the player.
public sealed class TellFallback
{
    private readonly List<PendingTell> pending = new();

    public void Sent(TellTarget target, string text, long now) => pending.Add(new PendingTell(target, text, now));

    public void Echoed(string name, string? world)
    {
        var index = pending.FindIndex(p => p.Target.Matches(name, world));
        if (index >= 0)
            pending.RemoveAt(index);
    }

    public List<PendingTell> Expired(long now)
    {
        var expired = pending.FindAll(p => now - p.At >= Limits.TellEchoTimeoutMs);
        pending.RemoveAll(p => now - p.At >= Limits.TellEchoTimeoutMs);
        return expired;
    }
}
