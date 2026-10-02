using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class ReplyTrackerTests
{
    [Fact]
    public void Reply_ToRelayedTell_BecomesTell()
    {
        var tracker = new ReplyTracker();
        tracker.Incoming(new TellTarget("Bob Smith", "Lich"), relayed: true);

        Assert.Equal("/tell Bob Smith@Lich see you", tracker.Rewrite("/r see you"));
        Assert.Null(tracker.Rewrite("/p hello"));
    }

    [Fact]
    public void Reply_ToGameTell_IsLeftToTheGame()
    {
        var tracker = new ReplyTracker();
        tracker.Incoming(new TellTarget("Bob Smith", "Lich"), relayed: true);
        tracker.Incoming(new TellTarget("Cid Garlond", "Lich"), relayed: false);

        Assert.Null(tracker.Rewrite("/r hi"));
    }

    [Fact]
    public void Reply_WithNothingReceived_IsLeftToTheGame() => Assert.Null(new ReplyTracker().Rewrite("/r hi"));
}
