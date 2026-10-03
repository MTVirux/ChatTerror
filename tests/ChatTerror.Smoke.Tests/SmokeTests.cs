using System.Net.Http.Json;
using System.Net.WebSockets;
using ChatTerror.Protocol;

namespace ChatTerror.Smoke.Tests;

// The relay allows few installs per client IP per hour, so the suite registers only two.
public class SmokeTests
{
    [Fact]
    public async Task IndexPage_ServesWebAppWithSecurityHeaders()
    {
        using var client = LiveRelay.Client();

        var response = await client.GetAsync("/");

        Assert.Contains("<div id=\"app\">", await LiveRelay.ReadTextAsync(response));
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'self'", Header(response, "Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
    }

    [Fact]
    public async Task Vapid_ReturnsPublicKey()
    {
        using var client = LiveRelay.Client();

        var vapid = await LiveRelay.ReadAsync<VapidResponse>(await client.GetAsync("/api/vapid"));

        Assert.Equal(65, Base64Url.Decode(vapid.PublicKey).Length);
    }

    [Fact]
    public async Task PairingFlow_DeviceClaimsCode()
    {
        var publicKey = LiveRelay.NewPublicKey();
        var install = await LiveRelay.RegisterInstallAsync(publicKey);
        using var plugin = LiveRelay.Client(install.InstallToken);
        using var phone = LiveRelay.Client();

        var pairing = await LiveRelay.ReadAsync<PairingResponse>(await plugin.PostAsJsonAsync("/api/pairings", new { }));
        var info = await LiveRelay.ReadAsync<PairingInfo>(await phone.GetAsync($"/api/pairings/{pairing.Code}"));
        var claim = await LiveRelay.ReadAsync<ClaimResponse>(await phone.PostAsJsonAsync($"/api/pairings/{pairing.Code}/claim",
            new { devicePublicKey = LiveRelay.NewPublicKey(), deviceName = "Smoke test" }));

        Assert.Equal(new PairingInfo(install.InstallId, publicKey), info);
        Assert.StartsWith($"d.{claim.DeviceId}.", claim.DeviceToken);
    }

    [Fact]
    public async Task PluginSocket_AuthenticatesWithInstallToken()
    {
        var install = await LiveRelay.RegisterInstallAsync(LiveRelay.NewPublicKey());
        using var socket = await LiveRelay.OpenSocketAsync();

        await LiveRelay.SendAsync(socket, new AuthFrame(install.InstallToken));

        Assert.Equal(new AuthOkFrame(RelayRoles.Plugin, install.InstallId), await LiveRelay.ReceiveAsync(socket));
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : "";
}
