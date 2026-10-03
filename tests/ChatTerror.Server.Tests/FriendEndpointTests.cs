using System.Net;
using System.Net.Http.Json;
using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ChatTerror.Server.Tests;

public class FriendEndpointTests
{
    private static readonly string A = TellHash.Compute(1);

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())?.Error;

    [Fact]
    public async Task Invite_LookupReturnsKeyScopeAndTag()
    {
        using var app = new RelayApp();
        using var keyA = P256.Generate();
        var a = await app.RegisterInstallAsync(keyA);
        var b = await app.RegisterInstallAsync();
        var tag = RelayApp.NewTag();

        var created = await app.Client(a.InstallToken).PostAsJsonAsync("/api/friends/invites", new { scope = A, tag });
        var invite = (await created.Content.ReadFromJsonAsync<FriendInviteResponse>())!;
        var info = await app.Client(b.InstallToken).GetFromJsonAsync<FriendInviteInfo>($"/api/friends/invites/{invite.Id}");

        Assert.Equal(new FriendInviteInfo(a.InstallId, Base64Url.Encode(P256.PublicRaw(keyA)), A, tag), info);
        Assert.Equal(app.Time.GetUtcNow().Add(Limits.FriendInviteTtl).ToUnixTimeMilliseconds(), invite.ExpiresAt);
        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", invite.Id);
        Assert.Equal(HttpStatusCode.OK, (await app.Client(b.InstallToken).GetAsync($"/api/friends/invites/{invite.Id.ToLowerInvariant()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync($"/api/friends/invites/{invite.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync("/api/friends/invites/0000-0000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync($"/api/friends/invites/{invite.Id[..9]}")).StatusCode);
    }

    [Fact]
    public async Task Invite_RejectsBadScopeAndTag()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var client = app.Client(a.InstallToken);

        var badScope = await client.PostAsJsonAsync("/api/friends/invites", new { scope = "nope", tag = RelayApp.NewTag() });
        var badTag = await client.PostAsJsonAsync("/api/friends/invites", new { scope = "*", tag = "short" });
        var missing = await client.PostAsJsonAsync("/api/friends/invites", new { });

        Assert.Equal(HttpStatusCode.BadRequest, badScope.StatusCode);
        Assert.Equal("invalidScope", await ErrorAsync(badScope));
        Assert.Equal(HttpStatusCode.BadRequest, badTag.StatusCode);
        Assert.Equal("invalidTag", await ErrorAsync(badTag));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().PostAsJsonAsync("/api/friends/invites", new { scope = "*", tag = RelayApp.NewTag() })).StatusCode);
    }

    [Fact]
    public async Task Invite_CappedPerInstall()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        for (var i = 0; i < Limits.MaxFriendInvites; i++)
            await app.CreateFriendInviteAsync(a.InstallToken);

        var extra = await app.Client(a.InstallToken).PostAsJsonAsync("/api/friends/invites", new { scope = "*", tag = RelayApp.NewTag() });

        Assert.Equal(HttpStatusCode.Conflict, extra.StatusCode);
        Assert.Equal("tooManyInvites", await ErrorAsync(extra));
    }

    [Fact]
    public async Task Invite_ExpiresAndIsSwept()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var invite = await app.CreateFriendInviteAsync(a.InstallToken);

        app.Time.Advance(Limits.FriendInviteTtl + TimeSpan.FromMilliseconds(1));

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).GetAsync($"/api/friends/invites/{invite.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendClaimAsync(b.InstallToken, invite.Id)).StatusCode);
        Assert.Empty((await app.GetFriendsAsync(a.InstallToken)).Invites);
        var store = app.Services.GetRequiredService<RelayStore>();
        Assert.Equal(1, store.DeleteExpiredFriendInvites());
        Assert.Equal(0, store.CountFriendInvites(a.InstallId));
    }

    [Fact]
    public async Task Claim_NotifiesInviterAndIsSingleUse()
    {
        using var app = new RelayApp();
        using var keyB = P256.Generate();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync(keyB);
        var c = await app.RegisterInstallAsync();
        await using var pluginA = await app.ConnectAsync(a.InstallToken);
        var invite = await app.CreateFriendInviteAsync(a.InstallToken);

        var self = await app.PostFriendClaimAsync(a.InstallToken, invite.Id);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("selfInvite", await ErrorAsync(self));

        Assert.Equal(HttpStatusCode.Accepted, (await app.PostFriendClaimAsync(b.InstallToken, invite.Id, "sealedB")).StatusCode);
        await pluginA.ReceiveAsync<FriendsChangedFrame>();

        var second = await app.PostFriendClaimAsync(c.InstallToken, invite.Id);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("alreadyClaimed", await ErrorAsync(second));
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(c.InstallToken).GetAsync($"/api/friends/invites/{invite.Id}")).StatusCode);

        var listed = Assert.Single((await app.GetFriendsAsync(a.InstallToken)).Invites);
        Assert.Equal(new FriendClaimInfo(b.InstallId, Base64Url.Encode(P256.PublicRaw(keyB)), "sealedB"), listed.Claim);
        Assert.Equal(invite.Id, listed.Id);
        Assert.Equal(FriendScopes.Account, listed.Scope);
        Assert.Empty((await app.GetFriendsAsync(b.InstallToken)).Invites);
    }

    [Fact]
    public async Task Claim_RejectsBadEnvelopeAndUnknownInvite()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var invite = await app.CreateFriendInviteAsync(a.InstallToken);

        var empty = await app.PostFriendClaimAsync(b.InstallToken, invite.Id, "");
        var tooLong = await app.PostFriendClaimAsync(b.InstallToken, invite.Id, new string('x', Limits.MaxFriendEnvelopeChars + 1));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("invalidEnvelope", await ErrorAsync(empty));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendClaimAsync(b.InstallToken, "0000-0000-0000")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendClaimAsync(b.InstallToken, invite.Id[..9])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendClaimAsync(b.InstallToken, "garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().PostAsJsonAsync($"/api/friends/invites/{invite.Id}/claim", new { @sealed = "x" })).StatusCode);
    }

    [Fact]
    public async Task Accept_CreatesFriendshipAndNotifiesBoth()
    {
        using var app = new RelayApp();
        using var keyA = P256.Generate();
        using var keyB = P256.Generate();
        var a = await app.RegisterInstallAsync(keyA);
        var b = await app.RegisterInstallAsync(keyB);
        var c = await app.RegisterInstallAsync();
        await using var pluginA = await app.ConnectAsync(a.InstallToken);
        await using var pluginB = await app.ConnectAsync(b.InstallToken);
        var invite = await app.CreateFriendInviteAsync(a.InstallToken);

        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendAcceptAsync(a.InstallToken, invite.Id)).StatusCode);
        await app.PostFriendClaimAsync(b.InstallToken, invite.Id);
        await pluginA.ReceiveAsync<FriendsChangedFrame>();
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendAcceptAsync(b.InstallToken, invite.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendAcceptAsync(c.InstallToken, invite.Id)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await app.PostFriendAcceptAsync(a.InstallToken, invite.Id)).StatusCode);

        await pluginA.ReceiveAsync<FriendsChangedFrame>();
        await pluginB.ReceiveAsync<FriendsChangedFrame>();
        var friendsA = await app.GetFriendsAsync(a.InstallToken);
        var friendsB = await app.GetFriendsAsync(b.InstallToken);
        Assert.Equal(new FriendEntry(b.InstallId, Base64Url.Encode(P256.PublicRaw(keyB)), null), Assert.Single(friendsA.Friends));
        Assert.Equal(new FriendEntry(a.InstallId, Base64Url.Encode(P256.PublicRaw(keyA)), null), Assert.Single(friendsB.Friends));
        Assert.Empty(friendsA.Invites);
        Assert.Empty((await app.GetFriendsAsync(c.InstallToken)).Friends);

        var again = await app.CreateFriendInviteAsync(a.InstallToken);
        var already = await app.PostFriendClaimAsync(b.InstallToken, again.Id);
        Assert.Equal(HttpStatusCode.Conflict, already.StatusCode);
        Assert.Equal("alreadyFriends", await ErrorAsync(already));
    }

    [Fact]
    public async Task Friends_CappedPerInstall()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var store = app.Services.GetRequiredService<RelayStore>();
        var invite = await app.CreateFriendInviteAsync(a.InstallToken);
        await app.PostFriendClaimAsync(b.InstallToken, invite.Id);
        for (var i = 0; i < Limits.MaxPairedFriends; i++)
            store.AddFriend(a.InstallId, $"other{i}");

        var accept = await app.PostFriendAcceptAsync(a.InstallToken, invite.Id);
        var claim = await app.PostFriendClaimAsync(b.InstallToken, (await app.CreateFriendInviteAsync(a.InstallToken)).Id);

        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        Assert.Equal("tooManyFriends", await ErrorAsync(accept));
        Assert.Equal(HttpStatusCode.Conflict, claim.StatusCode);
        Assert.Equal("tooManyFriends", await ErrorAsync(claim));
    }

    [Fact]
    public async Task DeleteInvite_OwnerOnlyAndNotifiesClaimant()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        await using var pluginB = await app.ConnectAsync(b.InstallToken);
        var invite = await app.CreateFriendInviteAsync(a.InstallToken);
        await app.PostFriendClaimAsync(b.InstallToken, invite.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await app.Client(b.InstallToken).DeleteAsync($"/api/friends/invites/{invite.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).DeleteAsync($"/api/friends/invites/{invite.Id}")).StatusCode);

        await pluginB.ReceiveAsync<FriendsChangedFrame>();
        Assert.Empty((await app.GetFriendsAsync(a.InstallToken)).Invites);
        Assert.Equal(HttpStatusCode.NotFound, (await app.PostFriendAcceptAsync(a.InstallToken, invite.Id)).StatusCode);
    }

    [Fact]
    public async Task Profile_OnlyBetweenFriendsAndOnlyForItsRecipient()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var c = await app.RegisterInstallAsync();
        await app.PairFriendsAsync(a, b);
        await app.PairFriendsAsync(a, c);

        var stranger = await app.Client(b.InstallToken).PutAsJsonAsync($"/api/friends/{c.InstallId}/profile", new { envelope = "x" });
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(TellErrors.NotPaired, await ErrorAsync(stranger));
        var tooLong = await app.Client(a.InstallToken).PutAsJsonAsync($"/api/friends/{b.InstallId}/profile", new { envelope = new string('x', Limits.MaxFriendEnvelopeChars + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("invalidEnvelope", await ErrorAsync(tooLong));

        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).PutAsJsonAsync($"/api/friends/{b.InstallId}/profile", new { envelope = "forB" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).PutAsJsonAsync($"/api/friends/{b.InstallId}/profile", new { envelope = "forB2" })).StatusCode);

        Assert.Equal("forB2", Assert.Single((await app.GetFriendsAsync(b.InstallToken)).Friends).Profile);
        Assert.Null(Assert.Single((await app.GetFriendsAsync(c.InstallToken)).Friends).Profile);
        Assert.All((await app.GetFriendsAsync(a.InstallToken)).Friends, friend => Assert.Null(friend.Profile));
    }

    [Fact]
    public async Task Profile_NotifiesTheRecipientsPlugin()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        await app.PairFriendsAsync(a, b);
        await using var pluginB = await app.ConnectAsync(b.InstallToken);
        await pluginB.BarrierAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).PutAsJsonAsync($"/api/friends/{b.InstallId}/profile", new { envelope = "forB" })).StatusCode);

        await pluginB.ReceiveAsync<FriendsChangedFrame>();
    }

    [Fact]
    public async Task RemoveFriend_RefusesOwnInstall()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var store = app.Services.GetRequiredService<RelayStore>();
        store.EnqueueTell("copy", a.InstallId, TellTargets.Plugin, a.InstallId, "key", "env");

        var response = await app.Client(a.InstallToken).DeleteAsync($"/api/friends/{a.InstallId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(["copy"], store.PendingTells(a.InstallId, TellTargets.Plugin).Select(tell => tell.Id));
    }

    [Fact]
    public async Task RemoveFriend_DropsProfilesAndQueuedTellsBothWays()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var c = await app.RegisterInstallAsync();
        await app.PairFriendsAsync(a, b);
        await app.PairFriendsAsync(a, c);
        await app.Client(a.InstallToken).PutAsJsonAsync($"/api/friends/{b.InstallId}/profile", new { envelope = "forB" });
        await app.Client(b.InstallToken).PutAsJsonAsync($"/api/friends/{a.InstallId}/profile", new { envelope = "forA" });
        var store = app.Services.GetRequiredService<RelayStore>();
        store.EnqueueTell("ab", b.InstallId, TellTargets.Plugin, a.InstallId, "key", "env");
        store.EnqueueTell("ba", a.InstallId, TellTargets.Plugin, b.InstallId, "key", "env");
        store.EnqueueTell("ca", a.InstallId, TellTargets.Plugin, c.InstallId, "key", "env");
        await using var pluginB = await app.ConnectAsync(b.InstallToken);
        await pluginB.BarrierAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).DeleteAsync($"/api/friends/{b.InstallId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Client(a.InstallToken).DeleteAsync($"/api/friends/{b.InstallId}")).StatusCode);

        await pluginB.ReceiveAsync<FriendsChangedFrame>();
        Assert.False(store.AreFriends(a.InstallId, b.InstallId));
        Assert.Empty((await app.GetFriendsAsync(b.InstallToken)).Friends);
        Assert.Single((await app.GetFriendsAsync(a.InstallToken)).Friends);
        Assert.Empty(store.PendingTells(b.InstallId, TellTargets.Plugin));
        Assert.Equal(["ca"], store.PendingTells(a.InstallId, TellTargets.Plugin).Select(tell => tell.Id));

        await app.PairFriendsAsync(a, b);
        Assert.All((await app.GetFriendsAsync(a.InstallToken)).Friends, friend => Assert.Null(friend.Profile));
        Assert.All((await app.GetFriendsAsync(b.InstallToken)).Friends, friend => Assert.Null(friend.Profile));
    }

    [Fact]
    public async Task DeletedInstall_LosesFriendshipsInvitesProfilesAndTells()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var c = await app.RegisterInstallAsync();
        await app.PairFriendsAsync(a, b);
        await app.Client(a.InstallToken).PutAsJsonAsync($"/api/friends/{b.InstallId}/profile", new { envelope = "forB" });
        await app.Client(b.InstallToken).PutAsJsonAsync($"/api/friends/{a.InstallId}/profile", new { envelope = "forA" });
        var store = app.Services.GetRequiredService<RelayStore>();
        store.EnqueueTell("ab", b.InstallId, TellTargets.Plugin, a.InstallId, "key", "env");
        var claimed = await app.CreateFriendInviteAsync(c.InstallToken);
        await app.PostFriendClaimAsync(a.InstallToken, claimed.Id);

        app.Time.Advance(TimeSpan.FromDays(31));
        store.TouchInstall(b.InstallId);
        store.TouchInstall(c.InstallId);
        app.Services.GetRequiredService<ExpiryService>().Sweep();

        Assert.Null(store.InstallPublicKey(a.InstallId));
        Assert.Empty((await app.GetFriendsAsync(b.InstallToken)).Friends);
        Assert.Empty(store.PendingTells(b.InstallId, TellTargets.Plugin));
        Assert.Equal(0, store.CountFriends(b.InstallId));
    }

    [Fact]
    public async Task DeleteFriendData_RemovesInvitesAndClaims()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        var b = await app.RegisterInstallAsync();
        var store = app.Services.GetRequiredService<RelayStore>();
        await app.CreateFriendInviteAsync(a.InstallToken);
        var claimed = await app.CreateFriendInviteAsync(b.InstallToken);
        await app.PostFriendClaimAsync(a.InstallToken, claimed.Id);

        store.DeleteFriendData(a.InstallId);

        Assert.Equal(0, store.CountFriendInvites(a.InstallId));
        Assert.Empty((await app.GetFriendsAsync(b.InstallToken)).Invites);
    }

    [Fact]
    public async Task FriendEndpoints_NeedAnInstallToken()
    {
        using var app = new RelayApp();
        var a = await app.RegisterInstallAsync();
        await using var plugin = await app.ConnectAsync(a.InstallToken);
        var device = await app.PairDeviceAsync(a, plugin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(device.DeviceToken).GetAsync("/api/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client().GetAsync("/api/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(device.DeviceToken).DeleteAsync($"/api/friends/{a.InstallId}")).StatusCode);
    }
}
