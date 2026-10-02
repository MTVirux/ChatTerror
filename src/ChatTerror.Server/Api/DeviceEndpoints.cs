using ChatTerror.Server.Data;
using ChatTerror.Server.Push;
using ChatTerror.Server.Relay;
using Microsoft.Extensions.Options;

namespace ChatTerror.Server.Api;

public sealed record DeviceResponse(string DeviceId, string Name, string Status, long LastSeen, string PublicKey);

public sealed record DeviceMeResponse(string DeviceId, string Status);

public sealed record PushKeys(string? P256dh, string? Auth);

public sealed record PushRequest(string? Endpoint, PushKeys? Keys);

public static class DeviceEndpoints
{
    public const int MaxEndpointLength = 1024;
    private const int MaxKeyLength = 256;

    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/devices", (HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Install(context, store) is not { } install)
                return AuthHelpers.Unauthorized();

            var devices = store.ListDevices(install.Id)
                .Select(d => new DeviceResponse(d.Id, d.Name, d.Status, d.LastSeen, d.PublicKey));
            return Results.Ok(devices);
        });

        app.MapGet("/api/devices/me", (HttpContext context, RelayStore store) =>
            AuthHelpers.Device(context, store) is { } device
                ? Results.Ok(new DeviceMeResponse(device.Id, device.Status))
                : AuthHelpers.Unauthorized());

        app.MapDelete("/api/devices/{deviceId}", (string deviceId, HttpContext context, RelayStore store, ConnectionRegistry registry) =>
        {
            DeviceRecord? device;
            if (AuthHelpers.Install(context, store) is { } install)
            {
                device = store.FindDevice(deviceId);
                if (device == null || device.InstallId != install.Id)
                    return AuthHelpers.NotFound();
            }
            else if (AuthHelpers.Device(context, store) is { } caller)
            {
                if (deviceId != "me" && deviceId != caller.Id)
                    return AuthHelpers.Error(StatusCodes.Status403Forbidden, "forbidden");
                device = caller;
            }
            else
            {
                return AuthHelpers.Unauthorized();
            }

            store.DeleteDevice(device.Id);
            registry.Revoke(device.Id, device.InstallId, notifyPlugin: true);
            return Results.NoContent();
        });

        app.MapPut("/api/devices/me/push", async (PushRequest? body, HttpContext context, RelayStore store, IOptions<RelayOptions> options) =>
        {
            if (AuthHelpers.Device(context, store) is not { } device)
                return AuthHelpers.Unauthorized();

            if (body is not { Endpoint: { Length: <= MaxEndpointLength } url, Keys: { P256dh: { Length: > 0 and <= MaxKeyLength } p256dh, Auth: { Length: > 0 and <= MaxKeyLength } auth } }
                || !Uri.TryCreate(url, UriKind.Absolute, out var endpoint)
                || !await PushEndpointGuard.IsAllowedAsync(endpoint, options.Value.GetPushServiceHosts(), context.RequestAborted))
                return AuthHelpers.Error(StatusCodes.Status400BadRequest, "invalidSubscription");

            store.SetPush(device.Id, new PushSubscriptionRecord(url, p256dh, auth));
            return Results.NoContent();
        }).RequireRateLimiting(RequestLimits.PushPolicy);

        app.MapDelete("/api/devices/me/push", (HttpContext context, RelayStore store) =>
        {
            if (AuthHelpers.Device(context, store) is not { } device)
                return AuthHelpers.Unauthorized();

            store.ClearPush(device.Id);
            return Results.NoContent();
        });
    }
}
