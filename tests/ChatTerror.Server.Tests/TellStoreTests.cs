using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class TellStoreTests
{
    private const string A = "senderA";
    private const string B = "senderB";

    [Fact]
    public void Queue_KeepsNewestAndAckRemoves()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        for (var i = 0; i < Limits.MaxQueuedTells + 5; i++)
        {
            app.Time.Advance(TimeSpan.FromMilliseconds(1));
            store.AddFriend("install1", $"sender{i}");
            store.EnqueueTell($"id{i}", "install1", TellTargets.Plugin, $"sender{i}", "key", $"env{i}");
        }
        store.AddFriend("install1", A);
        store.EnqueueTell("other", "install1", "device1", A, "key", "env");

        var pending = store.PendingTells("install1", TellTargets.Plugin);
        Assert.Equal(Limits.MaxQueuedTells, pending.Count);
        Assert.Equal(new TellFrame("id5", "sender5", "env5", "key"), pending[0]);

        store.AckTells("install1", TellTargets.Plugin, ["id5", "other"]);
        Assert.Equal(Limits.MaxQueuedTells - 1, store.PendingTells("install1", TellTargets.Plugin).Count);
        Assert.Single(store.PendingTells("install1", "device1"));
    }

    [Fact]
    public void Queue_CapsEachSenderWithoutEvictingOthers()
    {
        using var app = new RelayApp(new() { ["Relay:MaxQueuedTellsPerSender"] = "3" });
        var store = app.Services.GetRequiredService<RelayStore>();
        store.AddFriend("install1", A);
        store.AddFriend("install1", B);

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
        store.AddFriend("install1", A);
        store.EnqueueTell("id", "install1", TellTargets.Plugin, A, "key", "env");

        app.Time.Advance(Limits.TellTtl - TimeSpan.FromMinutes(1));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.Single(store.PendingTells("install1", TellTargets.Plugin));

        app.Time.Advance(TimeSpan.FromMinutes(2));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.Empty(store.PendingTells("install1", TellTargets.Plugin));
    }

    [Fact]
    public void Queue_DeletesTellsBetweenTwoInstallsOnly()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();
        store.AddFriend("installA", "installB");
        store.AddFriend("installB", "installC");
        store.EnqueueTell("ab", "installB", TellTargets.Plugin, "installA", "key", "env");
        store.EnqueueTell("ab", "installB", "device1", "installA", "key", "env");
        store.EnqueueTell("ba", "installA", TellTargets.Plugin, "installB", "key", "env");
        store.EnqueueTell("cb", "installB", TellTargets.Plugin, "installC", "key", "env");
        store.EnqueueTell("self", "installA", "device2", "installA", "key", "env");

        store.DeleteQueuedTellsBetween("installA", "installB");

        Assert.Equal(["cb"], store.PendingTells("installB", TellTargets.Plugin).Select(tell => tell.Id));
        Assert.Empty(store.PendingTells("installB", "device1"));
        Assert.Empty(store.PendingTells("installA", TellTargets.Plugin));
        Assert.Single(store.PendingTells("installA", "device2"));
    }

    [Fact]
    public void Queue_RefusesTellsFromExFriendsButKeepsSelfCopies()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();
        store.AddFriend("installA", "installB");
        store.RemoveFriend("installA", "installB");

        Assert.False(store.EnqueueTell("ab", "installB", TellTargets.Plugin, "installA", "key", "env"));
        Assert.True(store.EnqueueTell("self", "installA", "device1", "installA", "key", "env"));

        Assert.Empty(store.PendingTells("installB", TellTargets.Plugin));
        Assert.Single(store.PendingTells("installA", "device1"));
    }

    [Fact]
    public void Bundle_IsReplacedAndDeleted()
    {
        using var app = new RelayApp();
        var store = app.Services.GetRequiredService<RelayStore>();

        store.SetTellBundle("install1", new SignedTellBundle("a", "b"));
        store.SetTellBundle("install1", new SignedTellBundle("c", "d"));

        Assert.Equal(new SignedTellBundle("c", "d"), store.FindTellBundle("install1"));
        Assert.Null(store.FindTellBundle("install2"));

        store.DeleteTellBundle("install1");
        Assert.Null(store.FindTellBundle("install1"));
    }
}
