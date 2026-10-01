using ChatTerror.Server.Push;

namespace ChatTerror.Server.Tests;

public class PushEndpointGuardTests
{
    [Theory]
    [InlineData("https://1.1.1.1/sub")]
    [InlineData("https://8.8.8.8:8443/sub")]
    [InlineData("https://[2606:4700:4700::1111]/sub")]
    public async Task PublicHttpsEndpoint_Allowed(string endpoint)
    {
        Assert.True(await PushEndpointGuard.IsAllowedAsync(new Uri(endpoint)));
    }

    [Theory]
    [InlineData("http://1.1.1.1/sub")]
    [InlineData("https://127.0.0.1/sub")]
    [InlineData("https://0.0.0.0/sub")]
    [InlineData("https://10.0.0.1/sub")]
    [InlineData("https://172.16.5.4/sub")]
    [InlineData("https://192.168.1.1/sub")]
    [InlineData("https://100.64.0.1/sub")]
    [InlineData("https://169.254.169.254/sub")]
    [InlineData("https://224.0.0.1/sub")]
    [InlineData("https://255.255.255.255/sub")]
    [InlineData("https://[::1]/sub")]
    [InlineData("https://[::]/sub")]
    [InlineData("https://[fe80::1]/sub")]
    [InlineData("https://[fc00::1]/sub")]
    [InlineData("https://[fd12:3456::1]/sub")]
    [InlineData("https://[ff02::1]/sub")]
    [InlineData("https://[::ffff:127.0.0.1]/sub")]
    [InlineData("https://[::ffff:192.168.0.1]/sub")]
    public async Task NonPublicOrPlainEndpoint_Rejected(string endpoint)
    {
        Assert.False(await PushEndpointGuard.IsAllowedAsync(new Uri(endpoint)));
    }

    [Fact]
    public async Task LocalhostName_Rejected()
    {
        Assert.False(await PushEndpointGuard.IsAllowedAsync(new Uri("https://localhost/sub")));
    }
}
