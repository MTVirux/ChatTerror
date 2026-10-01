using System;
using System.Net;

namespace ChatTerror.Plugin.Logic;

public static class RelayUrl
{
    public const string InsecureError = "Plain http:// is only allowed for a relay on this computer (localhost). Use https://.";

    // Plain http/ws would send the install token in cleartext, so it is only allowed to loopback hosts.
    public static bool IsAllowed(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeWss
        || ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeWs) && IsLoopback(uri));

    public static bool IsAllowed(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && IsAllowed(uri);

    // Uri lowercases scheme and host, so equal relays normalize to the same string. Null for anything but http(s).
    public static string? Normalize(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
    }

    private static bool IsLoopback(Uri uri) =>
        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip));
}
