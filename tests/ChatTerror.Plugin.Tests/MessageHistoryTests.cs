using ChatTerror.Plugin.Logic;
using ChatTerror.Protocol;

namespace ChatTerror.Plugin.Tests;

public class MessageHistoryTests
{
    private static ChatItem Item(long ts, string character = "Alex Doe", ChatChannel channel = ChatChannel.Say, string sender = "Bob Smith", bool outgoing = false) =>
        new(Guid.NewGuid().ToString("N"), ts, channel, sender, "Gilgamesh", $"m{ts}", character, outgoing);

    [Fact]
    public void Add_OverCapacity_DropsOldest()
    {
        var h = new MessageHistory(3);
        for (var ts = 1; ts <= 5; ts++)
            h.Add(Item(ts));

        Assert.Equal([3L, 4L, 5L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Since_IsStrictlyGreater_OldestFirst()
    {
        var h = new MessageHistory(10);
        for (var ts = 1; ts <= 5; ts++)
            h.Add(Item(ts * 10));

        Assert.Equal([40L, 50L], h.Since(30).Select(i => i.Ts));
        Assert.Empty(h.Since(50));
    }

    [Fact]
    public void ShrinkCapacity_DropsOldest()
    {
        var h = new MessageHistory(5);
        for (var ts = 1; ts <= 5; ts++)
            h.Add(Item(ts));

        h.Capacity = 2;

        Assert.Equal(2, h.Capacity);
        Assert.Equal([4L, 5L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void GrowCapacity_KeepsAll()
    {
        var h = new MessageHistory(2);
        h.Add(Item(1));
        h.Add(Item(2));

        h.Capacity = 4;
        h.Add(Item(3));
        h.Add(Item(4));

        Assert.Equal([1L, 2L, 3L, 4L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Clear_Empties()
    {
        var h = new MessageHistory(5);
        h.Add(Item(1));

        h.Clear();

        Assert.Empty(h.Since(0));
    }

    [Fact]
    public void ConcurrentAdds_StayWithinCapacity()
    {
        var h = new MessageHistory(100);

        Parallel.For(0, 1000, i => h.Add(Item(i)));

        Assert.Equal(100, h.Since(-1).Count);
    }

    [Fact]
    public void Capacity_IsPerCharacter()
    {
        var h = new MessageHistory(2);
        h.Add(Item(1, "Alex Doe"));
        h.Add(Item(2, "Alex Doe"));
        h.Add(Item(3, "Sam Roe"));
        h.Add(Item(4, "Sam Roe"));
        h.Add(Item(5, "Sam Roe"));

        Assert.Equal([1L, 2L, 4L, 5L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Capacity_IsPerChannel()
    {
        var h = new MessageHistory(2);
        h.Add(Item(1, channel: ChatChannel.Party));
        h.Add(Item(2, channel: ChatChannel.Say));
        h.Add(Item(3, channel: ChatChannel.Say));
        h.Add(Item(4, channel: ChatChannel.Say));

        Assert.Equal([1L, 3L, 4L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Capacity_IsPerTellPartner_BothDirections()
    {
        var h = new MessageHistory(2);
        h.Add(Item(1, channel: ChatChannel.Tell, sender: "Bob Smith"));
        h.Add(Item(2, channel: ChatChannel.Tell, sender: "Cat Lee"));
        h.Add(Item(3, channel: ChatChannel.Tell, sender: "Cat Lee", outgoing: true));
        h.Add(Item(4, channel: ChatChannel.Tell, sender: "cat lee"));

        Assert.Equal([1L, 3L, 4L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Since_MergesCharactersInTimeOrder()
    {
        var h = new MessageHistory(10);
        h.Add(Item(3, "Sam Roe"));
        h.Add(Item(1, "Alex Doe"));
        h.Add(Item(2, "Sam Roe"));

        Assert.Equal([1L, 2L, 3L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Constructor_RestoresItemsWithinCapacity()
    {
        var h = new MessageHistory(2, [Item(1), Item(2), Item(3), Item(4, "Sam Roe")]);

        Assert.Equal([2L, 3L, 4L], h.Since(0).Select(i => i.Ts));
    }

    [Fact]
    public void Version_ChangesOnEveryMutation()
    {
        var h = new MessageHistory(5);
        var v0 = h.Version;
        h.Add(Item(1));
        var v1 = h.Version;
        h.Capacity = 1;
        var v2 = h.Version;
        h.Clear();

        Assert.True(v0 < v1 && v1 < v2 && v2 < h.Version);
    }

    [Fact]
    public void Characters_AreSortedAndDistinct()
    {
        var h = new MessageHistory(10);
        h.Add(Item(1, "Sam Roe"));
        h.Add(Item(2, "Alex Doe"));
        h.Add(Item(3, "Sam Roe"));

        Assert.Equal(["Alex Doe", "Sam Roe"], h.Characters);
    }

    [Fact]
    public void For_ReturnsOnlyThatCharacter_OldestFirst()
    {
        var h = new MessageHistory(10, [Item(3, "Alex Doe"), Item(2, "Sam Roe"), Item(1, "Alex Doe")]);

        Assert.Equal([1L, 3L], h.For("Alex Doe").Select(i => i.Ts));
        Assert.Empty(h.For("Nobody"));
    }
}
