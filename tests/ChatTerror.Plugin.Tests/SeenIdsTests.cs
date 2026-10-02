using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class SeenIdsTests
{
    [Fact]
    public void Add_ReportsDuplicatesAndForgetsOldest()
    {
        var seen = new SeenIds(2);

        Assert.True(seen.Add("a"));
        Assert.False(seen.Add("a"));
        Assert.True(seen.Add("b"));
        Assert.True(seen.Add("c"));
        Assert.True(seen.Add("a"));
    }

    [Fact]
    public void Ids_RoundTripOldestFirst()
    {
        var seen = new SeenIds(2, ["a", "b", "c"]);

        Assert.Equal(["b", "c"], seen.Ids);
        Assert.False(seen.Add("c"));
        Assert.True(seen.Add("a"));
    }
}
