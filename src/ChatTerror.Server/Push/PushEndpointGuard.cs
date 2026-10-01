using System.Net;
using System.Net.Sockets;

namespace ChatTerror.Server.Push;

// Keeps the relay from being pointed at its own network through a push endpoint.
public static class PushEndpointGuard
{
    private static readonly (IPAddress Network, int Prefix)[] BlockedRanges =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("224.0.0.0"), 3),
        (IPAddress.Parse("::"), 128),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("ff00::"), 8),
    ];

    public static async Task<bool> IsAllowedAsync(Uri endpoint, CancellationToken ct = default)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps)
            return false;

        IPAddress[] addresses;
        if (IPAddress.TryParse(endpoint.DnsSafeHost, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(endpoint.DnsSafeHost, ct);
            }
            catch (SocketException)
            {
                return false;
            }
        }
        return addresses.Length > 0 && addresses.All(IsPublic);
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return !BlockedRanges.Any(range => range.Network.AddressFamily == address.AddressFamily
            && new IPNetwork(range.Network, range.Prefix).Contains(address));
    }
}
