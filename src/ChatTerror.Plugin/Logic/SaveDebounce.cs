namespace ChatTerror.Plugin.Logic;

// Coalesces saves of state that changes often but only has to survive a restart, at most one per interval.
public sealed class SaveDebounce(long intervalMs)
{
    private long lastSaveAt = long.MinValue / 2;

    public bool Dirty { get; private set; }

    public void MarkDirty() => Dirty = true;

    // True when a save is due now, which clears the dirty flag.
    public bool TakeDue(long now)
    {
        if (!Dirty || now - lastSaveAt < intervalMs)
            return false;
        Dirty = false;
        lastSaveAt = now;
        return true;
    }
}
