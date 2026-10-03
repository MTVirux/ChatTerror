namespace ChatTerror.Server.Relay;

// Caps tells per sender install and recipient install, and pushes per sender install and device.
// Entries are dropped once their window has passed, so only recently active pairs are kept.
public sealed class TellLimiter(int tellsPerMinute, TimeSpan pushCooldown, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock gate = new();
    private readonly Dictionary<(string, string), (DateTimeOffset Start, int Count)> tells = new();
    private readonly Dictionary<(string, string), DateTimeOffset> pushes = new();
    private DateTimeOffset nextPrune;

    public bool TryTell(string senderInstall, string recipientInstall)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            Prune(now);
            var key = (senderInstall, recipientInstall);
            if (!tells.TryGetValue(key, out var window) || now - window.Start >= Window)
                window = (now, 0);
            if (window.Count >= tellsPerMinute)
                return false;

            tells[key] = (window.Start, window.Count + 1);
            return true;
        }
    }

    public bool TryPush(string senderInstall, string deviceId)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            Prune(now);
            var key = (senderInstall, deviceId);
            if (pushes.TryGetValue(key, out var last) && now - last < pushCooldown)
                return false;

            pushes[key] = now;
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (now < nextPrune)
            return;

        nextPrune = now + Window;
        foreach (var (key, window) in tells)
        {
            if (now - window.Start >= Window)
                tells.Remove(key);
        }
        foreach (var (key, last) in pushes)
        {
            if (now - last >= pushCooldown)
                pushes.Remove(key);
        }
    }
}
