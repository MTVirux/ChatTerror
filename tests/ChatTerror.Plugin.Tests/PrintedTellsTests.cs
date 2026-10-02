using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class PrintedTellsTests
{
    [Fact]
    public void Take_MatchesEachPrintOnce()
    {
        var printed = new PrintedTells(10);
        printed.Add(PrintedTells.Key(false, "hi"));

        Assert.False(printed.Take(PrintedTells.Key(true, "hi")));
        Assert.True(printed.Take(PrintedTells.Key(false, "hi")));
        Assert.False(printed.Take(PrintedTells.Key(false, "hi")));
    }

    [Fact]
    public void Add_ForgetsTheOldestPrintWhenFull()
    {
        var printed = new PrintedTells(2);
        printed.Add("a");
        printed.Add("b");
        printed.Add("c");

        Assert.False(printed.Take("a"));
        Assert.True(printed.Take("b"));
        Assert.True(printed.Take("c"));
    }
}
