using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class AwayQueueTests
{
    [Fact]
    public void TakeDue_ReturnsOnlyDueEntriesInOrder()
    {
        var queue = new AwayQueue(10);
        var notified = new List<string>();
        queue.TryAdd(100, 3_000, () => notified.Add("a"));
        queue.TryAdd(200, 1_000, () => notified.Add("b"));
        queue.TryAdd(300, 5_000, () => notified.Add("c"));
        queue.TryAdd(400, 2_000, () => notified.Add("d"));

        var due = queue.TakeDue(3_000);

        Assert.Equal([100L, 200L, 400L], due.Select(d => d.StartedAt));
        foreach (var entry in due)
            entry.Notify();
        Assert.Equal(["a", "b", "d"], notified);
    }

    [Fact]
    public void TakeDue_RemovesWhatItReturns()
    {
        var queue = new AwayQueue(10);
        queue.TryAdd(100, 1_000, () => { });
        queue.TryAdd(200, 5_000, () => { });

        Assert.Single(queue.TakeDue(1_000));
        Assert.Equal(1, queue.Count);
        Assert.Empty(queue.TakeDue(1_000));
        Assert.Equal(200, Assert.Single(queue.TakeDue(5_000)).StartedAt);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void TryAdd_FailsWhenFull()
    {
        var queue = new AwayQueue(2);

        Assert.True(queue.TryAdd(100, 1_000, () => { }));
        Assert.True(queue.TryAdd(200, 1_000, () => { }));
        Assert.False(queue.TryAdd(300, 1_000, () => { }));
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Clear_DropsEverything()
    {
        var queue = new AwayQueue(10);
        queue.TryAdd(100, 1_000, () => { });
        queue.TryAdd(200, 5_000, () => { });

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Empty(queue.TakeDue(long.MaxValue));
    }
}
