namespace ChatTerror.Protocol.Tests;

public class SeqTests
{
    [Fact]
    public void Seq_Monotonic_WithFrozenClock()
    {
        var counter = new SeqCounter(5000, () => 1000);

        Assert.Equal(5001, counter.Next());
        Assert.Equal(5002, counter.Next());
        Assert.Equal(5003, counter.Next());
        Assert.Equal(5003, counter.Last);
    }

    [Fact]
    public void Seq_UsesClock_WhenAheadOfLast()
    {
        var now = 1000L;
        var counter = new SeqCounter(0, () => now);

        Assert.Equal(1000, counter.Next());
        now = 5000;
        Assert.Equal(5000, counter.Next());
    }

    [Fact]
    public void Seq_AfterRestart_IsGreater()
    {
        var guard = new SeqGuard();
        var before = new SeqCounter(0, () => 1000);
        Assert.Equal(1000, before.Next());
        Assert.True(guard.Accept(1000));

        var afterRestart = new SeqCounter(0, () => 1001);
        var seq = afterRestart.Next();

        Assert.Equal(1001, seq);
        Assert.True(guard.Accept(seq));
    }

    [Fact]
    public void SeqGuard_RejectsReplayAndOlder()
    {
        var guard = new SeqGuard(10);

        Assert.False(guard.Accept(10));
        Assert.False(guard.Accept(9));
        Assert.True(guard.Accept(11));
        Assert.False(guard.Accept(11));
        Assert.Equal(11, guard.LastSeen);
    }
}
