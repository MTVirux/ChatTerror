using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class HelloThrottleTests
{
    [Fact]
    public void TryServe_FirstHello_IsServed()
    {
        var throttle = new HelloThrottle();

        Assert.True(throttle.TryServe("dev", 0, 1_000));
        Assert.Empty(throttle.TakeDue(1_000));
    }

    [Fact]
    public void TryServe_WithinCooldown_IsHeld()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("dev", 0, 1_000);

        Assert.False(throttle.TryServe("dev", 0, 1_100));
        Assert.Empty(throttle.TakeDue(1_000 + HelloThrottle.CooldownMs - 1));
    }

    [Fact]
    public void TakeDue_ServesHeldHelloOnceAfterCooldown()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("dev", 0, 1_000);
        for (var i = 0; i < 100; i++)
            throttle.TryServe("dev", 0, 1_100 + i);

        var now = 1_000 + HelloThrottle.CooldownMs;
        Assert.Equal([("dev", 0L)], throttle.TakeDue(now));
        Assert.Empty(throttle.TakeDue(now));
    }

    [Fact]
    public void TakeDue_KeepsEarliestSince()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("dev", 0, 1_000);
        throttle.TryServe("dev", 500, 1_100);
        throttle.TryServe("dev", 200, 1_200);
        throttle.TryServe("dev", 900, 1_300);

        Assert.Equal([("dev", 200L)], throttle.TakeDue(1_000 + HelloThrottle.CooldownMs));
    }

    [Fact]
    public void TakeDue_StartsNewCooldown()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("dev", 0, 1_000);
        throttle.TryServe("dev", 0, 1_100);
        var servedAt = 1_000 + HelloThrottle.CooldownMs;
        throttle.TakeDue(servedAt);

        Assert.False(throttle.TryServe("dev", 0, servedAt + 1));
        Assert.True(throttle.TryServe("other", 0, servedAt + 1));
    }

    [Fact]
    public void TryServe_AfterCooldown_IsServedAndDropsHeld()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("dev", 0, 1_000);
        throttle.TryServe("dev", 0, 1_100);

        Assert.True(throttle.TryServe("dev", 0, 1_000 + HelloThrottle.CooldownMs));
        Assert.Empty(throttle.TakeDue(1_000 + 2 * HelloThrottle.CooldownMs));
    }

    [Fact]
    public void Devices_AreThrottledIndependently()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("a", 0, 1_000);

        Assert.True(throttle.TryServe("b", 0, 1_100));
    }

    [Fact]
    public void Forget_DropsHeldHelloAndCooldown()
    {
        var throttle = new HelloThrottle();
        throttle.TryServe("dev", 0, 1_000);
        throttle.TryServe("dev", 0, 1_100);
        throttle.Forget("dev");

        Assert.Empty(throttle.TakeDue(1_000 + HelloThrottle.CooldownMs));
        Assert.True(throttle.TryServe("dev", 0, 1_200));
    }
}
