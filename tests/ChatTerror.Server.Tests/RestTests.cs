using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ChatTerror.Protocol;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class RestTests
{
    [Fact]
    public async Task RegisterInstall_ReturnsToken()
    {
        using var app = new RelayApp();
        var publicKey = RelayApp.NewPublicKey();

        var response = await app.Client().PostAsJsonAsync("/api/installs", new { publicKey });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var install = (await response.Content.ReadFromJsonAsync<InstallResponse>())!;
        Assert.Equal(16, Base64Url.Decode(install.InstallId).Length);
        Assert.StartsWith($"i.{install.InstallId}.", install.InstallToken);
        Assert.Equal(32, Base64Url.Decode(install.InstallToken.Split('.')[2]).Length);
    }

    [Fact]
    public async Task RegisterInstall_InvalidKey_400()
    {
        using var app = new RelayApp();

        var response = await app.Client().PostAsJsonAsync("/api/installs", new { publicKey = "not-a-key" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePairing_RequiresInstallAuth()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));

        var anonymous = await app.Client().PostAsJsonAsync("/api/pairings", new { });
        var asDevice = await app.Client(device.DeviceToken).PostAsJsonAsync("/api/pairings", new { });
        var badSecret = await app.Client(install.InstallToken[..^4] + "AAAA").PostAsJsonAsync("/api/pairings", new { });
        var ok = await app.Client(install.InstallToken).PostAsJsonAsync("/api/pairings", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asDevice.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, badSecret.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var pairing = (await ok.Content.ReadFromJsonAsync<PairingResponse>())!;
        Assert.Matches(new Regex("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$"), pairing.Code);
        var expected = app.Time.GetUtcNow().Add(Limits.PairingTtl).ToUnixTimeMilliseconds();
        Assert.Equal(expected, pairing.ExpiresAt);
    }

    [Fact]
    public async Task PairingFlow_ClaimCreatesPendingDevice_AndPluginGetsPairRequest()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(install.InstallToken);
        var code = await app.CreatePairingAsync(install.InstallToken);

        var info = (await app.Client().GetFromJsonAsync<PairingInfo>($"/api/pairings/{code}"))!;
        Assert.Equal(install.InstallId, info.InstallId);
        Assert.Equal(65, Base64Url.Decode(info.PluginPublicKey).Length);

        var devicePublicKey = RelayApp.NewPublicKey();
        var claimResponse = await app.Client().PostAsJsonAsync($"/api/pairings/{code}/claim", new { devicePublicKey, deviceName = "My Phone" });
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        var claim = (await claimResponse.Content.ReadFromJsonAsync<ClaimResponse>())!;
        Assert.StartsWith($"d.{claim.DeviceId}.", claim.DeviceToken);

        var request = await plugin.ReceiveAsync<PairRequestFrame>();
        Assert.Equal(new PairRequestFrame(claim.DeviceId, "My Phone", devicePublicKey), request);

        var devices = (await app.Client(install.InstallToken).GetFromJsonAsync<DeviceInfo[]>("/api/devices"))!;
        var listed = Assert.Single(devices);
        Assert.Equal(claim.DeviceId, listed.DeviceId);
        Assert.Equal("My Phone", listed.Name);
        Assert.Equal("pending", listed.Status);
        Assert.Equal(devicePublicKey, listed.PublicKey);

        var me = (await app.Client(claim.DeviceToken).GetFromJsonAsync<DeviceMe>("/api/devices/me"))!;
        Assert.Equal(new DeviceMe(claim.DeviceId, "pending"), me);

        await using var reconnected = await app.ConnectAsync(install.InstallToken);
        var again = await reconnected.ReceiveAsync<PairRequestFrame>();
        Assert.Equal(claim.DeviceId, again.DeviceId);
    }

    [Fact]
    public async Task Claim_ConsumesCode_SecondClaim404()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var code = await app.CreatePairingAsync(install.InstallToken);
        await app.ClaimAsync(code);

        var second = await app.PostClaimAsync(code);
        var lookup = await app.Client().GetAsync($"/api/pairings/{code}");

        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task Claim_LowercaseCode_Accepted()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var code = await app.CreatePairingAsync(install.InstallToken);

        var response = await app.PostClaimAsync(code.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Claim_ExpiredCode_404()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var code = await app.CreatePairingAsync(install.InstallToken);

        app.Time.Advance(Limits.PairingTtl + TimeSpan.FromSeconds(1));
        var lookup = await app.Client().GetAsync($"/api/pairings/{code}");
        var claim = await app.PostClaimAsync(code);

        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, claim.StatusCode);
    }

    [Fact]
    public async Task Claim_EleventhDevice_409()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        for (var i = 0; i < Limits.MaxDevices; i++)
            await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));

        var response = await app.PostClaimAsync(await app.CreatePairingAsync(install.InstallToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("tooManyDevices", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }

    [Fact]
    public async Task PairingLookup_RateLimited()
    {
        using var app = new RelayApp(new() { ["Relay:PairingRequestsPerMinute"] = "3" });

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await app.Client().GetAsync("/api/pairings/AAAA-AAAA")).StatusCode);
        var limited = await app.Client().GetAsync("/api/pairings/AAAA-AAAA");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rateLimited", (await limited.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }

    [Fact]
    public async Task DeleteDevice_ByOtherInstall_404_ByDeviceItself_204()
    {
        using var app = new RelayApp();
        var owner = await app.RegisterInstallAsync();
        var other = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(owner.InstallToken));
        var otherDevice = await app.ClaimAsync(await app.CreatePairingAsync(other.InstallToken));

        var byOtherInstall = await app.Client(other.InstallToken).DeleteAsync($"/api/devices/{device.DeviceId}");
        var byOtherDevice = await app.Client(otherDevice.DeviceToken).DeleteAsync($"/api/devices/{device.DeviceId}");
        var bySelf = await app.Client(device.DeviceToken).DeleteAsync($"/api/devices/{device.DeviceId}");

        Assert.Equal(HttpStatusCode.NotFound, byOtherInstall.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byOtherDevice.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, bySelf.StatusCode);
        Assert.Empty((await app.Client(owner.InstallToken).GetFromJsonAsync<DeviceInfo[]>("/api/devices"))!);
    }

    [Fact]
    public async Task Push_PutAndDelete_UpdatesSubscription()
    {
        using var app = new RelayApp();
        var install = await app.RegisterInstallAsync();
        var device = await app.ClaimAsync(await app.CreatePairingAsync(install.InstallToken));
        var client = app.Client(device.DeviceToken);
        var store = app.Services.GetRequiredService<ChatTerror.Server.Data.RelayStore>();

        var badPut = await client.PutAsJsonAsync("/api/devices/me/push", new { endpoint = "http://1.1.1.1/x", keys = new { p256dh = "a", auth = "b" } });
        var put = await client.PutAsJsonAsync("/api/devices/me/push", new { endpoint = "https://1.1.1.1/x", keys = new { p256dh = "a", auth = "b" } });

        Assert.Equal(HttpStatusCode.BadRequest, badPut.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(new ChatTerror.Server.Push.PushSubscriptionRecord("https://1.1.1.1/x", "a", "b"), store.FindDevice(device.DeviceId)!.Push);

        var delete = await client.DeleteAsync("/api/devices/me/push");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null(store.FindDevice(device.DeviceId)!.Push);
    }

    [Fact]
    public async Task Vapid_ReturnsPublicKey()
    {
        using var app = new RelayApp();

        var vapid = (await app.Client().GetFromJsonAsync<VapidResponse>("/api/vapid"))!;

        Assert.Equal(65, Base64Url.Decode(vapid.PublicKey).Length);
    }

    [Fact]
    public async Task ForeignOrigin_RejectedForPostAndWebSocket()
    {
        using var app = new RelayApp(new() { ["Relay:AllowedOrigins"] = "https://allowed.example" });

        var foreign = await app.Client(origin: "https://evil.example").PostAsJsonAsync("/api/installs", new { publicKey = RelayApp.NewPublicKey() });
        var allowed = await app.Client(origin: "https://allowed.example").PostAsJsonAsync("/api/installs", new { publicKey = RelayApp.NewPublicKey() });
        var sameOrigin = await app.Client(origin: "http://localhost").PostAsJsonAsync("/api/installs", new { publicKey = RelayApp.NewPublicKey() });
        var foreignGet = await app.Client(origin: "https://evil.example").GetAsync("/api/vapid");

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sameOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, foreignGet.StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => app.OpenSocketAsync(origin: "https://evil.example"));
    }
}
