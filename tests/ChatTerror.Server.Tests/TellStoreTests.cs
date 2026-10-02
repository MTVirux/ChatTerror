using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class TellStoreTests
{
    private static readonly string A = TellHash.Compute(1);
    private static readonly string B = TellHash.Compute(2);
    private static readonly string C = TellHash.Compute(3);

    [Fact]
    public void Character_MovesWhenTheOwnerInstallIsUnknown()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        store.SetTellCharacter("install1", A, [B]);
        Assert.True(store.SetTellCharacter("install2", A, []));

        Assert.Equal("install2", store.TellCharacterOwner(A));
        Assert.False(store.IsTellFriend(A, B));
        Assert.Null(store.TellCharacterOwner(B));
    }

    [Fact]
    public void Friends_AndRegisteredLookup()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        store.SetTellCharacter("install1", A, [B]);

        Assert.True(store.IsTellFriend(A, B));
        Assert.False(store.IsTellFriend(B, A));
        Assert.Empty(store.MutualTellFriends(A, [B]));

        store.DeleteTellCharacters("install1");
        Assert.Null(store.TellCharacterOwner(A));
    }

    [Fact]
    public void MutualFriends_NeedBothSidesToListEachOther()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        store.SetTellCharacter("install1", A, [B, C]);
        store.SetTellCharacter("install2", B, [A]);
        store.SetTellCharacter("install3", C, []);

        Assert.Equal([B], store.MutualTellFriends(A, [B, C]));
        Assert.True(store.CanSeeTellCharacter("install1", A));
        Assert.True(store.CanSeeTellCharacter("install1", B));
        Assert.False(store.CanSeeTellCharacter("install1", C));
        Assert.True(store.CanSeeTellCharacter("install2", A));
        Assert.False(store.CanSeeTellCharacter("install3", A));
    }

    [Fact]
    public void Queue_KeepsNewestAndAckRemoves()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        for (var i = 0; i < Limits.MaxQueuedTells + 5; i++)
        {
            app.Time.Advance(TimeSpan.FromMilliseconds(1));
            store.EnqueueTell($"id{i}", "install1", TellTargets.Plugin, TellHash.Compute((ulong)i + 100), "key", $"env{i}");
        }
        store.EnqueueTell("other", "install1", "device1", A, "key", "env");

        var pending = store.PendingTells("install1", TellTargets.Plugin);
        Assert.Equal(Limits.MaxQueuedTells, pending.Count);
        Assert.Equal(new TellFrame("id5", TellHash.Compute(105), "env5", "key"), pending[0]);

        store.AckTells("install1", TellTargets.Plugin, ["id5", "other"]);
        Assert.Equal(Limits.MaxQueuedTells - 1, store.PendingTells("install1", TellTargets.Plugin).Count);
        Assert.Single(store.PendingTells("install1", "device1"));
    }

    [Fact]
    public void Queue_CapsEachSenderWithoutEvictingOthers()
    {
        using var app = new RelayApp(new() { ["Relay:MaxQueuedTellsPerSender"] = "3" });
        var store = app.Services.GetRequiredService<RelayStore>();

        store.EnqueueTell("b1", "install1", TellTargets.Plugin, B, "key", "env");
        for (var i = 0; i < 10; i++)
        {
            app.Time.Advance(TimeSpan.FromMilliseconds(1));
            store.EnqueueTell($"a{i}", "install1", TellTargets.Plugin, A, "key", "env");
        }
        store.EnqueueTell("a0", "install1", "device1", A, "key", "env");

        Assert.Equal(["b1", "a7", "a8", "a9"], store.PendingTells("install1", TellTargets.Plugin).Select(tell => tell.Id));
        Assert.Single(store.PendingTells("install1", "device1"));
    }

    [Fact]
    public void Queue_ExpiresAfterTtl()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();
        store.EnqueueTell("id", "install1", TellTargets.Plugin, A, "key", "env");

        app.Time.Advance(Limits.TellTtl - TimeSpan.FromMinutes(1));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.Single(store.PendingTells("install1", TellTargets.Plugin));

        app.Time.Advance(TimeSpan.FromMinutes(2));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.Empty(store.PendingTells("install1", TellTargets.Plugin));
    }

    [Fact]
    public void Bundle_IsReplaced()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        store.SetTellBundle("install1", new SignedTellBundle("a", "b"));
        store.SetTellBundle("install1", new SignedTellBundle("c", "d"));

        Assert.Equal(new SignedTellBundle("c", "d"), store.FindTellBundle("install1"));
        Assert.Null(store.FindTellBundle("install2"));
    }
}
