using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Api;

// Fixed hourly window per install, kept in memory.
public sealed class PairingLimiter(IOptions<RelayOptions> options, TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly Lock gate = new();
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> windows = new();

    public bool TryAcquire(string installId)
    {
        var now = time.GetUtcNow();
        lock (gate)
        {
            if (windows.Count > 10_000)
                RemoveExpired(now);

            if (!windows.TryGetValue(installId, out var window) || now - window.Start >= Window)
                window = (now, 0);
            if (window.Count >= options.Value.PairingsPerInstallPerHour)
                return false;

            windows[installId] = (window.Start, window.Count + 1);
            return true;
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var (id, window) in windows.ToList())
        {
            if (now - window.Start >= Window)
                windows.Remove(id);
        }
    }
}
