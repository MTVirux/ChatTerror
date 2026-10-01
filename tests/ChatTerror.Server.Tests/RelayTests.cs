using System.Net;
using System.Net.Http.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class RelayTests
{
    private static async Task SubscribePushAsync(RelayApp app, ClaimResponse device, string endpoint = "https://push.example/sub")
    {
        var response = await app.Client(device.DeviceToken).PutAsJsonAsync("/api/devices/me/push",
            new { endpoint, keys = new { p256dh = "p256", auth = "secret" } });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Approve_SendsPairedToDevice_AndEnablesSend()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin, approve: false);
        await using var phone = await app.ConnectAsync(device.DeviceToken);
        Assert.True((await phone.ReceiveAsync<PluginStatusFrame>()).Online);

        await plugin.SendAsync(new PairDecisionFrame(device.DeviceId, true));

        await phone.ReceiveAsync<PairedFrame>();
        var me = (await app.Client(device.DeviceToken).GetFromJsonAsync<DeviceMe>("/api/devices/me"))!;
        Assert.Equal("active", me.Status);

        await phone.SendAsync(new SendFrame(Payload: "hello"));
        Assert.Equal(new MsgFrame(device.DeviceId, "hello"), await plugin.ReceiveAsync<MsgFrame>());
    }

    [Fact]
    public async Task Reject_DeletesDevice_AndSendsRevoked()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin, approve: false);
        await using var phone = await app.ConnectAsync(device.DeviceToken);

        await plugin.SendAsync(new PairDecisionFrame(device.DeviceId, false));

        var frames = await phone.ExpectClosedAsync();
        Assert.Contains(frames, f => f is RevokedFrame);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(device.DeviceToken).GetAsync("/api/devices/me")).StatusCode);
    }

    [Fact]
    public async Task PendingDevice_Send_NotApproved()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin, approve: false);
        await using var phone = await app.ConnectAsync(device.DeviceToken);

        await phone.SendAsync(new SendFrame(Payload: "hello"));

        Assert.Equal(RelayErrors.NotApproved, (await phone.ReceiveAsync<ErrorFrame>()).Code);
    }

    [Fact]
    public async Task Plugin_SendToPendingDevice_NotApproved()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin, approve: false);

        await plugin.SendAsync(new SendFrame(device.DeviceId, "hello"));

        Assert.Equal(RelayErrors.NotApproved, (await plugin.ReceiveAsync<ErrorFrame>()).Code);
    }

    [Fact]
    public async Task Plugin_SendToOnlineDevice_DeliversMsg()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(device.DeviceToken);
        Assert.Equal(device.DeviceId, (await plugin.ReceiveAsync<DeviceOnlineFrame>()).DeviceId);

        await plugin.SendAsync(new SendFrame(device.DeviceId, "cipher", Notify: true));

        Assert.Equal(new MsgFrame("plugin", "cipher"), await phone.ReceiveAsync<MsgFrame>());
        Assert.Empty(app.Push.Calls);
    }

    [Fact]
    public async Task Device_Send_DeliversToPlugin_WithFrom()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(device.DeviceToken);

        await phone.SendAsync(new SendFrame(Payload: "from-phone"));

        Assert.Equal(new MsgFrame(device.DeviceId, "from-phone"), await plugin.ReceiveAsync<MsgFrame>());
    }

    [Fact]
    public async Task Plugin_SendNotify_OfflineDeviceWithPush_SendsPush()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await SubscribePushAsync(app, device);

        await plugin.SendAsync(new SendFrame(device.DeviceId, "cipher", Notify: true));

        await app.Push.WaitForCallAsync();
        var (subscription, body) = Assert.Single(app.Push.Calls);
        Assert.Equal("https://push.example/sub", subscription.Endpoint);
        Assert.Equal("p256", subscription.P256dh);
        Assert.Equal("secret", subscription.Auth);
        Assert.Equal("{\"p\":\"cipher\"}", body);
    }

    [Fact]
    public async Task NonNotify_Offline_NoPush()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await SubscribePushAsync(app, device);

        await plugin.SendAsync(new SendFrame(device.DeviceId, "cipher"));
        await plugin.BarrierAsync();

        Assert.Empty(app.Push.Calls);
    }

    [Fact]
    public async Task PushGone_ClearsSubscription()
    {
        using var app = new RelayApp();
        app.Push.Result = Push.PushResult.Gone;
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await SubscribePushAsync(app, device);
        var store = app.Services.GetRequiredService<RelayStore>();

        await plugin.SendAsync(new SendFrame(device.DeviceId, "cipher", Notify: true));
        await app.Push.WaitForCallAsync();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (store.FindDevice(device.DeviceId)!.Push != null && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Null(store.FindDevice(device.DeviceId)!.Push);
    }

    [Fact]
    public async Task DeviceStatus_PluginConnectDisconnect_Broadcasts()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));
        await using var phone = await app.ConnectAsync(device.DeviceToken);
        Assert.False((await phone.ReceiveAsync<PluginStatusFrame>()).Online);

        var plugin = await app.ConnectAsync(install.InstallToken);
        Assert.True((await phone.ReceiveAsync<PluginStatusFrame>()).Online);

        await plugin.DisposeAsync();
        Assert.False((await phone.ReceiveAsync<PluginStatusFrame>()).Online);
    }

    [Fact]
    public async Task DeviceDisconnect_NotifiesPlugin()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);

        var phone = await app.ConnectAsync(device.DeviceToken);
        await plugin.ReceiveAsync<DeviceOnlineFrame>();
        await phone.DisposeAsync();

        Assert.Equal(device.DeviceId, (await plugin.ReceiveAsync<DeviceOfflineFrame>()).DeviceId);
    }

    [Fact]
    public async Task Revoke_ClosesDeviceSocket_AndTokenRejected()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(device.DeviceToken);

        var response = await app.Client(install.InstallToken).DeleteAsync($"/api/devices/{device.DeviceId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var frames = await phone.ExpectClosedAsync();
        Assert.Contains(frames, f => f is RevokedFrame);
        Assert.Equal(device.DeviceId, (await plugin.ReceiveAsync<DeviceRevokedFrame>()).DeviceId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(device.DeviceToken).GetAsync("/api/devices/me")).StatusCode);

        await using var retry = await app.OpenSocketAsync();
        await retry.SendAsync(new AuthFrame(device.DeviceToken));
        Assert.IsType<AuthFailFrame>(await retry.NextAsync());
        await retry.ExpectClosedAsync();
    }

    [Fact]
    public async Task Plugin_CannotSendToOtherInstallsDevice()
    {
        using var app = new RelayApp();
        var installA = await app.RegisterInstallAsync();
        var installB = await app.RegisterInstallAsync();
        await using var pluginA = await app.ConnectAsync(installA.InstallToken);
        await using var pluginB = await app.ConnectAsync(installB.InstallToken);
        var deviceB = await app.PairDeviceAsync(installB, pluginB);
        await using var phoneB = await app.ConnectAsync(deviceB.DeviceToken);

        await pluginA.SendAsync(new SendFrame(deviceB.DeviceId, "intrusion"));
        Assert.Equal(RelayErrors.UnknownDevice, (await pluginA.ReceiveAsync<ErrorFrame>()).Code);

        await pluginA.SendAsync(new PairDecisionFrame(deviceB.DeviceId, false));
        Assert.Equal(RelayErrors.UnknownDevice, (await pluginA.ReceiveAsync<ErrorFrame>()).Code);

        await pluginB.SendAsync(new SendFrame(deviceB.DeviceId, "legit"));
        Assert.Equal(new MsgFrame("plugin", "legit"), await phoneB.ReceiveAsync<MsgFrame>());
    }

    [Fact]
    public async Task BadFrame_ReturnsBadFrame()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);

        await plugin.SendRawAsync("not json");
        Assert.Equal(RelayErrors.BadFrame, (await plugin.ReceiveAsync<ErrorFrame>()).Code);

        await plugin.SendRawAsync("{\"t\":\"send\",\"payload\":\"x\"}");
        Assert.Equal(RelayErrors.BadFrame, (await plugin.ReceiveAsync<ErrorFrame>()).Code);
    }

    [Fact]
    public async Task OversizeFrame_TooLarge()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);

        await plugin.SendRawAsync(ProtocolJson.Serialize<RelayFrame>(new SendFrame("x", new string('a', Limits.MaxFrameBytes))));

        var frames = await plugin.ExpectClosedAsync();
        Assert.Contains(new ErrorFrame(RelayErrors.TooLarge), frames);
    }

    [Fact]
    public async Task Flood_RateLimited()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);

        for (var i = 0; i < 100; i++)
            await plugin.SendAsync(new SendFrame("nobody", "x"));

        var codes = new List<string>();
        for (var i = 0; i < 100; i++)
            codes.Add(((ErrorFrame)(await plugin.NextAsync())!).Code);
        Assert.Contains(RelayErrors.RateLimited, codes);
        Assert.True(codes.Count(c => c == RelayErrors.UnknownDevice) >= 40);
    }

    [Fact]
    public async Task NoAuthIn10s_Closed()
    {
        using var app = new RelayApp(new() { ["Relay:AuthTimeout"] = "00:00:00.300" });

        await using var socket = await app.OpenSocketAsync();

        await socket.ExpectClosedAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BadToken_AuthFail()
    {
        using var app = new RelayApp();

        await using var socket = await app.OpenSocketAsync();
        await socket.SendAsync(new AuthFrame("i.bogus.bogus"));

        Assert.IsType<AuthFailFrame>(await socket.NextAsync());
        await socket.ExpectClosedAsync();
    }

    [Fact]
    public async Task NewConnectionSameId_ClosesOld()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));
        await using var phone = await app.ConnectAsync(device.DeviceToken);
        await using var first = await app.ConnectAsync(install.InstallToken);
        await phone.ReceiveAsync<PluginStatusFrame>();

        await using var second = await app.ConnectAsync(install.InstallToken);

        await first.ExpectClosedAsync();
        await second.BarrierAsync();
        var phoneFrames = await phone.BarrierAsync();
        Assert.DoesNotContain(new PluginStatusFrame(false), phoneFrames);
    }
}
