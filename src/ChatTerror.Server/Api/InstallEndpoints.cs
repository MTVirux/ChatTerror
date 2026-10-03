using ChatTerror.Protocol;
using ChatTerror.Server.Data;
using ChatTerror.Server.Push;

namespace ChatTerror.Server.Api;

public sealed record CreateInstallRequest(string? PublicKey, string? Proof);

public sealed record CreateInstallResponse(string InstallId, string InstallToken);

public sealed record VapidResponse(string PublicKey);

public static class InstallEndpoints
{
    public static void MapInstallEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/installs", (CreateInstallRequest? body, RelayStore store) =>
        {
            if (body?.PublicKey is not { } publicKey || !AuthHelpers.IsPublicKey(publicKey))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidPublicKey");

            // Only the key's holder may register it again, e.g. after losing the install token. Anyone else could pair
            // with that key's owner and pass tells off as the owner's own.
            var proven = body.Proof is { } proof && InstallSignature.VerifyProof(publicKey, proof);
            var (status, install) = store.CreateInstall(publicKey, proven);
            return status switch
            {
                CreateInstallStatus.KeyInUse => AuthHelpers.Error(StatusCodes.Status409Conflict, "keyInUse"),
                CreateInstallStatus.LimitReached => AuthHelpers.Error(StatusCodes.Status503ServiceUnavailable, "installLimitReached"),
                _ => Results.Ok(new CreateInstallResponse(install!.Value.Id, install.Value.Token)),
            };
        }).RequireRateLimiting(RequestLimits.InstallPolicy);

        app.MapGet("/api/vapid", (VapidKeys keys) => Results.Ok(new VapidResponse(keys.PublicKey)));
    }
}
