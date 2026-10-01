using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using ChatTerror.Protocol;
using ChatTerror.Server.Api;
using ChatTerror.Server.Data;
using ChatTerror.Server.Push;
using ChatTerror.Server.Relay;
using ChatTerror.Server.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class HardeningTests
{
    [Fact]
    public async Task ExpiredPendingDevice_IsRevoked_AndPluginNotified()
    {
        using var app = new RelayApp(new() { ["Relay:PendingDeviceTtl"] = "00:05:00" });
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var pending = await app.PairDeviceAsync(install, plugin, approve: false);
        var active = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(pending.DeviceToken);

        app.Time.Advance(TimeSpan.FromMinutes(6));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        var frames = await phone.ExpectClosedAsync();
        Assert.Contains(frames, f => f is RevokedFrame);
        Assert.Equal(pending.DeviceId, (await plugin.ReceiveAsync<DeviceRevokedFrame>()).DeviceId);
        var store = app.Services.GetRequiredService<RelayStore>();
        Assert.Null(store.FindDevice(pending.DeviceId));
        Assert.NotNull(store.FindDevice(active.DeviceId));
    }

    [Fact]
    public async Task PendingDevice_WithinTtl_NotExpired()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));

        app.Time.Advance(TimeSpan.FromMinutes(59));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.NotNull(app.Services.GetRequiredService<RelayStore>().FindDevice(device.DeviceId));
    }

    [Fact]
    public async Task Plugin_WithThreeActiveDevices_GetsTripleBurst()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using (var pairing = await app.ConnectAsync(install.InstallToken))
        {
            for (var i = 0; i < 3; i++)
                await app.PairDeviceAsync(install, pairing);
        }
        await using var plugin = await app.ConnectAsync(install.InstallToken);

        for (var i = 0; i < 120; i++)
            await plugin.SendAsync(new SendFrame("nobody", "x"));

        for (var i = 0; i < 120; i++)
            Assert.Equal(new ErrorFrame(RelayErrors.UnknownDevice), await plugin.NextAsync());
    }

    [Fact]
    public async Task RelayedFrameOver64KiB_TooLarge_ConnectionStaysOpen()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(device.DeviceToken);
        var msgOverhead = Conn.Encode(new MsgFrame(device.DeviceId, "")).Length;
        var largest = new string('b', Limits.MaxFrameBytes - msgOverhead);

        // The incoming send frame fits, but the msg frame it becomes is one byte over.
        await phone.SendAsync(new SendFrame(Payload: largest + "b"));
        Assert.Equal(RelayErrors.TooLarge, (await phone.ReceiveAsync<ErrorFrame>()).Code);

        await phone.SendAsync(new SendFrame(Payload: largest));
        Assert.Equal(largest, (await plugin.ReceiveAsync<MsgFrame>()).Payload);
    }

    [Fact]
    public async Task EmojiPayload_EscapedFrameOver64KiB_TooLarge()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(device.DeviceToken);
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 6000));

        // Sent raw, these are 4 bytes each; escaped on the way out they become 12.
        await phone.SendRawAsync($"{{\"t\":\"send\",\"payload\":\"{emoji}\"}}");
        Assert.Equal(RelayErrors.TooLarge, (await phone.ReceiveAsync<ErrorFrame>()).Code);
        await plugin.SendRawAsync($"{{\"t\":\"send\",\"to\":\"{device.DeviceId}\",\"payload\":\"{emoji}\"}}");
        Assert.Equal(RelayErrors.TooLarge, (await plugin.ReceiveAsync<ErrorFrame>()).Code);

        Assert.DoesNotContain(await plugin.BarrierAsync(), f => f is MsgFrame);
        Assert.DoesNotContain(await phone.BarrierAsync(), f => f is MsgFrame);
    }

    [Fact]
    public async Task PreAuthFrameOver4KiB_TooLarge_Closed()
    {
        using var app = new RelayApp();
        await using var socket = await app.OpenSocketAsync();

        await socket.SendAsync(new AuthFrame(new string('a', RelaySocketHandler.PreAuthMaxBytes)));

        var frames = await socket.ExpectClosedAsync();
        Assert.Contains(new ErrorFrame(RelayErrors.TooLarge), frames);
    }

    [Fact]
    public void SlowReader_OutboxOverOneMegabyte_Closes()
    {
        using var socket = new StalledSocket();
        var conn = new Conn(socket);
        var frame = new MsgFrame("plugin", new string('a', 60_000));

        for (var i = 0; i < 15; i++)
            conn.Send(frame);
        Assert.False(conn.IsClosing);

        for (var i = 0; i < 5; i++)
            conn.Send(frame);
        Assert.True(conn.IsClosing);
    }

    [Fact]
    public async Task RegisterInstall_RateLimited()
    {
        using var app = new RelayApp(new() { ["Relay:InstallsPerHour"] = "2" });

        await app.RegisterInstallAsync();
        await app.RegisterInstallAsync();
        var limited = await app.Client().PostAsJsonAsync("/api/installs", new { publicKey = RelayApp.NewPublicKey() });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public void PartitionKey_GroupsIpv6By64_AndUnmapsIpv4()
    {
        Assert.Equal(
            RequestLimits.PartitionKey(IPAddress.Parse("2001:db8:1:2::1")),
            RequestLimits.PartitionKey(IPAddress.Parse("2001:db8:1:2:ffff::9")));
        Assert.NotEqual(
            RequestLimits.PartitionKey(IPAddress.Parse("2001:db8:1:2::1")),
            RequestLimits.PartitionKey(IPAddress.Parse("2001:db8:1:3::1")));
        Assert.Equal("1.2.3.4", RequestLimits.PartitionKey(IPAddress.Parse("::ffff:1.2.3.4")));
        Assert.Equal("unknown", RequestLimits.PartitionKey(null));
    }

    [Fact]
    public async Task TrustedProxy_ForwardedFor_UsedForRateLimit()
    {
        using var app = new RelayApp(new()
        {
            ["Relay:TrustedProxies"] = "10.0.0.0/8",
            ["Relay:PairingRequestsPerMinute"] = "1",
        })
        {
            ExtraServices = services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(IPAddress.Parse("10.1.2.3"))),
        };

        async Task<HttpStatusCode> Lookup(string forwardedFor)
        {
            var client = app.Client();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
            return (await client.GetAsync("/api/pairings/AAAA-AAAA")).StatusCode;
        }

        Assert.Equal(HttpStatusCode.NotFound, await Lookup("1.1.1.1"));
        Assert.Equal(HttpStatusCode.NotFound, await Lookup("2.2.2.2"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Lookup("1.1.1.1"));
        Assert.Equal(HttpStatusCode.NotFound, await Lookup("2001:db8::1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Lookup("2001:db8::2"));
    }

    [Fact]
    public async Task UntrustedProxy_ForwardedForIgnored()
    {
        using var app = new RelayApp(new() { ["Relay:PairingRequestsPerMinute"] = "1" });
        var first = app.Client();
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "1.1.1.1");
        var second = app.Client();
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "2.2.2.2");

        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync("/api/pairings/AAAA-AAAA")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await second.GetAsync("/api/pairings/AAAA-AAAA")).StatusCode);
    }

    [Fact]
    public async Task PushBodyOver4KB_Skipped()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await app.Client(device.DeviceToken).PutAsJsonAsync("/api/devices/me/push",
            new { endpoint = "https://push.example/sub", keys = new { p256dh = "p", auth = "a" } });

        await plugin.SendAsync(new SendFrame(device.DeviceId, new string('a', RelaySocketHandler.MaxPushBodyBytes), Notify: true));
        await plugin.SendAsync(new SendFrame(device.DeviceId, string.Concat(Enumerable.Repeat("\u00e9", 2100)), Notify: true));
        await plugin.BarrierAsync();

        Assert.Empty(app.Push.Calls);
    }

    [Fact]
    public void PushLimiter_CapsPerInstallAndTotal()
    {
        var limiter = new PushLimiter(maxTotal: 3, maxPerInstall: 2);

        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
        Assert.False(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("b"));
        Assert.False(limiter.TryAcquire("c"));

        limiter.Release("a");
        Assert.True(limiter.TryAcquire("c"));
    }

    [Fact]
    public async Task SaturatedInstall_DoesNotBlockOtherInstallsPush()
    {
        using var app = new RelayApp();
        var stuck = new TaskCompletionSource();
        app.Push.Hold = sub => sub.Endpoint.EndsWith("/a") ? stuck.Task : Task.CompletedTask;

        async Task<(TestSocket Plugin, Support.ClaimResponse Device)> Setup(string endpoint)
        {
            var install = await app.RegisterInstallAsync();
            var plugin = await app.ConnectAsync(install.InstallToken);
            var device = await app.PairDeviceAsync(install, plugin);
            await app.Client(device.DeviceToken).PutAsJsonAsync("/api/devices/me/push",
                new { endpoint, keys = new { p256dh = "p", auth = "a" } });
            return (plugin, device);
        }

        var (pluginA, deviceA) = await Setup("https://push.example/a");
        var (pluginB, deviceB) = await Setup("https://push.example/b");
        await using var _a = pluginA;
        await using var _b = pluginB;

        for (var i = 0; i < 6; i++)
            await pluginA.SendAsync(new SendFrame(deviceA.DeviceId, "a", Notify: true));
        await pluginA.BarrierAsync();
        Assert.Equal(4, app.Push.Calls.Count);

        await pluginB.SendAsync(new SendFrame(deviceB.DeviceId, "b", Notify: true));
        await pluginB.BarrierAsync();
        Assert.Contains(app.Push.Calls, call => call.Subscription.Endpoint.EndsWith("/b"));

        stuck.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await pluginA.SendAsync(new SendFrame(deviceA.DeviceId, "a", Notify: true));
            await pluginA.BarrierAsync();
            if (app.Push.Calls.Count(call => call.Subscription.Endpoint.EndsWith("/a")) > 4)
                return;
            await Task.Delay(20);
        }
        Assert.Fail("Install A never got its push slots back.");
    }

    [Fact]
    public async Task PushSubscription_OversizeFields_400()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));
        var client = app.Client(device.DeviceToken);

        var longEndpoint = await client.PutAsJsonAsync("/api/devices/me/push",
            new { endpoint = "https://push.example/" + new string('a', 2048), keys = new { p256dh = "p", auth = "a" } });
        var longKey = await client.PutAsJsonAsync("/api/devices/me/push",
            new { endpoint = "https://push.example/sub", keys = new { p256dh = new string('p', 257), auth = "a" } });

        Assert.Equal(HttpStatusCode.BadRequest, longEndpoint.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, longKey.StatusCode);
    }

    [Fact]
    public async Task ApiBodyOverLimit_413()
    {
        using var app = new RelayApp();
        var json = $"{{\"publicKey\":\"{new string('a', 20_000)}\"}}";

        var response = await app.Client().PostAsync("/api/installs", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private sealed class RemoteAddressFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    // Accepts frames into the send call but never finishes sending them.
    private sealed class StalledSocket : WebSocket
    {
        private WebSocketState state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => state;

        public override string? SubProtocol => null;

        public override void Abort() => state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose() => state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
