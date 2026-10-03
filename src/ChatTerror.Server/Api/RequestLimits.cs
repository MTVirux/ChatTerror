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
    public const string PortraitPolicy = "portraits";

    // IPv6 clients usually own at least a whole /64, so one client is one /64.
    public static string PartitionKey(IPAddress? address) => PartitionKey(address, 64);

    // Home connections are commonly given a /56, so install creation is limited per /56 to make large
    // prefixes less useful without lumping a whole ISP into one bucket.
    public static string InstallPartitionKey(IPAddress? address) => PartitionKey(address, 56);

    private static string PartitionKey(IPAddress? address, int ipv6Prefix)
    {
        if (address == null)
            return "unknown";
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = new byte[16];
        address.GetAddressBytes()[..(ipv6Prefix / 8)].CopyTo(bytes, 0);
        return new IPAddress(bytes) + "/" + ipv6Prefix;
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
