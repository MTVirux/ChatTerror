using System.Net.Http.Json;
using System.Security.Cryptography;
using ChatTerror.Protocol;
using ChatTerror.Server.Tests.Support;

namespace ChatTerror.Server.Tests;

public class TellRelayTests
{
    private static readonly string A = TellHash.Compute(1);
    private static readonly string B = TellHash.Compute(2);

    private sealed record Side(InstallResponse Install, ECDiffieHellman Key);

    // A and B are friends both ways, each with only the plugin in its bundle.
    private static async Task<(Side A, Side B)> FriendsAsync(RelayApp app)
    {
        var keyA = P256.Generate();
        var keyB = P256.Generate();
        var a = new Side(await app.RegisterInstallAsync(keyA), keyA);
        var b = new Side(await app.RegisterInstallAsync(keyB), keyB);
        await app.PutTellCharacterAsync(a.Install.InstallToken, A, B);
        await app.PutTellCharacterAsync(b.Install.InstallToken, B, A);
        await app.PutTellBundleAsync(a.Install.InstallToken, a.Key);
        await app.PutTellBundleAsync(b.Install.InstallToken, b.Key);
        return (a, b);
    }

    private static TellSendFrame Send(string id, string from, string to, params TellCopy[] copies) => new(id, from, to, copies);

    [Fact]
    public async Task Tell_ReachesOnlineRecipientPlugin()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);
        await using var pluginB = await app.ConnectAsync(b.Install.InstallToken);

        await pluginA.SendAsync(Send("t1", A, B, new TellCopy(false, TellTargets.Plugin, "env")));

        Assert.Equal(new TellResultFrame("t1", true), await pluginA.ReceiveAsync<TellResultFrame>());
        Assert.Equal(new TellFrame("t1", A, "env"), await pluginB.ReceiveAsync<TellFrame>());
    }

    [Fact]
    public async Task Tell_RefusedWhenRecipientDoesNotListSender()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await app.PutTellCharacterAsync(b.Install.InstallToken, B);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        await pluginA.SendAsync(Send("t1", A, B, new TellCopy(false, TellTargets.Plugin, "env")));

        Assert.Equal(new TellResultFrame("t1", false, TellErrors.NotFriend), await pluginA.ReceiveAsync<TellResultFrame>());
    }

    [Fact]
    public async Task Tell_RefusedForForeignSenderUnknownTargetAndBadCopies()
    {
        using var app = new RelayApp();
        var (a, _) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        await pluginA.SendAsync(Send("t1", B, A, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(TellErrors.NotOwner, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);

        await pluginA.SendAsync(Send("t2", A, TellHash.Compute(99), new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(TellErrors.NotChatTerror, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);

        await pluginA.SendAsync(Send("t3", A, B, new TellCopy(false, "not-in-bundle", "env")));
        Assert.Equal(TellErrors.BadCopies, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);

        await pluginA.SendAsync(Send("t4", A, B, new TellCopy(false, TellTargets.Plugin, new string('x', Limits.MaxTellEnvelopeChars + 1))));
        Assert.Equal(TellErrors.BadCopies, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);
    }

    [Fact]
    public async Task Tell_QueuedUntilAcked()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using (var pluginA = await app.ConnectAsync(a.Install.InstallToken))
        {
            await pluginA.SendAsync(Send("t1", A, B, new TellCopy(false, TellTargets.Plugin, "env1")));
            await pluginA.SendAsync(Send("t2", A, B, new TellCopy(false, TellTargets.Plugin, "env2")));
            await pluginA.ReceiveAsync<TellResultFrame>();
            await pluginA.ReceiveAsync<TellResultFrame>();
        }

        await using (var pluginB = await app.ConnectAsync(b.Install.InstallToken))
        {
            Assert.Equal("t1", (await pluginB.ReceiveAsync<TellFrame>()).Id);
            Assert.Equal("t2", (await pluginB.ReceiveAsync<TellFrame>()).Id);
            await pluginB.SendAsync(new TellAckFrame(["t1"]));
            await pluginB.BarrierAsync();
        }

        await using var again = await app.ConnectAsync(b.Install.InstallToken);
        Assert.Equal("t2", (await again.ReceiveAsync<TellFrame>()).Id);
    }

    [Fact]
    public async Task Tell_PushesOfflineDeviceAndSkipsSendersOwnConnection()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginB = await app.ConnectAsync(b.Install.InstallToken);
        var phoneB = await app.PairDeviceAsync(b.Install, pluginB);
        var push = await app.Client(phoneB.DeviceToken).PutAsJsonAsync("/api/devices/me/push", new { endpoint = "https://1.1.1.1/sub", keys = new { p256dh = "p256", auth = "secret" } });
        push.EnsureSuccessStatusCode();
        await app.PutTellBundleAsync(b.Install.InstallToken, b.Key, new TellBundleEntry(phoneB.DeviceId, RelayApp.NewPublicKey(), true));
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);
        var phoneA = await app.PairDeviceAsync(a.Install, pluginA);
        await app.PutTellBundleAsync(a.Install.InstallToken, a.Key, new TellBundleEntry(phoneA.DeviceId, RelayApp.NewPublicKey(), false));
        await using var phoneASocket = await app.ConnectAsync(phoneA.DeviceToken);

        await phoneASocket.SendAsync(Send("t1", A, B,
            new TellCopy(false, TellTargets.Plugin, "toB"),
            new TellCopy(false, phoneB.DeviceId, "toPhoneB"),
            new TellCopy(true, TellTargets.Plugin, "toA"),
            new TellCopy(true, phoneA.DeviceId, "toPhoneA")));

        Assert.True((await phoneASocket.ReceiveAsync<TellResultFrame>()).Ok);
        Assert.Equal("toA", (await pluginA.ReceiveAsync<TellFrame>()).Envelope);
        Assert.Equal("toB", (await pluginB.ReceiveAsync<TellFrame>()).Envelope);
        await app.Push.WaitForCallAsync();
        var (_, body) = Assert.Single(app.Push.Calls);
        Assert.Contains("\"t\":\"tell\"", body);
        Assert.Contains("toPhoneB", body);
        Assert.DoesNotContain(await phoneASocket.BarrierAsync(), f => f is TellFrame);
    }
}
