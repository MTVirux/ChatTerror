using System.Net;
using System.Net.Http.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Relay;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task CreatePairing_ReplacesPreviousCode()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var first = await app.CreatePairingAsync(install.InstallToken);
        var second = await app.CreatePairingAsync(install.InstallToken);

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client().GetAsync($"/api/pairings/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client().GetAsync($"/api/pairings/{second}")).StatusCode);
    }

    [Fact]
    public async Task CreatePairing_OtherInstallsCodeSurvives()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var codeA = await app.CreatePairingAsync(a.InstallToken);
        await app.CreatePairingAsync(b.InstallToken);

        Assert.Equal(HttpStatusCode.OK, (await app.Client().GetAsync($"/api/pairings/{codeA}")).StatusCode);
    }

    [Fact]
    public async Task CreatePairing_RateLimitedPerInstall()
    {
        using var app = new RelayApp(new() { ["Relay:PairingsPerInstallPerHour"] = "3" });
        var limited = await app.RegisterInstallAsync();
        var other = await app.RegisterInstallAsync();

        for (var i = 0; i < 3; i++)
            await app.CreatePairingAsync(limited.InstallToken);
        var blocked = await app.Client(limited.InstallToken).PostAsJsonAsync("/api/pairings", new { });

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("rateLimited", (await blocked.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
        await app.CreatePairingAsync(other.InstallToken);

        app.Time.Advance(TimeSpan.FromHours(1));
        await app.CreatePairingAsync(limited.InstallToken);
    }

    [Fact]
    public async Task StaleInstall_WithoutDevices_IsDeleted()
    {
        using var app = new RelayApp();
        var stale = await app.RegisterInstallAsync();
        await app.CreatePairingAsync(stale.InstallToken);
        var store = app.Services.GetRequiredService<RelayStore>();

        app.Time.Advance(TimeSpan.FromDays(29));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.NotNull(store.FindInstallByToken(stale.InstallToken));

        app.Time.Advance(TimeSpan.FromDays(2));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.Null(store.FindInstallByToken(stale.InstallToken));
    }

    [Fact]
    public async Task StaleInstall_TtlIsConfigurable()
    {
        using var app = new RelayApp(new() { ["Relay:InstallTtl"] = "2.00:00:00" });
        var stale = await app.RegisterInstallAsync();

        app.Time.Advance(TimeSpan.FromDays(3));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.Null(app.Services.GetRequiredService<RelayStore>().FindInstallByToken(stale.InstallToken));
    }

    [Fact]
    public async Task StaleInstall_WithDevice_IsKept()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using (var plugin = await app.ConnectAsync(install.InstallToken))
            await app.PairDeviceAsync(install, plugin);

        app.Time.Advance(TimeSpan.FromDays(31));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.NotNull(app.Services.GetRequiredService<RelayStore>().FindInstallByToken(install.InstallToken));
    }

    [Fact]
    public async Task Install_ConnectedRecently_OrConnectedNow_IsKept()
    {
        using var app = new RelayApp();
        var recent = await app.RegisterInstallAsync();
        var online = await app.RegisterInstallAsync();
        await using var onlinePlugin = await app.ConnectAsync(online.InstallToken);

        app.Time.Advance(TimeSpan.FromDays(20));
        await using (await app.ConnectAsync(recent.InstallToken))
        {
        }
        app.Time.Advance(TimeSpan.FromDays(20));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        var store = app.Services.GetRequiredService<RelayStore>();
        Assert.NotNull(store.FindInstallByToken(recent.InstallToken));
        Assert.NotNull(store.FindInstallByToken(online.InstallToken));
    }

    [Fact]
    public async Task InactiveDevice_IsDeleted_AndTreatedAsRevoked()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        var store = app.Services.GetRequiredService<RelayStore>();

        app.Time.Advance(TimeSpan.FromDays(89));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.NotNull(store.FindDevice(device.DeviceId));

        app.Time.Advance(TimeSpan.FromDays(2));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.Null(store.FindDevice(device.DeviceId));
        Assert.Equal(device.DeviceId, (await plugin.ReceiveAsync<DeviceRevokedFrame>()).DeviceId);
        await using var phone = await app.OpenSocketAsync();
        await phone.SendAsync(new AuthFrame(device.DeviceToken));
        Assert.IsType<AuthFailFrame>(await phone.NextAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(device.DeviceToken).GetAsync("/api/devices/me")).StatusCode);
    }

    [Fact]
    public async Task InactiveDevice_ConnectedNow_IsKept()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await using var phone = await app.ConnectAsync(device.DeviceToken);

        app.Time.Advance(TimeSpan.FromDays(91));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.NotNull(app.Services.GetRequiredService<RelayStore>().FindDevice(device.DeviceId));
    }

    [Fact]
    public async Task InactiveInstall_WithDevices_IsDeleted()
    {
        using var app = new RelayApp(new() { ["Relay:InactiveInstallTtl"] = "10.00:00:00" });
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));
        var store = app.Services.GetRequiredService<RelayStore>();
        store.SetDeviceStatus(device.DeviceId, DeviceStatus.Active);
        var code = await app.CreatePairingAsync(install.InstallToken);
        await using var phone = await app.ConnectAsync(device.DeviceToken);

        app.Time.Advance(TimeSpan.FromDays(9));
        app.Services.GetRequiredService<ExpiryService>().Sweep();
        Assert.NotNull(store.FindInstallByToken(install.InstallToken));

        app.Time.Advance(TimeSpan.FromDays(2));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.Contains(await phone.ExpectClosedAsync(), f => f is RevokedFrame);
        Assert.Null(store.FindInstallByToken(install.InstallToken));
        Assert.Null(store.FindDevice(device.DeviceId));
        Assert.Null(store.FindPairing(code));
        await using var plugin = await app.OpenSocketAsync();
        await plugin.SendAsync(new AuthFrame(install.InstallToken));
        Assert.IsType<AuthFailFrame>(await plugin.NextAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(install.InstallToken).GetAsync("/api/devices")).StatusCode);
    }

    [Fact]
    public async Task InactiveInstall_ConnectedNow_IsKept()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        await app.PairDeviceAsync(install, plugin);

        app.Time.Advance(TimeSpan.FromDays(91));
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.NotNull(app.Services.GetRequiredService<RelayStore>().FindInstallByToken(install.InstallToken));
    }

    [Fact]
    public async Task PushBody_AtCap_Sent_OverCap_Skipped()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var device = await app.PairDeviceAsync(install, plugin);
        await app.Client(device.DeviceToken).PutAsJsonAsync("/api/devices/me/push",
            new { endpoint = "https://1.1.1.1/sub", keys = new { p256dh = "p", auth = "a" } });
        var wrapper = $"{{\"p\":\"\",\"d\":\"{device.DeviceId}\"}}".Length;

        Assert.Equal(3993, RelaySocketHandler.MaxPushBodyBytes);
        await plugin.SendAsync(new SendFrame(device.DeviceId, new string('a', 3994 - wrapper), Notify: true));
        await plugin.SendAsync(new SendFrame(device.DeviceId, new string('b', 3993 - wrapper), Notify: true));
        await plugin.BarrierAsync();
        await app.Push.WaitForCallAsync();

        var call = Assert.Single(app.Push.Calls);
        Assert.Equal(3993, call.Body.Length);
    }

    [Fact]
    public async Task PushSubscription_PrivateEndpoint_400()
    {
        using var app = new RelayApp(new() { ["Relay:PushServiceHosts"] = "127.0.0.1,10.1.2.3,::1,169.254.169.254,fd00::1" });
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));
        var client = app.Client(device.DeviceToken);

        foreach (var endpoint in new[] { "https://127.0.0.1/x", "https://10.1.2.3/x", "https://[::1]/x", "https://169.254.169.254/x", "https://[fd00::1]/x" })
        {
            var response = await client.PutAsJsonAsync("/api/devices/me/push", new { endpoint, keys = new { p256dh = "a", auth = "b" } });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Null(app.Services.GetRequiredService<RelayStore>().FindDevice(device.DeviceId)!.Push);
    }

    [Fact]
    public async Task StaticFiles_EntryPoints_AreNoCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "chatterror-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        foreach (var file in new[] { "index.html", "manifest.webmanifest", "sw.js", "assets/app.js" })
            File.WriteAllText(Path.Combine(root, file), "x");
        try
        {
            using var app = new RelayApp(new() { ["webroot"] = root });
            var client = app.Client();

            foreach (var path in new[] { "/", "/index.html", "/manifest.webmanifest", "/sw.js" })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoCache, $"{path} should be no-cache");
            }
            var asset = await client.GetAsync("/assets/app.js");
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.NotEqual(true, asset.Headers.CacheControl?.NoCache);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
