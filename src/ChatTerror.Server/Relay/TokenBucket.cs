namespace ChatTerror.Server.Relay;

// Not thread safe; each connection's receive loop owns its bucket.
public sealed class TokenBucket(double ratePerSecond, int burst, TimeProvider time)
{
    private double tokens = burst;
    private long last = time.GetTimestamp();

    public bool TryTake()
    {
        var now = time.GetTimestamp();
        tokens = Math.Min(burst, tokens + time.GetElapsedTime(last, now).TotalSeconds * ratePerSecond);
        last = now;
        if (tokens < 1)
            return false;

        tokens -= 1;
        return true;
    }
}
