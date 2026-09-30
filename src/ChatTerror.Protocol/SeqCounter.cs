namespace ChatTerror.Protocol;

// Time-based so a restarted sender with a fresh counter still outruns what receivers have seen.
public sealed class SeqCounter
{
    private readonly Func<long> nowMs;
    private readonly Lock gate = new();
    private long last;

    public SeqCounter(long last = 0)
        : this(last, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
    {
    }

    public SeqCounter(long last, Func<long> nowMs)
    {
        this.last = last;
        this.nowMs = nowMs;
    }

    public long Last
    {
        get { lock (gate) return last; }
    }

    public long Next()
    {
        lock (gate)
        {
            last = Math.Max(last + 1, nowMs());
            return last;
        }
    }
}

public sealed class SeqGuard
{
    private readonly Lock gate = new();
    private long lastSeen;

    public SeqGuard(long lastSeen = 0)
    {
        this.lastSeen = lastSeen;
    }

    public long LastSeen
    {
        get { lock (gate) return lastSeen; }
    }

    public bool Accept(long seq)
    {
        lock (gate)
        {
            if (seq <= lastSeen)
                return false;
            lastSeen = seq;
            return true;
        }
    }
}
