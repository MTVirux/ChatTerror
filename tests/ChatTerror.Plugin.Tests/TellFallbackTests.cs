using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class TellFallbackTests
{
    private static readonly TellTarget Bob = new("Bob Smith", "Lich");

    [Fact]
    public void EchoedTell_IsNeverRelayed()
    {
        var fallback = new TellFallback();
        fallback.Sent(Bob, "hi", 0);

        fallback.Echoed("Bob Smith", null);

        Assert.Empty(fallback.Expired(Limits.TellEchoTimeoutMs * 2));
    }

    [Fact]
    public void MissingEcho_ExpiresAfterTimeoutOnce()
    {
        var fallback = new TellFallback();
        fallback.Sent(Bob, "hi", 0);

        Assert.Empty(fallback.Expired(Limits.TellEchoTimeoutMs - 1));
        Assert.Equal([new PendingTell(Bob, "hi", 0)], fallback.Expired(Limits.TellEchoTimeoutMs));
        Assert.Empty(fallback.Expired(Limits.TellEchoTimeoutMs * 2));
    }

    [Fact]
    public void Echo_ClearsOnlyTheOldestTellToThatTarget()
    {
        var fallback = new TellFallback();
        var cid = new TellTarget("Cid Garlond", "Lich");
        fallback.Sent(Bob, "one", 0);
        fallback.Sent(Bob, "two", 10);
        fallback.Sent(cid, "three", 20);

        fallback.Echoed("Bob Smith", "Lich");

        Assert.Equal(["two", "three"], fallback.Expired(10_000).Select(p => p.Text));
    }
}
