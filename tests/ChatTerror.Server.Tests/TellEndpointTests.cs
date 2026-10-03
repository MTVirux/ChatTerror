using System.Net;
using System.Net.Http.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Tests.Support;

namespace ChatTerror.Server.Tests;

public class TellEndpointTests
{
    [Fact]
    public async Task Bundle_MustBeSignedByTheInstallKey()
    {
        using var app = new RelayApp();
        using var key = P256.Generate();
        using var other = P256.Generate();
        var a = await app.RegisterInstallAsync(key);
        var otherKey = Base64Url.Encode(P256.PublicRaw(other));
        var forged = TellBundles.Sign(other, new TellBundle(otherKey, [new TellBundleEntry(TellTargets.Plugin, otherKey, false)], 1));

        var response = await app.Client(a.InstallToken).PutAsJsonAsync("/api/tells/bundle", forged, ProtocolJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bundle_RejectsForeignDeviceTargets()
    {
        using var app = new RelayApp();
        using var key = P256.Generate();
        var a = await app.RegisterInstallAsync(key);
        var publicKey = Base64Url.Encode(P256.PublicRaw(key));
        var bundle = TellBundles.Sign(key, new TellBundle(publicKey, [new TellBundleEntry("someone-elses-device", publicKey, true)], 1));

        var response = await app.Client(a.InstallToken).PutAsJsonAsync("/api/tells/bundle", bundle, ProtocolJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBundle_NeedsAnApprovedDevice()
    {
        using var app = new RelayApp();
        using var key = P256.Generate();
        var a = await app.RegisterInstallAsync(key);
        await using var plugin = await app.ConnectAsync(a.InstallToken);
        await app.PutTellBundleAsync(a.InstallToken, key);
        var pending = await app.PairDeviceAsync(a, plugin, approve: false);

        var response = await app.Client(pending.DeviceToken).GetAsync("/api/tells/bundles/self");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBundle_ByInstallIdAndSelf()
    {
        using var app = new RelayApp();
        using var key = P256.Generate();
        var a = await app.RegisterInstallAsync(key);
        await using var plugin = await app.ConnectAsync(a.InstallToken);
        var device = await app.PairDeviceAsync(a, plugin);
        await app.PutTellBundleAsync(a.InstallToken, key, new TellBundleEntry(device.DeviceId, RelayApp.NewPublicKey(), true));
        var expected = Base64Url.Encode(P256.PublicRaw(key));

        var byId = await app.Client(device.DeviceToken).GetFromJsonAsync<SignedTellBundle>($"/api/tells/bundles/{a.InstallId}", ProtocolJson.Options);
        var self = await app.Client(device.DeviceToken).GetFromJsonAsync<SignedTellBundle>("/api/tells/bundles/self", ProtocolJson.Options);
        var pluginSelf = await app.Client(a.InstallToken).GetFromJsonAsync<SignedTellBundle>("/api/tells/bundles/self", ProtocolJson.Options);

        Assert.Equal(2, TellBundles.Verify(byId!, expected)!.Entries.Count);
        Assert.Equal(byId, self);
        Assert.Equal(byId, pluginSelf);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync("/api/tells/bundles/self")).StatusCode);
    }

    [Fact]
    public async Task GetBundle_OnlyForFriendsAndTheirDevices()
    {
        using var app = new RelayApp();
        using var keyA = P256.Generate();
        var a = await app.RegisterInstallAsync(keyA);
        var b = await app.RegisterInstallAsync();
        await app.PutTellBundleAsync(a.InstallToken, keyA);
        await using var pluginB = await app.ConnectAsync(b.InstallToken);
        var phoneB = await app.PairDeviceAsync(b, pluginB);

        var stranger = await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{a.InstallId}");
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(TellErrors.NotChatTerror, (await stranger.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(phoneB.DeviceToken).GetAsync($"/api/tells/bundles/{a.InstallId}")).StatusCode);

        await app.PairFriendsAsync(a, b);

        Assert.Equal(HttpStatusCode.OK, (await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{a.InstallId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Client(phoneB.DeviceToken).GetAsync($"/api/tells/bundles/{a.InstallId}")).StatusCode);
    }

    [Fact]
    public async Task DeleteBundle_HidesItFromFriends()
    {
        using var app = new RelayApp();
        using var keyA = P256.Generate();
        var a = await app.RegisterInstallAsync(keyA);
        var b = await app.RegisterInstallAsync();
        await app.PairFriendsAsync(a, b);
        await app.PutTellBundleAsync(a.InstallToken, keyA);

        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).DeleteAsync("/api/tells/bundle")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{a.InstallId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(a.InstallToken).GetAsync("/api/tells/bundles/self")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().DeleteAsync("/api/tells/bundle")).StatusCode);
    }

    [Fact]
    public async Task CharacterEndpoints_AreGone()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var client = app.Client(a.InstallToken);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/tells/characters/{TellHash.Compute(1)}", new { friends = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/tells/characters")).StatusCode);
    }

    [Fact]
    public async Task TellEndpoints_AreRateLimited()
    {
        using var app = new RelayApp(new() { ["Relay:TellRequestsPerMinute"] = "2" });
        var a = await app.RegisterInstallAsync();
        var client = app.Client(a.InstallToken);

        await client.GetAsync("/api/tells/bundles/self");
        await client.GetAsync("/api/tells/bundles/self");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/tells/bundles/self")).StatusCode);
    }
}
