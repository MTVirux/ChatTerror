using ChatTerror.Plugin.Logic;

namespace ChatTerror.Plugin.Tests;

public class SaveDebounceTests
{
    [Fact]
    public void TakeDue_OnlyWhenDirty()
    {
        var save = new SaveDebounce(5_000);

        Assert.False(save.TakeDue(1_000));
        save.MarkDirty();
        Assert.True(save.TakeDue(1_000));
        Assert.False(save.Dirty);
        Assert.False(save.TakeDue(10_000));
    }

    [Fact]
    public void TakeDue_HoldsChangesUntilTheIntervalPassed()
    {
        var save = new SaveDebounce(5_000);
        save.MarkDirty();
        Assert.True(save.TakeDue(1_000));

        save.MarkDirty();
        Assert.False(save.TakeDue(5_999));
        Assert.True(save.Dirty);
        Assert.True(save.TakeDue(6_000));
        Assert.False(save.Dirty);
    }
}
