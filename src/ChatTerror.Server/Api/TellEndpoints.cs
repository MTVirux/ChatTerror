using ChatTerror.Protocol;
using ChatTerror.Server.Data;

namespace ChatTerror.Server.Api;

public static class TellEndpoints
{
    public static void MapTellEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/tells/bundle", (SignedTellBundle? body, HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();
            if (body == null || TellBundles.Verify(body, install.PublicKey) is not { } bundle || !HasOwnTargets(bundle, install.Id, store))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidBundle");

            store.SetTellBundle(install.Id, body);
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapDelete("/api/tells/bundle", (HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();

            store.DeleteTellBundle(install.Id);
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.TellPolicy);

        app.MapGet("/api/tells/bundles/{installId}", (string installId, HttpContext context, RelayStore store) =>
        {
            var callerInstall = AuthHelpers.Install(context, store)?.Id
                ?? (AuthHelpers.Device(context, store) is { Status: DeviceStatus.Active } device ? device.InstallId : null);
            if (callerInstall == null)
                return AuthHelpers.Unauthorized();

            // Same answer for strangers as for friends without a bundle, so it doesn't reveal who uses ChatTerror.
            var owner = installId == "self" ? callerInstall : installId;
            return (owner == callerInstall || store.AreFriends(callerInstall, owner)) && store.FindTellBundle(owner) is { } bundle
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
