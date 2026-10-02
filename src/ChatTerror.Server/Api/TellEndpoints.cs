using ChatTerror.Protocol;
using ChatTerror.Server.Data;

namespace ChatTerror.Server.Api;

public sealed record TellCharacterRequest(List<string>? Friends);

public sealed record TellCharacterResponse(List<string> Registered);

public static class TellEndpoints
{
    public static void MapTellEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/tells/characters/{hash}", (string hash, TellCharacterRequest? body, HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (!TellHash.IsValid(hash))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidHash");
            if (body?.Friends is not { Count: <= Limits.MaxFriends } friends || !friends.All(TellHash.IsValid))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidFriends");
            if (store.TellCharacterOwner(hash) != install.Id && store.CountTellCharacters(install.Id) >= Limits.MaxTellCharacters)
                return AuthHelpers.Error(StatusCodes.Status409Conflict, "tooManyCharacters");

            store.TouchInstall(install.Id);
            var distinct = friends.Distinct().ToList();
            if (!store.SetTellCharacter(install.Id, hash, distinct))
                return AuthHelpers.Error(StatusCodes.Status409Conflict, "characterTaken");
            return Results.Ok(new TellCharacterResponse(store.MutualTellFriends(hash, distinct)));
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapDelete("/api/tells/characters", (HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();

            store.DeleteTellCharacters(install.Id);
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapPut("/api/tells/bundle", (SignedTellBundle? body, HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (body == null || TellBundles.Verify(body, install.PublicKey) is not { } bundle || !HasOwnTargets(bundle, install.Id, store))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidBundle");

            store.SetTellBundle(install.Id, body);
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapGet("/api/tells/bundles/{hash}", (string hash, HttpContext context, RelayStore store) =>
        {
            var callerInstall = AuthHelpers.Install(context, store)?.Id ?? AuthHelpers.Device(context, store)?.InstallId;
            if (callerInstall == null)
                return AuthHelpers.Unauthorized();

            var owner = hash == "self" ? callerInstall : store.CanSeeTellCharacter(callerInstall, hash) ? store.TellCharacterOwner(hash) : null;
            return owner != null && store.FindTellBundle(owner) is { } bundle
                ? Results.Json(bundle, ProtocolJson.Options)
                : AuthHelpers.Error(StatusCodes.Status404NotFound, TellErrors.NotChatTerror);
        }).RequireRateLimiting(RequestLimits.TellPolicy);
    }

    private static bool HasOwnTargets(TellBundle bundle, string installId, RelayStore store)
    {
        var devices = store.ListDevices(installId).Select(d => d.Id).ToHashSet();
        return bundle.Entries.Count <= Limits.MaxDevices + 1
            && bundle.Entries.All(e => (e.Target == TellTargets.Plugin || devices.Contains(e.Target)) && AuthHelpers.IsPublicKey(e.Key));
    }
}
