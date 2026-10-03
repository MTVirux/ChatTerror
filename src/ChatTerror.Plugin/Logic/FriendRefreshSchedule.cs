namespace ChatTerror.Plugin.Logic;

// Decides when to refetch the friend list. A requested refresh runs right away, but change notices are coalesced
// so a friend spamming profile uploads cannot use up our relay request budget.
public sealed class FriendRefreshSchedule
{
    public const long IntervalMs = 10 * 60_000;
    public const long ChangedCooldownMs = 10_000;

    private long lastRefresh = long.MinValue / 2;
    private bool requested = true;
    private bool changed;

    public void Request() => requested = true;

    public void Changed() => changed = true;

    public bool TryStart(long now)
    {
        var elapsed = now - lastRefresh;
        if (!requested && elapsed < IntervalMs && !(changed && elapsed >= ChangedCooldownMs))
            return false;

        requested = false;
        changed = false;
        lastRefresh = now;
        return true;
    }
}
