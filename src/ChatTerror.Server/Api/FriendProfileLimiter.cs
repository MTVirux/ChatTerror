using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Api;

// Fixed hourly window per install and friend, kept in memory. Each upload makes the friend's plugin refetch its friend list.
public sealed class FriendProfileLimiter(IOptions<RelayOptions> options, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly Lock gate = new();
    private readonly Dictionary<(string, string), (DateTimeOffset Start, int Count)> windows = new();

    public bool TryAcquire(string ownerInstall, string friendInstall)
    {
        var now = time.GetUtcNow();
        var key = (ownerInstall, friendInstall);
        lock (gate)
        {
            if (windows.Count > 10_000)
                RemoveExpired(now);

            if (!windows.TryGetValue(key, out var window) || now - window.Start >= Window)
                window = (now, 0);
            if (window.Count >= options.Value.FriendProfilesPerHour)
                return false;

            windows[key] = (window.Start, window.Count + 1);
            return true;
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var (key, window) in windows.ToList())
        {
            if (now - window.Start >= Window)
                windows.Remove(key);
        }
    }
}
