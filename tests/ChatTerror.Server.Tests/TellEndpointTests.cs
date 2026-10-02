using System.Net;
using System.Net.Http.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Tests.Support;

namespace ChatTerror.Server.Tests;

public class TellEndpointTests
{
    private static readonly string A = TellHash.Compute(1);
    private static readonly string B = TellHash.Compute(2);
    private static readonly string C = TellHash.Compute(3);

    [Fact]
    public async Task PutCharacter_ReturnsRegisteredFriends()
    {
        using var app = new RelayApp();
        using var keyA = P256.Generate();
        using var keyB = P256.Generate();
        var a = await app.RegisterInstallAsync(keyA);
        var b = await app.RegisterInstallAsync(keyB);

        Assert.Empty(await app.PutTellCharacterAsync(a.InstallToken, A, B));
        Assert.Equal([A], await app.PutTellCharacterAsync(b.InstallToken, B, A));
    }

    [Fact]
    public async Task PutCharacter_RejectsBadInput()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var client = app.Client(a.InstallToken);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/tells/characters/nope", new { friends = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/tells/characters/{A}", new { friends = new[] { "x" } })).StatusCode);
        var tooMany = Enumerable.Range(0, Limits.MaxFriends + 1).Select(i => TellHash.Compute((ulong)i + 10)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/tells/characters/{A}", new { friends = tooMany })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().PutAsJsonAsync($"/api/tells/characters/{A}", new { friends = Array.Empty<string>() })).StatusCode);
    }

    [Fact]
    public async Task DeleteCharacters_Unregisters()
    {
        using var app = new RelayApp();
        using var key = P256.Generate();
        var a = await app.RegisterInstallAsync(key);
        var b = await app.RegisterInstallAsync();
        await app.PutTellCharacterAsync(a.InstallToken, A);
        await app.PutTellBundleAsync(a.InstallToken, key);

        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).DeleteAsync("/api/tells/characters")).StatusCode);

        Assert.Empty(await app.PutTellCharacterAsync(b.InstallToken, B, A));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{A}")).StatusCode);
    }

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
    public async Task GetBundle_ByHashAndSelf()
    {
        using var app = new RelayApp();
        using var key = P256.Generate();
        var a = await app.RegisterInstallAsync(key);
        await using var plugin = await app.ConnectAsync(a.InstallToken);
        var device = await app.PairDeviceAsync(a, plugin);
        await app.PutTellCharacterAsync(a.InstallToken, A);
        await app.PutTellBundleAsync(a.InstallToken, key, new TellBundleEntry(device.DeviceId, RelayApp.NewPublicKey(), true));
        var expected = Base64Url.Encode(P256.PublicRaw(key));

        var byHash = await app.Client(device.DeviceToken).GetFromJsonAsync<SignedTellBundle>($"/api/tells/bundles/{A}", ProtocolJson.Options);
        var self = await app.Client(device.DeviceToken).GetFromJsonAsync<SignedTellBundle>("/api/tells/bundles/self", ProtocolJson.Options);

        Assert.Equal(2, TellBundles.Verify(byHash!, expected)!.Entries.Count);
        Assert.Equal(byHash, self);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(device.DeviceToken).GetAsync($"/api/tells/bundles/{B}")).StatusCode);
    }

    [Fact]
    public async Task PutCharacter_CapsCharactersPerInstall()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        for (var i = 0; i < Limits.MaxTellCharacters; i++)
            await app.PutTellCharacterAsync(a.InstallToken, TellHash.Compute((ulong)i + 100));

        var extra = await app.Client(a.InstallToken).PutAsJsonAsync($"/api/tells/characters/{TellHash.Compute(5000)}", new { friends = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
        Assert.Empty(await app.PutTellCharacterAsync(a.InstallToken, TellHash.Compute(100)));
    }

    [Fact]
    public async Task GetBundle_OnlyForMutualFriendsOfTheCallersCharacters()
    {
        using var app = new RelayApp();
        using var keyA = P256.Generate();
        var a = await app.RegisterInstallAsync(keyA);
        var b = await app.RegisterInstallAsync();
        await app.PutTellCharacterAsync(a.InstallToken, A);
        await app.PutTellBundleAsync(a.InstallToken, keyA);
        await app.PutTellCharacterAsync(b.InstallToken, B);

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{A}")).StatusCode);

        await app.PutTellCharacterAsync(b.InstallToken, B, A);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{A}")).StatusCode);

        await app.PutTellCharacterAsync(a.InstallToken, A, B);
        Assert.Equal(HttpStatusCode.OK, (await app.Client(b.InstallToken).GetAsync($"/api/tells/bundles/{A}")).StatusCode);
    }

    [Fact]
    public async Task PutCharacter_HidesRegisteredHashesThatDoNotListTheCaller()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var c = await app.RegisterInstallAsync();
        await app.PutTellCharacterAsync(a.InstallToken, A, C);
        await app.PutTellCharacterAsync(b.InstallToken, B);

        Assert.Equal([A], await app.PutTellCharacterAsync(c.InstallToken, C, A, B));
        Assert.Empty(await app.PutTellCharacterAsync(c.InstallToken, C, B));
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

    [Fact]
    public async Task PutCharacter_StaysWithAnActiveInstall()
    {
        using var app = new RelayApp();
        var owner = await app.RegisterInstallAsync();
        var other = await app.RegisterInstallAsync();
        await app.PutTellCharacterAsync(owner.InstallToken, A);

        var taken = await app.Client(other.InstallToken).PutAsJsonAsync($"/api/tells/characters/{A}", new { friends = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        app.Time.Advance(Limits.TellOwnerTtl + TimeSpan.FromMinutes(1));
        Assert.Empty(await app.PutTellCharacterAsync(other.InstallToken, A));
    }

    [Fact]
    public async Task PutCharacter_FreedAfterTheOwnerUnregisters()
    {
        using var app = new RelayApp();
        var owner = await app.RegisterInstallAsync();
        var other = await app.RegisterInstallAsync();
        await app.PutTellCharacterAsync(owner.InstallToken, A);
        await app.Client(owner.InstallToken).DeleteAsync("/api/tells/characters");

        Assert.Empty(await app.PutTellCharacterAsync(other.InstallToken, A));
    }
}
