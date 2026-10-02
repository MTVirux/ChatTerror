using ChatTerror.Server.Data;
using ChatTerror.Server.Push;

namespace ChatTerror.Server.Api;

public sealed record CreateInstallRequest(string? PublicKey);

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

            if (store.CreateInstall(publicKey) is not { } install)
                return AuthHelpers.Error(StatusCodes.Status503ServiceUnavailable, "installLimitReached");
            return Results.Ok(new CreateInstallResponse(install.Id, install.Token));
        }).RequireRateLimiting(RequestLimits.InstallPolicy);

        app.MapGet("/api/vapid", (VapidKeys keys) => Results.Ok(new VapidResponse(keys.PublicKey)));
    }
}
