using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

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

    [Fact]
    public void IdleSeconds_NoCharacterCountsAsAway()
    {
        Assert.Null(AwayCheck.IdleSeconds(false, 5f, 5f));
    }

    [Fact]
    public void IdleSeconds_UsesTheLargerTimer()
    {
        Assert.Equal(12f, AwayCheck.IdleSeconds(true, 12f, 3f));
        Assert.Equal(12f, AwayCheck.IdleSeconds(true, 3f, 12f));
    }

    [Fact]
    public void AlertWaitSetting_CapsDutyPops()
    {
        Assert.Equal(30, AwayCheck.AlertWaitSetting(AlertKind.DutyPop, 60));
        Assert.Equal(30, AwayCheck.AlertWaitSetting(AlertKind.DutyPop, 31));
        Assert.Equal(20, AwayCheck.AlertWaitSetting(AlertKind.DutyPop, 20));
        Assert.Equal(0, AwayCheck.AlertWaitSetting(AlertKind.DutyPop, 0));
    }

    [Fact]
    public void AlertWaitSetting_KeepsTheFullSettingForPartyFull()
    {
        Assert.Equal(60, AwayCheck.AlertWaitSetting(AlertKind.PartyFull, 60));
    }

    [Fact]
    public void AlertTtlSeconds_DutyPopCountsFromThePop()
    {
        Assert.Equal(45, AwayCheck.AlertTtlSeconds(AlertKind.DutyPop, 0));
        Assert.Equal(35, AwayCheck.AlertTtlSeconds(AlertKind.DutyPop, 10_000));
        Assert.Equal(15, AwayCheck.AlertTtlSeconds(AlertKind.DutyPop, 30_000));
        Assert.Equal(14, AwayCheck.AlertTtlSeconds(AlertKind.DutyPop, 30_016));
    }

    [Fact]
    public void AlertTtlSeconds_DutyPopKeepsAShortMinimum()
    {
        Assert.Equal(5, AwayCheck.AlertTtlSeconds(AlertKind.DutyPop, 40_000));
        Assert.Equal(5, AwayCheck.AlertTtlSeconds(AlertKind.DutyPop, 120_000));
    }

    [Fact]
    public void AlertTtlSeconds_PartyFullStays900()
    {
        Assert.Equal(900, AwayCheck.AlertTtlSeconds(AlertKind.PartyFull, 0));
        Assert.Equal(900, AwayCheck.AlertTtlSeconds(AlertKind.PartyFull, 60_000));
    }
}
