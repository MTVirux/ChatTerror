using ChatTerror.Protocol;
using ChatTerror.Server.Auth;
using ChatTerror.Server.Data;
using ChatTerror.Server.Relay;

namespace ChatTerror.Server.Api;

public sealed record CreatePairingResponse(string Code, long ExpiresAt);

public sealed record PairingInfoResponse(string InstallId, string PluginPublicKey);

public sealed record ClaimRequest(string? DevicePublicKey, string? DeviceName);

public sealed record ClaimResponse(string DeviceId, string DeviceToken);

public static class PairingEndpoints
{
    private const int MaxDeviceNameLength = 64;

    public static void MapPairingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/pairings", (HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();

            var (code, expiresAt) = store.CreatePairing(install.Id);
            return Results.Ok(new CreatePairingResponse(code, expiresAt));
        });

        app.MapGet("/api/pairings/{code}", (string code, RelayStore store) =>
        {
            if (PairingCodes.Normalize(code) is not { } normalized || store.FindPairing(normalized) is not { } pairing)
                return AuthHelpers.NotFound();
            return Results.Ok(new PairingInfoResponse(pairing.InstallId, pairing.PluginPublicKey));
        }).RequireRateLimiting(RequestLimits.PairingPolicy);

        app.MapPost("/api/pairings/{code}/claim", (string code, ClaimRequest? body, RelayStore store, ConnectionRegistry registry) =>
        {
            var name = body?.DeviceName?.Trim() ?? "";
            if (body?.DevicePublicKey is not { } publicKey || !AuthHelpers.IsPublicKey(publicKey))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidPublicKey");
            if (name.Length is 0 or > MaxDeviceNameLength || name.Any(char.IsControl))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidName");
            if (PairingCodes.Normalize(code) is not { } normalized)
                return AuthHelpers.NotFound();

            var result = store.ClaimPairing(normalized, publicKey, name);
            if (result.Status == ClaimStatus.TooManyDevices)
                return AuthHelpers.Error(StatusCodes.Status409Conflict, "tooManyDevices");
            if (result is not { Device: { } device, Token: { } token })
                return AuthHelpers.NotFound();

            registry.Plugin(device.InstallId)?.Send(new PairRequestFrame(device.Id, device.Name, device.PublicKey));
            return Results.Ok(new ClaimResponse(device.Id, token));
        }).RequireRateLimiting(RequestLimits.PairingPolicy);
    }
}
