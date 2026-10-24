namespace ChatTerror.Server.Relay;

// Friend tell pushes the relay left to the recipient's plugin. A tellPush is only honoured once for one of these, so a
// plugin can't push a tell the relay already pushed itself, like a queued one it gets again when it reconnects.
public sealed class DeferredTellPushes(int maxEntries, TimeProvider time)
{
    // Well past the plugin's longest wait.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);

    private readonly Lock gate = new();
    private readonly Dictionary<(string, string, string), DateTimeOffset> entries = new();
    private DateTimeOffset nextPrune;

    // False when full, so the caller pushes directly instead of losing the push.
    public bool TryAdd(string installId, string deviceId, string tellId)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            Prune(now);
            if (entries.Count >= maxEntries)
                return false;

            entries[(installId, deviceId, tellId)] = now;
            return true;
        }
    }

    public bool TryTake(string installId, string deviceId, string tellId)
    {
        lock (gate)
        {
            return entries.Remove((installId, deviceId, tellId), out var added) && time.GetUtcNow() - added < Lifetime;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (now < nextPrune)
            return;

        nextPrune = now + PruneInterval;
        foreach (var (key, added) in entries)
        {
            if (now - added >= Lifetime)
                entries.Remove(key);
        }
    }
}
