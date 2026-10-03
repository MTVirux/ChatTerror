using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class FriendRefreshScheduleTests
{
    [Fact]
    public void TryStart_RunsFirstRefreshRightAway()
    {
        var schedule = new FriendRefreshSchedule();

        Assert.True(schedule.TryStart(1_000));
        Assert.False(schedule.TryStart(1_001));
    }

    [Fact]
    public void TryStart_RequestIsNotDelayed()
    {
        var schedule = new FriendRefreshSchedule();
        schedule.TryStart(1_000);

        schedule.Request();

        Assert.True(schedule.TryStart(1_001));
    }

    [Fact]
    public void TryStart_CoalescesChangesWithinCooldown()
    {
        var schedule = new FriendRefreshSchedule();
        schedule.TryStart(1_000);

        schedule.Changed();
        schedule.Changed();
        schedule.Changed();

        Assert.False(schedule.TryStart(1_000 + FriendRefreshSchedule.ChangedCooldownMs - 1));
        Assert.True(schedule.TryStart(1_000 + FriendRefreshSchedule.ChangedCooldownMs));
        Assert.False(schedule.TryStart(1_000 + 2 * FriendRefreshSchedule.ChangedCooldownMs));
    }

    [Fact]
    public void TryStart_RefreshesPeriodicallyWithoutChanges()
    {
        var schedule = new FriendRefreshSchedule();
        schedule.TryStart(1_000);

        Assert.False(schedule.TryStart(1_000 + FriendRefreshSchedule.IntervalMs - 1));
        Assert.True(schedule.TryStart(1_000 + FriendRefreshSchedule.IntervalMs));
    }
}
