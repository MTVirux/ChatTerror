using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http.Features;

namespace ChatTerror.Server.Api;

public static class RequestLimits
{
    public const string PairingPolicy = "pairing";
    public const string InstallPolicy = "installs";
    public const string SocketPolicy = "sockets";
    public const string PushPolicy = "push";
    public const string TellPolicy = "tells";

    // IPv6 clients usually own a whole /64, so one client is one /64.
    public static string PartitionKey(IPAddress? address)
    {
        if (address == null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var prefix = address.GetAddressBytes()[..8];
        return new IPAddress([.. prefix, 0, 0, 0, 0, 0, 0, 0, 0]) + "/64";
    }

    public static IApplicationBuilder UseApiBodyLimit(this IApplicationBuilder app, long maxBytes) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                if (context.Request.ContentLength > maxBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    await context.Response.WriteAsJsonAsync(new ErrorBody("tooLarge"));
                    return;
                }

                // Covers chunked bodies on Kestrel, where the length is not known up front.
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
                    feature.MaxRequestBodySize = maxBytes;
            }
            await next(context);
        });
}
