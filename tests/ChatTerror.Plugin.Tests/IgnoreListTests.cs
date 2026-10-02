using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class IgnoreListTests
{
    [Fact]
    public void Merge_AddsNewNamesAndCountsThem()
    {
        List<string> ignored = ["Bob Smith"];

        var added = IgnoreList.Merge(ignored, ["Amy Lee", " Cid Garlond ", ""]);

        Assert.Equal(2, added);
        Assert.Equal(["Bob Smith", "Amy Lee", "Cid Garlond"], ignored);
    }

    [Fact]
    public void Merge_SkipsExistingNamesIgnoringCase()
    {
        List<string> ignored = ["Bob Smith"];

        Assert.Equal(0, IgnoreList.Merge(ignored, ["bob smith"]));
        Assert.Single(ignored);
    }

    [Fact]
    public void Merge_KeepsWorldOverride()
    {
        List<string> ignored = ["Bob Smith@Twintania"];

        Assert.Equal(0, IgnoreList.Merge(ignored, ["Bob Smith"]));
        Assert.Equal(["Bob Smith@Twintania"], ignored);
    }
}
