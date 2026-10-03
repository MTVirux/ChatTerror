using ChatTerror.Protocol;
using ChatTerror.Server.Auth;
using ChatTerror.Server.Data;
using ChatTerror.Server.Relay;

namespace ChatTerror.Server.Api;

public sealed record CreateFriendInviteRequest(string? Scope, string? Tag);

public sealed record CreateFriendInviteResponse(string Id, long ExpiresAt);

public sealed record FriendInviteInfoResponse(string InstallId, string InstallPublicKey, string Scope, string Tag);

public sealed record FriendClaimRequest(string? Sealed);

public sealed record FriendProfileRequest(string? Envelope);

public sealed record FriendClaimResponse(string InstallId, string PublicKey, string Sealed);

public sealed record FriendInviteResponse(string Id, string Scope, long ExpiresAt, FriendClaimResponse? Claim);

public sealed record FriendResponse(string InstallId, string PublicKey, string? Profile);

public sealed record FriendsResponse(List<FriendResponse> Friends, List<FriendInviteResponse> Invites);

public static class FriendEndpoints
{
    public static void MapFriendEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/friends/invites", (CreateFriendInviteRequest? body, HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (!FriendScopes.IsValid(body?.Scope))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidScope");
            if (!IsTag(body!.Tag))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidTag");
            if (store.CountFriendInvites(install.Id) >= Limits.MaxFriendInvites)
                return AuthHelpers.Error(StatusCodes.Status409Conflict, "tooManyInvites");

            var (id, expiresAt) = store.CreateFriendInvite(install.Id, body.Scope!, body.Tag!);
            return Results.Ok(new CreateFriendInviteResponse(id, expiresAt));
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapGet("/api/friends/invites/{id}", (string id, HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is null)
                return AuthHelpers.Unauthorized();
            if (PairingCodes.Normalize(id) is not { } normalized || store.FindFriendInvite(normalized) is not { ClaimInstall: null } invite)
                return AuthHelpers.NotFound();
            return Results.Ok(new FriendInviteInfoResponse(invite.InstallId, invite.InstallPublicKey, invite.Scope, invite.Tag));
        }).RequireRateLimiting(RequestLimits.PairingPolicy);

        app.MapPost("/api/friends/invites/{id}/claim", (string id, FriendClaimRequest? body, HttpContext context, RelayStore store, ConnectionRegistry registry) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (!IsEnvelope(body?.Sealed))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidEnvelope");
            if (PairingCodes.Normalize(id) is not { } normalized)
                return AuthHelpers.NotFound();

            var (status, inviter) = store.ClaimFriendInvite(normalized, install.Id, body!.Sealed!);
            if (status != FriendInviteResult.Ok)
                return InviteError(status);

            registry.Plugin(inviter!)?.Send(new FriendsChangedFrame());
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }).RequireRateLimiting(RequestLimits.PairingPolicy);

        app.MapPost("/api/friends/invites/{id}/accept", (string id, HttpContext context, RelayStore store, ConnectionRegistry registry) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (PairingCodes.Normalize(id) is not { } normalized)
                return AuthHelpers.NotFound();

            var (status, claimant) = store.AcceptFriendInvite(normalized, install.Id);
            if (status != FriendInviteResult.Ok)
                return InviteError(status);

            registry.Plugin(install.Id)?.Send(new FriendsChangedFrame());
            registry.Plugin(claimant!)?.Send(new FriendsChangedFrame());
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapDelete("/api/friends/invites/{id}", (string id, HttpContext context, RelayStore store, ConnectionRegistry registry) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (PairingCodes.Normalize(id) is not { } normalized || store.DeleteFriendInvite(normalized, install.Id) is not { } invite)
                return AuthHelpers.NotFound();

            if (invite.ClaimInstall is { } claimant)
                registry.Plugin(claimant)?.Send(new FriendsChangedFrame());
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapGet("/api/friends", (HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();

            var friends = store.ListFriends(install.Id)
                .Select(friend => new FriendResponse(friend.InstallId, friend.PublicKey, friend.Profile))
                .ToList();
            var invites = store.ListFriendInvites(install.Id)
                .Select(invite => new FriendInviteResponse(invite.Id, invite.Scope, invite.ExpiresAt,
                    invite is { ClaimInstall: { } claimant, ClaimPublicKey: { } key, ClaimSealed: { } sealedClaim }
                        ? new FriendClaimResponse(claimant, key, sealedClaim)
                        : null))
                .ToList();
            return Results.Ok(new FriendsResponse(friends, invites));
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapPut("/api/friends/{installId}/profile", (string installId, FriendProfileRequest? body, HttpContext context, RelayStore store,
            ConnectionRegistry registry, FriendProfileLimiter limiter) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (!store.AreFriends(install.Id, installId))
                return AuthHelpers.Error(StatusCodes.Status404NotFound, TellErrors.NotPaired);
            if (!IsEnvelope(body?.Envelope))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidEnvelope");

            if (!limiter.TryAcquire(install.Id, installId))
                return AuthHelpers.Error(StatusCodes.Status429TooManyRequests, "rateLimited");

            if (store.SetFriendProfile(install.Id, installId, body!.Envelope!))
                registry.Plugin(installId)?.Send(new FriendsChangedFrame());
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapDelete("/api/friends/{installId}", (string installId, HttpContext context, RelayStore store, ConnectionRegistry registry) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (installId == install.Id)
                return AuthHelpers.NotFound();

            if (store.RemoveFriend(install.Id, installId))
                registry.Plugin(installId)?.Send(new FriendsChangedFrame());
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);
    }

    private static IResult InviteError(FriendInviteResult status) => status switch
    {
        FriendInviteResult.AlreadyClaimed => AuthHelpers.Error(StatusCodes.Status409Conflict, "alreadyClaimed"),
        FriendInviteResult.SelfInvite => AuthHelpers.Error(StatusCodes.Status409Conflict, "selfInvite"),
        FriendInviteResult.AlreadyFriends => AuthHelpers.Error(StatusCodes.Status409Conflict, "alreadyFriends"),
        FriendInviteResult.TooManyFriends => AuthHelpers.Error(StatusCodes.Status409Conflict, "tooManyFriends"),
        _ => AuthHelpers.NotFound(),
    };

    private static bool IsEnvelope(string? envelope) => envelope is { Length: > 0 and <= Limits.MaxFriendEnvelopeChars };

    private static bool IsTag(string? tag)
    {
        if (tag is not { Length: 43 })
            return false;
        try
        {
            return Base64Url.Decode(tag).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
