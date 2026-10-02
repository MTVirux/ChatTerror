using System.Net;
using System.Net.Sockets;

namespace ChatTerror.Server.Push;

// Keeps the relay from being pointed at its own network, or at arbitrary hosts, through a push endpoint.
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
        (IPAddress.Parse("64:ff9b::"), 96),
        (IPAddress.Parse("2001::"), 32),
        (IPAddress.Parse("2002::"), 16),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("ff00::"), 8),
    ];

    public static async Task<bool> IsAllowedAsync(Uri endpoint, IReadOnlyList<string> serviceHosts, CancellationToken ct = default) =>
        IsPushService(endpoint, serviceHosts) && PickAddress(await ResolveAsync(endpoint.DnsSafeHost, ct)) != null;

    public static bool IsPushService(string endpoint, IReadOnlyList<string> serviceHosts) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && IsPushService(uri, serviceHosts);

    public static bool IsPushService(Uri endpoint, IReadOnlyList<string> serviceHosts) =>
        endpoint.Scheme == Uri.UriSchemeHttps
        && endpoint.Port == 443
        && serviceHosts.Any(pattern => HostMatches(endpoint.DnsSafeHost, pattern));

    // "*.example.com" only matches below a dot, so "evilexample.com" and "example.com.evil.net" do not.
    public static bool HostMatches(string host, string pattern)
    {
        if (!pattern.StartsWith("*."))
            return string.Equals(host, pattern, StringComparison.OrdinalIgnoreCase);

        var suffix = pattern[1..];
        return host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    // Unresolvable hosts come back empty, which PickAddress rejects.
    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal))
            return [literal];
        try
        {
            return await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return [];
        }
    }

    // One non-public address rejects the host, so DNS cannot mix in an internal target.
    public static IPAddress? PickAddress(IReadOnlyList<IPAddress> addresses) =>
        addresses.Count > 0 && addresses.All(IsPublic) ? addresses[0] : null;

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return !BlockedRanges.Any(range => range.Network.AddressFamily == address.AddressFamily
            && new IPNetwork(range.Network, range.Prefix).Contains(address));
    }
}
