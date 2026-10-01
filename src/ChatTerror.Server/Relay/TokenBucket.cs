namespace ChatTerror.Server.Relay;

// Not thread safe; each connection's receive loop owns its bucket.
public sealed class TokenBucket
{
    private readonly double baseRate;
    private readonly int baseBurst;
    private readonly TimeProvider time;
    private double rate;
    private double capacity;
    private double tokens;
    private long last;

    public TokenBucket(double ratePerSecond, int burst, TimeProvider time)
    {
        baseRate = rate = ratePerSecond;
        baseBurst = burst;
        capacity = tokens = burst;
        this.time = time;
        last = time.GetTimestamp();
    }

    // Multiplies the base rate and burst; growing the burst also grants the extra tokens right away.
    public void SetScale(int factor)
    {
        var newCapacity = (double)baseBurst * factor;
        tokens = Math.Min(newCapacity, tokens + Math.Max(0, newCapacity - capacity));
        capacity = newCapacity;
        rate = baseRate * factor;
    }

    public bool TryTake()
    {
        var now = time.GetTimestamp();
        tokens = Math.Min(capacity, tokens + time.GetElapsedTime(last, now).TotalSeconds * rate);
        last = now;
        if (tokens < 1)
            return false;

        tokens -= 1;
        return true;
    }
}
