using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class TellRelayTests
{
    private sealed record Side(InstallResponse Install, ECDiffieHellman Key);

    // A and B are paired friends, each with only the plugin in its bundle.
    private static async Task<(Side A, Side B)> FriendsAsync(RelayApp app)
    {
        var keyA = P256.Generate();
        var keyB = P256.Generate();
        var a = new Side(await app.RegisterInstallAsync(keyA), keyA);
        var b = new Side(await app.RegisterInstallAsync(keyB), keyB);
        await app.PairFriendsAsync(a.Install, b.Install);
        await app.PutTellBundleAsync(a.Install.InstallToken, a.Key);
        await app.PutTellBundleAsync(b.Install.InstallToken, b.Key);
        return (a, b);
    }

    private static TellSendFrame Send(string id, Side to, params TellCopy[] copies) => new(id, to.Install.InstallId, copies);

    private static async Task TellAsync(TestSocket from, string id, Side to, params TellCopy[] copies)
    {
        await from.SendAsync(Send(id, to, copies));
        Assert.True((await from.ReceiveAsync<TellResultFrame>()).Ok);
    }

    // The phone stays offline, with a push subscription and a bundle entry that asks for pushes unless push is false.
    private static async Task<ClaimResponse> PushablePhoneAsync(RelayApp app, Side side, TestSocket plugin, bool push = true)
    {
        var phone = await app.PairDeviceAsync(side.Install, plugin);
        var response = await app.Client(phone.DeviceToken).PutAsJsonAsync("/api/devices/me/push", new { endpoint = "https://1.1.1.1/sub", keys = new { p256dh = "p256", auth = "secret" } });
        response.EnsureSuccessStatusCode();
        await app.PutTellBundleAsync(side.Install.InstallToken, side.Key, new TellBundleEntry(phone.DeviceId, RelayApp.NewPublicKey(), push));
        return phone;
    }

    private sealed record TellPushSetup(Side A, Side B, TestSocket PluginA, TestSocket PluginB, ClaimResponse PhoneB) : IAsyncDisposable
    {
        public TellCopy[] Copies => [new TellCopy(false, TellTargets.Plugin, "toB"), new TellCopy(false, PhoneB.DeviceId, "toPhoneB")];

        public async ValueTask DisposeAsync()
        {
            await PluginA.DisposeAsync();
            await PluginB.DisposeAsync();
        }
    }

    // Plugin B times its own tell pushes, and phone B is offline with pushes on unless push is false.
    private static async Task<TellPushSetup> TellPushSetupAsync(RelayApp app, bool push = true)
    {
        var (a, b) = await FriendsAsync(app);
        var pluginB = await app.ConnectAsync(b.Install.InstallToken, [RelayFeatures.TellPush]);
        var phoneB = await PushablePhoneAsync(app, b, pluginB, push);
        var pluginA = await app.ConnectAsync(a.Install.InstallToken);
        return new TellPushSetup(a, b, pluginA, pluginB, phoneB);
    }

    private static string? PushedTellId(string body) => JsonDocument.Parse(body).RootElement.GetProperty("i").GetString();

    [Fact]
    public async Task Tell_ReachesOnlineRecipientPlugin()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);
        await using var pluginB = await app.ConnectAsync(b.Install.InstallToken);

        await pluginA.SendAsync(Send("t1", b, new TellCopy(false, TellTargets.Plugin, "env")));

        Assert.Equal(new TellResultFrame("t1", true), await pluginA.ReceiveAsync<TellResultFrame>());
        Assert.Equal(new TellFrame("t1", a.Install.InstallId, "env", Base64Url.Encode(P256.PublicRaw(a.Key))), await pluginB.ReceiveAsync<TellFrame>());
    }

    [Fact]
    public async Task Tell_RefusedForStrangersAndWithoutABundle()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        using var keyC = P256.Generate();
        var c = new Side(await app.RegisterInstallAsync(keyC), keyC);
        await app.PutTellBundleAsync(c.Install.InstallToken, c.Key);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);
        await using var pluginC = await app.ConnectAsync(c.Install.InstallToken);

        await pluginA.SendAsync(Send("t1", c, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(new TellResultFrame("t1", false, TellErrors.NotPaired), await pluginA.ReceiveAsync<TellResultFrame>());
        await pluginA.SendAsync(Send("t2", a, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(TellErrors.NotPaired, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);
        await pluginA.SendAsync(new TellSendFrame("t3", "unknown-install", [new TellCopy(false, TellTargets.Plugin, "env")]));
        Assert.Equal(TellErrors.NotPaired, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);
        await pluginC.SendAsync(Send("t4", a, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(TellErrors.NotPaired, (await pluginC.ReceiveAsync<TellResultFrame>()).Error);

        await app.Client(b.Install.InstallToken).DeleteAsync("/api/tells/bundle");
        await pluginA.SendAsync(Send("t5", b, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(TellErrors.NotChatTerror, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);
    }

    [Fact]
    public async Task Tell_RefusedForBadCopies()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        await pluginA.SendAsync(Send("t3", b, new TellCopy(false, "not-in-bundle", "env")));
        Assert.Equal(TellErrors.BadCopies, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);

        await pluginA.SendAsync(Send("t4", b, new TellCopy(false, TellTargets.Plugin, new string('x', Limits.MaxTellEnvelopeChars + 1))));
        Assert.Equal(TellErrors.BadCopies, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);

        await pluginA.SendAsync(Send("t5", b, new TellCopy(false, TellTargets.Plugin, "env1"), new TellCopy(false, TellTargets.Plugin, "env2")));
        Assert.Equal(TellErrors.BadCopies, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);

        await pluginA.SendAsync(Send("", b, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(TellErrors.BadCopies, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);
    }

    [Fact]
    public async Task Tell_StopsOnceTheFriendIsRemoved()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        await app.Client(b.Install.InstallToken).DeleteAsync($"/api/friends/{a.Install.InstallId}");
        await pluginA.ReceiveAsync<FriendsChangedFrame>();
        await pluginA.SendAsync(Send("t1", b, new TellCopy(false, TellTargets.Plugin, "env")));

        Assert.Equal(TellErrors.NotPaired, (await pluginA.ReceiveAsync<TellResultFrame>()).Error);
    }

    [Fact]
    public async Task Tell_QueuedUntilAcked()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using (var pluginA = await app.ConnectAsync(a.Install.InstallToken))
        {
            await pluginA.SendAsync(Send("t1", b, new TellCopy(false, TellTargets.Plugin, "env1")));
            await pluginA.SendAsync(Send("t2", b, new TellCopy(false, TellTargets.Plugin, "env2")));
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
    public async Task QueuedTell_FromAnExFriendIsDroppedOnConnect()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using (var pluginA = await app.ConnectAsync(a.Install.InstallToken))
        {
            await pluginA.SendAsync(Send("t1", b, new TellCopy(false, TellTargets.Plugin, "env")));
            Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);
        }

        // Unpairs without clearing the queue, like an unfriend racing a tell that was already checked.
        using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = app.DbPath, Pooling = false }.ToString()))
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM friends";
            command.ExecuteNonQuery();
        }

        await using var pluginB = await app.ConnectAsync(b.Install.InstallToken);
        Assert.DoesNotContain(await pluginB.BarrierAsync(), f => f is TellFrame);
        Assert.Empty(app.Services.GetRequiredService<RelayStore>().PendingTells(b.Install.InstallId, TellTargets.Plugin));
    }

    [Fact]
    public async Task Tell_SelfCopyQueuesForOfflineOwnDevice()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);
        var phoneA = await app.PairDeviceAsync(a.Install, pluginA);
        await app.PutTellBundleAsync(a.Install.InstallToken, a.Key, new TellBundleEntry(phoneA.DeviceId, RelayApp.NewPublicKey(), false));

        await pluginA.SendAsync(Send("t1", b,
            new TellCopy(true, phoneA.DeviceId, "toPhoneA"),
            new TellCopy(false, TellTargets.Plugin, "toB")));
        Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);

        await using var phoneASocket = await app.ConnectAsync(phoneA.DeviceToken);
        Assert.Equal("toPhoneA", (await phoneASocket.ReceiveAsync<TellFrame>()).Envelope);
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

        await phoneASocket.SendAsync(Send("t1", b,
            new TellCopy(false, TellTargets.Plugin, "toB"),
            new TellCopy(false, phoneB.DeviceId, "toPhoneB"),
            new TellCopy(true, TellTargets.Plugin, "toA"),
            new TellCopy(true, phoneA.DeviceId, "toPhoneA")));

        Assert.True((await phoneASocket.ReceiveAsync<TellResultFrame>()).Ok);
        Assert.Equal("toA", (await pluginA.ReceiveAsync<TellFrame>()).Envelope);
        Assert.Equal("toB", (await pluginB.ReceiveAsync<TellFrame>()).Envelope);
        await app.Push.WaitForCallAsync();
        var (_, body, _) = Assert.Single(app.Push.Calls);
        Assert.Contains("\"t\":\"tell\"", body);
        Assert.Contains("toPhoneB", body);
        Assert.Contains($"\"f\":\"{a.Install.InstallId}\"", body);
        Assert.Contains($"\"k\":\"{Base64Url.Encode(P256.PublicRaw(a.Key))}\"", body);
        Assert.DoesNotContain(await phoneASocket.BarrierAsync(), f => f is TellFrame);
    }

    [Fact]
    public async Task Tell_RateLimitedGetsATellResult()
    {
        using var app = new RelayApp(new() { ["Relay:FramesPerSecond"] = "0.001", ["Relay:FrameBurst"] = "1" });
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        await pluginA.SendAsync(Send("t1", b, new TellCopy(false, TellTargets.Plugin, "env")));
        await pluginA.SendAsync(Send("t2", b, new TellCopy(false, TellTargets.Plugin, "env")));

        Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);
        Assert.Equal(new TellResultFrame("t2", false, TellErrors.RateLimited), await pluginA.ReceiveAsync<TellResultFrame>());
    }

    [Fact]
    public async Task QueuedTell_CarriesTheSendersInstallKey()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using (var pluginA = await app.ConnectAsync(a.Install.InstallToken))
        {
            await pluginA.SendAsync(Send("t1", b, new TellCopy(false, TellTargets.Plugin, "env")));
            await pluginA.ReceiveAsync<TellResultFrame>();
        }

        await using var pluginB = await app.ConnectAsync(b.Install.InstallToken);
        Assert.Equal(Base64Url.Encode(P256.PublicRaw(a.Key)), (await pluginB.ReceiveAsync<TellFrame>()).FromKey);
    }

    [Fact]
    public async Task Tell_RateLimitedPerSenderAndRecipient()
    {
        using var app = new RelayApp(new() { ["Relay:TellsPerRecipientPerMinute"] = "2" });
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        for (var i = 0; i < 2; i++)
        {
            await pluginA.SendAsync(Send($"t{i}", b, new TellCopy(false, TellTargets.Plugin, "env")));
            Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);
        }
        await pluginA.SendAsync(Send("t2", b, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.Equal(new TellResultFrame("t2", false, TellErrors.RateLimited), await pluginA.ReceiveAsync<TellResultFrame>());

        app.Time.Advance(TimeSpan.FromMinutes(1));
        await pluginA.SendAsync(Send("t3", b, new TellCopy(false, TellTargets.Plugin, "env")));
        Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);
    }

    [Fact]
    public async Task Tell_PushCooldownStillQueuesTheTell()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        ClaimResponse phoneB;
        await using (var pluginB = await app.ConnectAsync(b.Install.InstallToken))
            phoneB = await app.PairDeviceAsync(b.Install, pluginB);
        var push = await app.Client(phoneB.DeviceToken).PutAsJsonAsync("/api/devices/me/push", new { endpoint = "https://1.1.1.1/sub", keys = new { p256dh = "p256", auth = "secret" } });
        push.EnsureSuccessStatusCode();
        await app.PutTellBundleAsync(b.Install.InstallToken, b.Key, new TellBundleEntry(phoneB.DeviceId, RelayApp.NewPublicKey(), true));
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken);

        foreach (var id in new[] { "t1", "t2" })
        {
            await pluginA.SendAsync(Send(id, b, new TellCopy(false, phoneB.DeviceId, "env")));
            Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);
        }
        Assert.Single(app.Push.Calls);

        app.Time.Advance(TimeSpan.FromSeconds(10));
        await pluginA.SendAsync(Send("t3", b, new TellCopy(false, phoneB.DeviceId, "env")));
        Assert.True((await pluginA.ReceiveAsync<TellResultFrame>()).Ok);
        Assert.Equal(2, app.Push.Calls.Count);

        var store = app.Services.GetRequiredService<RelayStore>();
        Assert.Equal(["t1", "t2", "t3"], store.PendingTells(b.Install.InstallId, phoneB.DeviceId).Select(tell => tell.Id));
    }

    [Fact]
    public async Task Tell_PluginWithTellPushDefersThePush()
    {
        using var app = new RelayApp();
        await using var s = await TellPushSetupAsync(app);

        await TellAsync(s.PluginA, "t1", s.B, s.Copies);
        Assert.Equal("toB", (await s.PluginB.ReceiveAsync<TellFrame>()).Envelope);
        await s.PluginA.BarrierAsync();
        Assert.Empty(app.Push.Calls);

        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await app.Push.WaitForCallAsync();
        var call = Assert.Single(app.Push.Calls);
        var key = Base64Url.Encode(P256.PublicRaw(s.A.Key));
        Assert.Equal($"{{\"t\":\"tell\",\"i\":\"t1\",\"f\":\"{s.A.Install.InstallId}\",\"e\":\"toPhoneB\",\"k\":\"{key}\",\"d\":\"{s.PhoneB.DeviceId}\"}}", call.Body);
        Assert.Equal(86400, call.Ttl);
    }

    [Fact]
    public async Task TellPush_IgnoredForOnlineDevice()
    {
        using var app = new RelayApp();
        await using var s = await TellPushSetupAsync(app);
        await TellAsync(s.PluginA, "t1", s.B, s.Copies);

        await using (var phone = await app.ConnectAsync(s.PhoneB.DeviceToken))
        {
            await phone.ReceiveAsync<TellFrame>();
            await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
            await s.PluginB.BarrierAsync();
            Assert.Empty(app.Push.Calls);
        }

        await s.PluginB.ReceiveAsync<DeviceOfflineFrame>();
        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();
        Assert.Single(app.Push.Calls);
    }

    [Fact]
    public async Task TellPush_IgnoredForAckedTell()
    {
        using var app = new RelayApp();
        await using var s = await TellPushSetupAsync(app);
        await TellAsync(s.PluginA, "t1", s.B, s.Copies);
        await TellAsync(s.PluginA, "t2", s.B, s.Copies);
        await using (var phone = await app.ConnectAsync(s.PhoneB.DeviceToken))
        {
            await phone.SendAsync(new TellAckFrame(["t1"]));
            await phone.BarrierAsync();
        }
        await s.PluginB.ReceiveAsync<DeviceOfflineFrame>();

        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await s.PluginB.SendAsync(new TellPushFrame("t2", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();

        Assert.Equal("t2", PushedTellId(Assert.Single(app.Push.Calls).Body));
    }

    [Fact]
    public async Task TellPush_IgnoredForSelfCopy()
    {
        using var app = new RelayApp();
        var (a, b) = await FriendsAsync(app);
        await using var pluginA = await app.ConnectAsync(a.Install.InstallToken, [RelayFeatures.TellPush]);
        var phoneA = await PushablePhoneAsync(app, a, pluginA);
        await using var pluginB = await app.ConnectAsync(b.Install.InstallToken);
        await TellAsync(pluginA, "t1", b, new TellCopy(false, TellTargets.Plugin, "toB"), new TellCopy(true, phoneA.DeviceId, "toPhoneA"));
        await TellAsync(pluginB, "t2", a, new TellCopy(false, TellTargets.Plugin, "toA"), new TellCopy(false, phoneA.DeviceId, "fromB"));
        Assert.Empty(app.Push.Calls);

        await pluginA.SendAsync(new TellPushFrame("t1", phoneA.DeviceId));
        await pluginA.SendAsync(new TellPushFrame("t2", phoneA.DeviceId));
        await pluginA.BarrierAsync();

        Assert.Equal("t2", PushedTellId(Assert.Single(app.Push.Calls).Body));
    }

    [Fact]
    public async Task TellPush_IgnoredWhenTheEntryHasNoPush()
    {
        using var app = new RelayApp();
        await using var s = await TellPushSetupAsync(app, push: false);
        await TellAsync(s.PluginA, "t1", s.B, s.Copies);

        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();
        Assert.Empty(app.Push.Calls);

        await app.PutTellBundleAsync(s.B.Install.InstallToken, s.B.Key, new TellBundleEntry(s.PhoneB.DeviceId, RelayApp.NewPublicKey(), true));
        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();
        Assert.Single(app.Push.Calls);
    }

    [Fact]
    public async Task TellPush_IgnoredForAnotherInstallsDevice()
    {
        using var app = new RelayApp();
        await using var s = await TellPushSetupAsync(app);
        var other = await app.RegisterInstallAsync();
        await using var pluginC = await app.ConnectAsync(other.InstallToken, [RelayFeatures.TellPush]);
        await TellAsync(s.PluginA, "t1", s.B, s.Copies);

        await pluginC.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        Assert.DoesNotContain(await pluginC.BarrierAsync(), f => f is ErrorFrame);
        Assert.Empty(app.Push.Calls);

        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();
        Assert.Single(app.Push.Calls);
    }

    [Fact]
    public async Task TellPush_RespectsThePushCooldown()
    {
        using var app = new RelayApp();
        await using var s = await TellPushSetupAsync(app);
        await TellAsync(s.PluginA, "t1", s.B, s.Copies);
        await TellAsync(s.PluginA, "t2", s.B, s.Copies);

        await s.PluginB.SendAsync(new TellPushFrame("t1", s.PhoneB.DeviceId));
        await s.PluginB.SendAsync(new TellPushFrame("t2", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();
        Assert.Single(app.Push.Calls);

        app.Time.Advance(TimeSpan.FromSeconds(10));
        await s.PluginB.SendAsync(new TellPushFrame("t2", s.PhoneB.DeviceId));
        await s.PluginB.BarrierAsync();
        Assert.Equal(["t1", "t2"], app.Push.Calls.Select(call => PushedTellId(call.Body)));
    }
}
