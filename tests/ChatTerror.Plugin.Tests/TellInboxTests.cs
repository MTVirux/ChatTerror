using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class TellInboxTests
{
    private static readonly TellCharacter Me = new()
    {
        Hash = "me", Name = "Main Char", World = "Twintania",
        Friends = [new TellFriend { Hash = "bob", Name = "Bob Smith", World = "Lich" }],
    };

    private static TellBody Body(long ts = 1000) => new("t1", "bob", "Forged Name", "Lich", "me", "Main Char", "Twintania", "hi", ts);

    [Fact]
    public void Inbox_HoldsTellsUntilLoggedIn()
    {
        var inbox = new TellInbox(10);
        inbox.Add(new TellFrame("a", "f", "e", "k"));

        Assert.Empty(inbox.Drain(loggedIn: false, max: 5));
        Assert.Equal(["a"], inbox.Drain(loggedIn: true, max: 5).Select(f => f.Id));
        Assert.Empty(inbox.Drain(loggedIn: true, max: 5));
    }

    [Fact]
    public void Inbox_DropsNewTellsWhenFullAndDrainsAFewAtATime()
    {
        var inbox = new TellInbox(3);
        foreach (var id in new[] { "a", "b", "c", "d" })
            inbox.Add(new TellFrame(id, "f", "e", "k"));

        Assert.Equal(["a", "b"], inbox.Drain(loggedIn: true, max: 2).Select(f => f.Id));
        Assert.Equal(["c"], inbox.Drain(loggedIn: true, max: 2).Select(f => f.Id));
    }

    [Fact]
    public void IsExpired_RefusesTellsOlderThanTheRelayKeepsThem()
    {
        var now = (long)Limits.TellTtl.TotalMilliseconds + 10_000;

        Assert.False(TellItems.IsExpired(Body(ts: 10_000), now));
        Assert.True(TellItems.IsExpired(Body(ts: 9_999), now));
        Assert.False(TellItems.IsExpired(Body(ts: now + 5000), now));
    }

    [Fact]
    public void Incoming_UsesTheFriendListNameAndNeverAFutureTime()
    {
        var item = TellItems.Incoming(Body(ts: 9_999_999), "bob", [Me], now: 5000);

        Assert.Equal(new ChatItem("t1", 5000, ChatChannel.Tell, "Bob Smith", "Lich", "hi", "Main Char", false), item);
        Assert.Equal(1000, TellItems.Incoming(Body(ts: 1000), "bob", [Me], now: 5000)!.Ts);
    }

    [Fact]
    public void Incoming_DropsUnknownSenders() => Assert.Null(TellItems.Incoming(Body(), "stranger", [Me], now: 5000));

    [Fact]
    public void Outgoing_ClampsTheTime()
    {
        var item = TellItems.Outgoing(Body(ts: 9_999_999), now: 5000);
        Assert.Equal(new ChatItem("t1", 5000, ChatChannel.Tell, "Main Char", "Twintania", "hi", "Forged Name", true), item);
    }

    [Fact]
    public void InFlight_DropAllReturnsEverythingOnce()
    {
        var inFlight = new InFlightTells<string>();
        inFlight.Add("a", "first");
        inFlight.Add("b", "second");

        Assert.Equal("first", inFlight.Complete("a"));
        Assert.Null(inFlight.Complete("a"));
        Assert.Equal(["second"], inFlight.DropAll());
        Assert.Empty(inFlight.DropAll());
    }

    [Fact]
    public void Contacts_CarryThePinnedKey()
    {
        var registered = new TellCharacter { Hash = "me", Name = "Main Char", World = "Twintania", Friends = Me.Friends, Registered = ["bob"] };

        var contacts = TellContacts.ForDevices([registered], new Dictionary<string, string> { ["bob"] = "key" });

        Assert.Equal("key", Assert.Single(contacts).Key);
        Assert.Null(Assert.Single(TellContacts.ForDevices([registered], new Dictionary<string, string>())).Key);
    }
}
