using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class TellContactsTests
{
    private static TellFriend Bob => new() { Hash = TellHash.Compute(10), Name = "Bob Smith", World = "Lich" };

    private static TellCharacter Main => new()
    {
        Hash = TellHash.Compute(1), Name = "Main Char", World = "Twintania",
        Friends = [Bob], Registered = [Bob.Hash],
    };

    private static TellCharacter Alt => new()
    {
        Hash = TellHash.Compute(2), Name = "Alt Char", World = "Lich",
        Friends = [Bob, new TellFriend { Hash = TellHash.Compute(11), Name = "Cid Garlond", World = "Lich" }],
        Registered = [Bob.Hash],
    };

    [Fact]
    public void Route_PrefersTheLoggedInCharacter()
    {
        var route = TellContacts.Route([Main, Alt], Alt.Hash, new TellTarget("Bob Smith", "Lich"));
        Assert.Equal(Alt.Hash, route!.From.Hash);
    }

    [Fact]
    public void Route_FallsBackToAnAltThatIsFriends()
    {
        var other = new TellCharacter { Hash = TellHash.Compute(3), Name = "Other", World = "Lich" };
        var route = TellContacts.Route([other, Main], other.Hash, new TellTarget("bob smith", "lich"));
        Assert.Equal(Main.Hash, route!.From.Hash);
    }

    [Fact]
    public void Route_IgnoresFriendsThatAreNotRegistered()
    {
        Assert.Null(TellContacts.Route([Alt], Alt.Hash, new TellTarget("Cid Garlond", "Lich")));
        Assert.Null(TellContacts.Route([Alt], Alt.Hash, new TellTarget("Nobody Here", "Lich")));
    }

    [Fact]
    public void Friend_LooksUpByHashForThatCharacterOnly()
    {
        Assert.Equal("Bob Smith", TellContacts.Friend([Main, Alt], Main.Hash, Bob.Hash)!.Name);
        Assert.Null(TellContacts.Friend([Main, Alt], Main.Hash, TellHash.Compute(11)));
    }

    [Fact]
    public void ForDevices_ListsRegisteredFriendsPerCharacter()
    {
        var contacts = TellContacts.ForDevices([Main, Alt], new Dictionary<string, string>());

        Assert.Equal(2, contacts.Count);
        Assert.Equal(new TellContact("Main Char", "Twintania", Main.Hash, "Bob Smith", "Lich", Bob.Hash), contacts[0]);
    }

    [Fact]
    public void Uploadable_LeavesOutIgnoredFriends()
    {
        var settings = new RelaySettings { IgnoredSenders = ["Cid Garlond@Lich"] };
        Assert.Equal([Bob.Hash], TellContacts.Uploadable(Alt, settings));
    }

    [Fact]
    public void Sender_IsPinnedOnFirstTellAndRefusedWhenItsKeyChanges()
    {
        var pins = new Dictionary<string, string>();

        Assert.Equal(SenderTrust.Pinned, TellContacts.TrustSender(pins, "bob", "key1"));
        Assert.Equal("key1", pins["bob"]);
        Assert.Equal(SenderTrust.Trusted, TellContacts.TrustSender(pins, "bob", "key1"));
        Assert.Equal(SenderTrust.KeyChanged, TellContacts.TrustSender(pins, "bob", "key2"));
        Assert.Equal("key1", pins["bob"]);
    }
}
