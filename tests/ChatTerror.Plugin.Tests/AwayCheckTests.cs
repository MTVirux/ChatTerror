using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class AwayCheckTests
{
    [Fact]
    public void WaitSeconds_ZeroSettingNotifiesNow()
    {
        Assert.Equal(0, AwayCheck.WaitSeconds(0, 5f));
    }

    [Fact]
    public void WaitSeconds_UnknownIdleNotifiesNow()
    {
        Assert.Equal(0, AwayCheck.WaitSeconds(10, null));
    }

    [Fact]
    public void WaitSeconds_AlreadyAwayNotifiesNow()
    {
        Assert.Equal(0, AwayCheck.WaitSeconds(10, 120f));
        Assert.Equal(10, AwayCheck.WaitSeconds(10, 119.9f));
    }

    [Fact]
    public void WaitSeconds_UsesTheSetting()
    {
        Assert.Equal(30, AwayCheck.WaitSeconds(30, 1f));
    }

    [Fact]
    public void StillAway_NoInputSinceTheEvent()
    {
        Assert.True(AwayCheck.StillAway(10.2f, 10_000));
    }

    [Fact]
    public void StillAway_InputSinceTheEvent()
    {
        Assert.False(AwayCheck.StillAway(3f, 10_000));
    }

    [Fact]
    public void StillAway_ToleratesFrameJitter()
    {
        Assert.True(AwayCheck.StillAway(9.6f, 10_000));
    }

    [Fact]
    public void StillAway_UnknownIdleCountsAsAway()
    {
        Assert.True(AwayCheck.StillAway(null, 10_000));
    }
}
